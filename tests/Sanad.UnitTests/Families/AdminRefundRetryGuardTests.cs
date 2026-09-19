using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Sanad.UnitTests.Families;

public sealed class AdminRefundRetryGuardTests
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
        var family = Family.Create(UserId.New(), "Refund Guard Family");

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

    private static Booking CreateConfirmedBooking(Family family, Elderly elderly, CaregiverId caregiverId, DateTime now)
    {
        var booking = Booking.Create(
            family.Id,
            family.OwnerUserId,
            elderly.Id,
            caregiverId,
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(now).AddDays(2),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "123 Nile St, Cairo",
            null,
            BookingPriceSnapshot.Calculate(500m, 15m),
            now.AddHours(24),
            DateOnly.FromDateTime(now),
            now);

        booking.MarkAsPaid(booking.Id.Value.ToString(), $"txn-{Guid.NewGuid():N}", now);
        booking.AcceptByCaregiver(now.AddMinutes(1));
        return booking;
    }

    private static BookingCancellationFact CreateNoRefundDueFact(BookingId bookingId, UserId actorUserId)
    {
        DateTime acceptance = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);
        DateTime cancelled = acceptance.AddMinutes(70); // outside grace
        var feedback = BookingCancellationFeedback.Create(BookingCancellationReasonCategory.Other, "family outside grace");
        var decision = BookingCancellationPolicy.Decide(
            BookingCancellationPolicyInput.ForFamilyCancellation(
                BookingStatus.Confirmed,
                acceptance,
                null,
                cancelled,
                BookingCaptureEvidence.Captured,
                feedback));
        return BookingCancellationFact.Create(bookingId, actorUserId, decision);
    }

    private static BookingCancellationFact CreateFullCapturedRefundFact(BookingId bookingId, UserId actorUserId)
    {
        DateTime acceptance = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);
        DateTime cancelled = acceptance.AddMinutes(10);
        var feedback = BookingCancellationFeedback.Create(BookingCancellationReasonCategory.Emergency, "caregiver emergency");
        var decision = BookingCancellationPolicy.Decide(
            BookingCancellationPolicyInput.ForCaregiverCancellation(
                BookingStatus.Confirmed,
                acceptance,
                null,
                cancelled,
                BookingCaptureEvidence.Captured,
                feedback));
        return BookingCancellationFact.Create(bookingId, actorUserId, decision);
    }

    private static void ClearPaymobTransactionIds(Booking booking)
    {
        // Use reflection to null out the stored transaction ids to hit the "No Paymob transaction id" branch.
        var paymobProp = typeof(Booking).GetProperty("PaymobTransactionId", BindingFlags.Instance | BindingFlags.Public);
        paymobProp?.SetValue(booking, null);

        var orderProp = typeof(Booking).GetProperty("PaymobOrderId", BindingFlags.Instance | BindingFlags.Public);
        orderProp?.SetValue(booking, null);

        // Also clear any succeeded payment transactions' ids if present (not needed for this path, but ensure empty)
        var field = typeof(Booking).GetField("_paymentTransactions", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(booking) is System.Collections.IList list)
        {
            list.Clear();
        }
    }

    [Fact]
    public async Task PaidCancelledByCaregiver_WithNoRefundDueFact_ShouldReturnNoRefundDue_AndNotCallGateway()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        var actorUserId = UserId.New();

        var booking = CreateConfirmedBooking(family, elderly, caregiverId, now);
        booking.CancelByCaregiver("family outside grace retained", now.AddMinutes(2));
        var fact = CreateNoRefundDueFact(booking.Id, actorUserId);

        dbContext.Bookings.Add(booking);
        dbContext.BookingCancellationFacts.Add(fact);
        dbContext.SaveChanges();

        var paymob = new RecordingPaymobClient();
        var handler = new AdminRefundBookingCommandHandler(dbContext, paymob);

        var result = await handler.Handle(
            new AdminRefundBookingCommand(booking.Id, now.AddMinutes(10)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Bookings.NoRefundDue", result.Error.Code);
        Assert.Equal("No refund is due for this cancellation; the policy retained the amount.", result.Error.Message);
        Assert.Empty(paymob.Calls);

        // Booking untouched: still not refunded
        var reloaded = await dbContext.Bookings.SingleAsync(b => b.Id == booking.Id);
        Assert.Null(reloaded.RefundedOnUtc);
        Assert.NotEqual(BookingStatus.Refunded, reloaded.Status);
        Assert.Equal(BookingStatus.CancelledByCaregiver, reloaded.Status);
    }

    [Fact]
    public async Task PaidCancelled_WithFullCapturedRefundFact_NotRefunded_GatewaySuccess_ShouldRefundOnce()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        var actorUserId = UserId.New();

        var booking = CreateConfirmedBooking(family, elderly, caregiverId, now);
        DateTime cancelOn = now.AddMinutes(2);
        booking.CancelByCaregiver("caregiver emergency", cancelOn);
        var fact = CreateFullCapturedRefundFact(booking.Id, actorUserId);

        // Capture expected values before handler runs
        string expectedTransactionId = booking.PaymobTransactionId!;
        decimal expectedAmount = booking.PriceSnapshot.TotalPayableAmount;

        dbContext.Bookings.Add(booking);
        dbContext.BookingCancellationFacts.Add(fact);
        dbContext.SaveChanges();

        var paymob = new RecordingPaymobClient { ShouldSucceed = true, RefundId = "refund-txn-success" };
        var handler = new AdminRefundBookingCommandHandler(dbContext, paymob);
        DateTime refundTime = now.AddMinutes(10);

        var result = await handler.Handle(
            new AdminRefundBookingCommand(booking.Id, refundTime),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStatus.Refunded, result.Value.Status);
        Assert.Equal(BookingRefundState.Succeeded, result.Value.RefundState);

        Assert.Single(paymob.Calls);
        Assert.Equal(expectedTransactionId, paymob.Calls[0].PaymobTransactionId);
        Assert.Equal(expectedAmount, paymob.Calls[0].Amount);

        var reloaded = await dbContext.Bookings.AsNoTracking().SingleAsync(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.Refunded, reloaded.Status);
        Assert.NotNull(reloaded.RefundedOnUtc);
        Assert.Equal(refundTime, reloaded.RefundedOnUtc);
        Assert.Equal("refund-txn-success", reloaded.PaymobRefundTransactionId);
    }

    [Fact]
    public async Task AlreadyRefunded_ShouldReturnAlreadyRefunded_AndNotCallGateway()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();

        var booking = CreateConfirmedBooking(family, elderly, caregiverId, now);
        booking.CancelByCaregiver("to be refunded", now.AddMinutes(2));
        booking.MarkRefunded("already-refund-id", now.AddMinutes(5));

        dbContext.Bookings.Add(booking);
        dbContext.SaveChanges();

        var paymob = new RecordingPaymobClient();
        var handler = new AdminRefundBookingCommandHandler(dbContext, paymob);

        var result = await handler.Handle(
            new AdminRefundBookingCommand(booking.Id, now.AddMinutes(10)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Bookings.AlreadyRefunded", result.Error.Code);
        Assert.Empty(paymob.Calls);

        var reloaded = await dbContext.Bookings.SingleAsync(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.Refunded, reloaded.Status);
    }

    [Fact]
    public async Task NoFact_PaidCancelled_LegacyPath_ShouldProceedOnGatewaySuccess()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();

        var booking = CreateConfirmedBooking(family, elderly, caregiverId, now);
        booking.CancelByCaregiver("legacy no fact", now.AddMinutes(2));

        dbContext.Bookings.Add(booking);
        dbContext.SaveChanges();

        var paymob = new RecordingPaymobClient { ShouldSucceed = true, RefundId = "legacy-refund" };
        var handler = new AdminRefundBookingCommandHandler(dbContext, paymob);

        var result = await handler.Handle(
            new AdminRefundBookingCommand(booking.Id, now.AddMinutes(10)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(paymob.Calls);
        Assert.Equal(BookingStatus.Refunded, result.Value.Status);
        Assert.Equal(BookingRefundState.Succeeded, result.Value.RefundState);
    }

    [Fact]
    public async Task NoPaymobTransactionId_ShouldReturnRefundNotEligible()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();

        var booking = CreateConfirmedBooking(family, elderly, caregiverId, now);
        booking.CancelByCaregiver("no txn id", now.AddMinutes(2));

        // Clear transaction ids to hit the missing-id branch
        ClearPaymobTransactionIds(booking);

        dbContext.Bookings.Add(booking);
        dbContext.SaveChanges();

        // Ensure PaidOnUtc still set so Resolve == Failed (paid cancelled)
        var reloadedBefore = await dbContext.Bookings.AsNoTracking().SingleAsync(b => b.Id == booking.Id);
        Assert.NotNull(reloadedBefore.PaidOnUtc);
        Assert.Equal(BookingStatus.CancelledByCaregiver, reloadedBefore.Status);

        var paymob = new RecordingPaymobClient();
        var handler = new AdminRefundBookingCommandHandler(dbContext, paymob);

        var result = await handler.Handle(
            new AdminRefundBookingCommand(booking.Id, now.AddMinutes(10)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Bookings.RefundNotEligible", result.Error.Code);
        Assert.Contains("No Paymob transaction id", result.Error.Message);
        Assert.Empty(paymob.Calls);
    }

    [Fact]
    public async Task UnknownBookingId_ShouldReturnNotFound()
    {
        var (dbContext, _, _) = SeedFamily();
        var paymob = new RecordingPaymobClient();
        var handler = new AdminRefundBookingCommandHandler(dbContext, paymob);

        var result = await handler.Handle(
            new AdminRefundBookingCommand(BookingId.New(), DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Bookings.NotFound", result.Error.Code);
        Assert.Empty(paymob.Calls);
    }

    private sealed class RecordingPaymobClient : IPaymobClient
    {
        public List<(string PaymobTransactionId, decimal Amount)> Calls { get; } = [];
        public bool ShouldSucceed { get; set; } = true;
        public string RefundId { get; set; } = "test-refund";

        public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(PaymobPaymentIntentInput input, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<PaymobPaymentIntent>.Success(new PaymobPaymentIntent(input.BookingId.Value.ToString(), "intention", "secret", "key")));

        public Task<Result<string?>> RefundPaymentAsync(string paymobTransactionId, decimal amount, CancellationToken cancellationToken = default)
        {
            Calls.Add((paymobTransactionId, amount));
            if (ShouldSucceed)
                return Task.FromResult(Result<string?>.Success(RefundId));
            return Task.FromResult(Result<string?>.Failure(new Error("Paymob.GatewayError", "gateway refused")));
        }
    }
}
