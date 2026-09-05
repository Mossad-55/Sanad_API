using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Caregivers;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Sanad.UnitTests.Families;

public sealed class BookingPaymentWebhookTests
{
    private static FamiliesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new FamiliesDbContext(options);
    }

    private static (FamiliesDbContext DbContext, Family Family, Elderly Elderly) SeedFamily()
    {
        var dbContext = CreateDbContext();
        var family = Family.Create(UserId.New(), "Webhook Family");

        var elderly = Elderly.Create(
            family.OwnerUserId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("مسن تجريبي"),
            FullName.Create("Elderly Test"),
            Gender.Male,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-70)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        dbContext.Families.Add(family);
        dbContext.Elderlies.Add(elderly);
        dbContext.SaveChanges();

        return (dbContext, family, elderly);
    }

    private sealed class FakePricing(BookingCaregiverType type, decimal fee) : ICaregiverBookingPricing
    {
        public Task<Result<CaregiverBookingPrice>> GetBookingPriceAsync(
            CaregiverId caregiverId,
            BookingShiftType shiftType,
            TimeOnly startTime,
            TimeOnly endTime,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<CaregiverBookingPrice>.Success(
                new CaregiverBookingPrice(type, fee)));
        }
    }

    private sealed class FakePaymobClient(
        PaymobPaymentIntent? intent,
        Error? intentError = null,
        string? refundId = "dev-refund-1",
        Error? refundError = null) : IPaymobClient
    {
        public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(
            PaymobPaymentIntentInput input,
            CancellationToken cancellationToken = default)
        {
            if (intentError is not null)
            {
                return Task.FromResult(Result<PaymobPaymentIntent>.Failure(intentError));
            }

            return Task.FromResult(Result<PaymobPaymentIntent>.Success(intent!));
        }

        public Task<Result<string?>> RefundPaymentAsync(
            string paymobTransactionId,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            if (refundError is not null)
            {
                return Task.FromResult(Result<string?>.Failure(refundError));
            }

            return Task.FromResult(Result<string?>.Success(refundId));
        }
    }

    private static async Task<Booking> CheckoutAsync(
        FamiliesDbContext dbContext,
        Family family,
        Elderly elderly)
    {
        var checkout = new CreateBookingCheckoutCommandHandler(
            dbContext,
            new FakePricing(BookingCaregiverType.Medical, 500m));

        var checkoutResult = await checkout.Handle(
            new CreateBookingCheckoutCommand(
                family.OwnerUserId,
                elderly.Id,
                CaregiverId.New(),
                BookingShiftType.HomeVisit,
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2),
                new TimeOnly(10, 0),
                new TimeOnly(12, 0),
                "123 Nile St, Cairo",
                null,
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(checkoutResult.IsSuccess);

        return dbContext.Bookings.Single();
    }

    [Fact]
    public async Task Confirm_ShouldMarkBookingPaid_AndSettleTransaction()
    {
        var (dbContext, family, elderly) = SeedFamily();
        Booking booking = await CheckoutAsync(dbContext, family, elderly);

        booking.RecordPaymentIntent("PM-ORDER-1", PaymentMethod.Card, DateTime.UtcNow);
        dbContext.SaveChanges();

        var handler = new ConfirmBookingPaymentCommandHandler(
            dbContext,
            new FakePaymobClient(new PaymobPaymentIntent("PM-ORDER-1", "pi_1", "dev-secret", "pk_dev")));

        var result = await handler.Handle(
            new ConfirmBookingPaymentCommand("PM-ORDER-1", 999888, 57500, true, false, DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Paid", result.Value.Outcome);

        Booking persisted = dbContext.Bookings.Single();
        Assert.Equal(BookingStatus.PendingCaregiverApproval, persisted.Status);
        Assert.Equal("999888", persisted.PaymobTransactionId);
        Assert.Equal(
            PaymentTransactionStatus.Succeeded,
            persisted.PaymentTransactions.Single().Status);
    }

    [Fact]
    public async Task Confirm_ShouldBeIdempotent_OnReplay()
    {
        var (dbContext, family, elderly) = SeedFamily();
        Booking booking = await CheckoutAsync(dbContext, family, elderly);

        booking.RecordPaymentIntent("PM-ORDER-2", PaymentMethod.Card, DateTime.UtcNow);
        dbContext.SaveChanges();

        var handler = new ConfirmBookingPaymentCommandHandler(
            dbContext,
            new FakePaymobClient(new PaymobPaymentIntent("PM-ORDER-2", "pi_2", "dev-secret", "pk_dev")));

        var first = await handler.Handle(
            new ConfirmBookingPaymentCommand("PM-ORDER-2", 111111, 57500, true, false, DateTime.UtcNow),
            CancellationToken.None);

        var second = await handler.Handle(
            new ConfirmBookingPaymentCommand("PM-ORDER-2", 222222, 57500, true, false, DateTime.UtcNow),
            CancellationToken.None);

        Assert.Equal("Paid", first.Value.Outcome);
        Assert.Equal("AlreadyProcessed", second.Value.Outcome);
        Assert.Single(dbContext.Bookings.Single().PaymentTransactions, t => t.PaymobOrderId == "PM-ORDER-2");
    }

    [Fact]
    public async Task Confirm_ShouldRejectAmountMismatch()
    {
        var (dbContext, family, elderly) = SeedFamily();
        Booking booking = await CheckoutAsync(dbContext, family, elderly);

        booking.RecordPaymentIntent("PM-ORDER-3", PaymentMethod.Card, DateTime.UtcNow);
        dbContext.SaveChanges();

        var handler = new ConfirmBookingPaymentCommandHandler(
            dbContext,
            new FakePaymobClient(new PaymobPaymentIntent("PM-ORDER-3", "pi_3", "dev-secret", "pk_dev")));

        var result = await handler.Handle(
            new ConfirmBookingPaymentCommand("PM-ORDER-3", 999888, 999999, true, false, DateTime.UtcNow),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Paymob.AmountMismatch", result.Error.Code);
        Assert.Equal(BookingStatus.PendingPayment, dbContext.Bookings.Single().Status);
    }

    [Fact]
    public async Task Confirm_ShouldRecordFailure_WithoutChangingBookingStatus()
    {
        var (dbContext, family, elderly) = SeedFamily();
        Booking booking = await CheckoutAsync(dbContext, family, elderly);

        booking.RecordPaymentIntent("PM-ORDER-4", PaymentMethod.Wallet, DateTime.UtcNow);
        dbContext.SaveChanges();

        var handler = new ConfirmBookingPaymentCommandHandler(
            dbContext,
            new FakePaymobClient(new PaymobPaymentIntent("PM-ORDER-4", "pi_4", "dev-secret", "pk_dev")));

        var result = await handler.Handle(
            new ConfirmBookingPaymentCommand("PM-ORDER-4", 444444, 57500, false, false, DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Failed", result.Value.Outcome);

        Booking persisted = dbContext.Bookings.Single();
        Assert.Equal(BookingStatus.PendingPayment, persisted.Status);
        Assert.Equal(
            PaymentTransactionStatus.Failed,
            persisted.PaymentTransactions.Single().Status);
    }

    [Fact]
    public async Task Confirm_ShouldExpireAndRefund_WhenPaidAfterDeadline()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime creation = DateTime.UtcNow;

        // Deadline must be AFTER creation (aggregate guard); lateness comes from
        // the webhook arriving at a UtcNow past the deadline.
        var booking = Booking.Create(
            family.Id,
            family.OwnerUserId,
            elderly.Id,
            CaregiverId.New(),
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(creation).AddDays(2),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "123 Nile St, Cairo",
            null,
            BookingPriceSnapshot.Calculate(500m, 15m),
            creation.AddMinutes(5),
            DateOnly.FromDateTime(creation),
            creation);

        dbContext.Bookings.Add(booking);
        booking.RecordPaymentIntent("PM-ORDER-5", PaymentMethod.Card, creation);
        dbContext.SaveChanges();

        var handler = new ConfirmBookingPaymentCommandHandler(
            dbContext,
            new FakePaymobClient(
                new PaymobPaymentIntent("PM-ORDER-5", "pi_5", "dev-secret", "pk_dev"),
                refundId: "dev-refund-99"));

        var result = await handler.Handle(
            new ConfirmBookingPaymentCommand(
                "PM-ORDER-5", 555555, 57500, true, false, creation.AddMinutes(10)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("PaidExpired", result.Value.Outcome);

        Booking persisted = dbContext.Bookings.Single();
        Assert.Equal(BookingStatus.Refunded, persisted.Status);
        Assert.NotNull(persisted.RefundedOnUtc);
        Assert.Equal("dev-refund-99", persisted.PaymobRefundTransactionId);
        Assert.Equal(
            PaymentTransactionStatus.Refunded,
            persisted.PaymentTransactions.Single().Status);
    }

    [Fact]
    public async Task Decline_ShouldRefund_WhenBookingWasPaid()
    {
        var (dbContext, family, elderly) = SeedFamily();
        Booking booking = await CheckoutAsync(dbContext, family, elderly);

        booking.RecordPaymentIntent("PM-ORDER-6", PaymentMethod.Card, DateTime.UtcNow);
        dbContext.SaveChanges();
        booking.MarkAsPaid("PM-ORDER-6", "666666", DateTime.UtcNow);
        dbContext.SaveChanges();

        var declineHandler = new CaregiverDeclineBookingCommandHandler(
            dbContext,
            new FakePaymobClient(null, refundId: "dev-refund-66"));

        var declineResult = await declineHandler.Handle(
            new CaregiverDeclineBookingCommand(
                booking.CaregiverId,
                booking.Id,
                "Schedule conflict",
                DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(declineResult.IsSuccess);

        Booking persisted = dbContext.Bookings.Single();
        Assert.Equal(BookingStatus.Refunded, persisted.Status);
        Assert.NotNull(persisted.RefundedOnUtc);
        Assert.Equal("dev-refund-66", persisted.PaymobRefundTransactionId);
    }
}