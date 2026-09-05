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

public sealed class BookingPaymentIntentTests
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
        var family = Family.Create(UserId.New(), "Intent Family");

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

    private sealed class FakePaymobClient(PaymobPaymentIntent? intent, Error? error) : IPaymobClient
    {
        public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(
            PaymobPaymentIntentInput input,
            CancellationToken cancellationToken = default)
        {
            if (error is not null)
            {
                return Task.FromResult(Result<PaymobPaymentIntent>.Failure(error));
            }

            return Task.FromResult(Result<PaymobPaymentIntent>.Success(intent!));
        }

        public Task<Result<string?>> RefundPaymentAsync(
            string paymobTransactionId,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<string?>.Success($"dev-refund-{Guid.NewGuid():N}"));
        }
    }

    private static async Task<(BookingCheckoutResponse Response, Booking Booking)> CheckoutAsync(
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

        Booking booking = dbContext.Bookings.Single();

        return (checkoutResult.Value, booking);
    }

    private static CreateBookingPaymentIntentCommand IntentCommand(
        Family family,
        Booking booking,
        PaymentMethod method,
        string? walletNumber = null)
    {
        return new CreateBookingPaymentIntentCommand(
            booking.Id,
            family.OwnerUserId,
            method,
            walletNumber,
            ValidBilling(),
            DateTime.UtcNow);
    }
    [Fact]
    public async Task Intent_ShouldRecordTransaction_AndReturnGatewayPayload()
    {
        var (dbContext, family, elderly) = SeedFamily();
        var (_, booking) = await CheckoutAsync(dbContext, family, elderly);

        var expectedIntent = new PaymobPaymentIntent(
            "PM-ORDER-9",
            "https://accept.paymob.com/api/acceptance/iframes/123?payment_token=abc",
            null);

        var handler = new CreateBookingPaymentIntentCommandHandler(
            dbContext,
            new FakePaymobClient(expectedIntent, null));

        var result = await handler.Handle(
            IntentCommand(family, booking, PaymentMethod.Card),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("PM-ORDER-9", result.Value.PaymobOrderId);
        Assert.Equal(expectedIntent.IframeUrl, result.Value.IframeUrl);
        Assert.Equal(575m, result.Value.Amount);
        Assert.Equal("EGP", result.Value.Currency);

        var transaction = Assert.Single(booking.PaymentTransactions);
        Assert.Equal(PaymentTransactionStatus.Pending, transaction.Status);
        Assert.Equal(PaymentMethod.Card, transaction.Method);
        Assert.Equal("PM-ORDER-9", transaction.PaymobOrderId);
        Assert.Equal(575m, transaction.Amount);
    }

    [Fact]
    public async Task Intent_ShouldReject_WhenBookingAlreadyPaid()
    {
        var (dbContext, family, elderly) = SeedFamily();
        var (_, booking) = await CheckoutAsync(dbContext, family, elderly);

        booking.MarkAsPaid("PM-ORDER-1", "PM-TXN-1", DateTime.UtcNow);

        var handler = new CreateBookingPaymentIntentCommandHandler(
            dbContext,
            new FakePaymobClient(
                new PaymobPaymentIntent("PM-ORDER-2", null, null),
                null));

        var result = await handler.Handle(
            IntentCommand(family, booking, PaymentMethod.Card),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Bookings.Domain.InvalidOperation", result.Error.Code);
    }

    [Fact]
    public async Task Intent_ShouldRejectForeignFamilyBooking()
    {
        var (dbContext, family, elderly) = SeedFamily();

        var attackerFamily = Family.Create(UserId.New(), "Other Family");
        dbContext.Families.Add(attackerFamily);
        dbContext.SaveChanges();

        var (response, booking) = await CheckoutAsync(dbContext, family, elderly);

        var command = new CreateBookingPaymentIntentCommand(
            new BookingId(response.BookingId),
            attackerFamily.OwnerUserId,
            PaymentMethod.Card,
            null,
            new PaymobBillingData("Ali", "Hassan", "ali@example.com", "+201098765432"),
            DateTime.UtcNow);

        var handler = new CreateBookingPaymentIntentCommandHandler(
            dbContext,
            new FakePaymobClient(
                new PaymobPaymentIntent("PM-ORDER-3", null, null),
                null));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Bookings.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Intent_ShouldPropagateGatewayFailure_AndNotRecordTransaction()
    {
        var (dbContext, family, elderly) = SeedFamily();
        var (_, booking) = await CheckoutAsync(dbContext, family, elderly);

        var handler = new CreateBookingPaymentIntentCommandHandler(
            dbContext,
            new FakePaymobClient(
                null,
                new Error("Paymob.MethodNotAvailable", "The selected payment method is not available.")));

        var result = await handler.Handle(
            IntentCommand(family, booking, PaymentMethod.Wallet, "201012345678"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Paymob.MethodNotAvailable", result.Error.Code);
        Assert.Empty(dbContext.Bookings.Single().PaymentTransactions);
    }

    [Fact]
    public async Task Validator_ShouldRequireWalletNumber_ForWalletMethod()
    {
        var validator = new CreateBookingPaymentIntentCommandValidator();

        var walletWithoutNumber = new CreateBookingPaymentIntentCommand(
            BookingId.New(),
            UserId.New(),
            PaymentMethod.Wallet,
            null,
            ValidBilling(),
            DateTime.UtcNow);

        Assert.False((await validator.ValidateAsync(walletWithoutNumber)).IsValid);

        var walletWithNumber = new CreateBookingPaymentIntentCommand(
            BookingId.New(),
            UserId.New(),
            PaymentMethod.Wallet,
            "201012345678",
            ValidBilling(),
            DateTime.UtcNow);

        Assert.True((await validator.ValidateAsync(walletWithNumber)).IsValid);

        var cardWithNumber = new CreateBookingPaymentIntentCommand(
            BookingId.New(),
            UserId.New(),
            PaymentMethod.Card,
            "201012345678",
            ValidBilling(),
            DateTime.UtcNow);

        Assert.False((await validator.ValidateAsync(cardWithNumber)).IsValid);
    }

    private static PaymobBillingData ValidBilling()
    {
        return new PaymobBillingData(
            "Ahmed",
            "Ali",
            "ahmed@example.com",
            "+201012345678");
    }
}