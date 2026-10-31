using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Families.Application.Abstractions.Payments;
using Ahlena.Modules.Families.Application.Bookings;
using Ahlena.Modules.Families.Domain.Bookings;
using Ahlena.Modules.Families.Domain.Elderlies;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Ahlena.UnitTests.Families;

/// <summary>
/// The retry of an incomplete refund must send what is still owed, never a recomputed figure:
/// a failed 50% award is retried at 50%, an award that already settled is refused, a revision-1
/// booking keeps the legacy remaining-ceiling semantics, and a revision-2 fact without a recorded
/// award fails closed.
/// <para>
/// Authored, NOT RUN. The in-memory provider is used only for the handler wiring; it does not prove
/// anything about relational concurrency, which needs the approved disposable PostgreSQL gate.
/// </para>
/// </summary>
public sealed class BookingRefundRetryAmountTests
{
    // ---------------------------------------------------------------------------------------------
    // The rule, asserted directly.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Resolve_RevisionTwoPartialAward_ReturnsTheAwardNotTheCapture()
    {
        long amount = BookingRefundRetryAmount.Resolve(
            factPolicyVersion: 2,
            factAwardedMinorUnits: 20000,
            capturedCeiling: 40000,
            alreadyRefundedMinorUnits: 0,
            out Error? error);

        Assert.Null(error);
        Assert.Equal(20000, amount);
    }

    [Fact]
    public void Resolve_RevisionTwoAwardAlreadySettled_RefusesInsteadOfPayingTwice()
    {
        long amount = BookingRefundRetryAmount.Resolve(
            factPolicyVersion: 2,
            factAwardedMinorUnits: 20000,
            capturedCeiling: 20000,
            alreadyRefundedMinorUnits: 20000,
            out Error? error);

        Assert.NotNull(error);
        Assert.Equal("Bookings.AlreadyRefunded", error!.Code);
        Assert.Equal(0, amount);
    }

    [Fact]
    public void Resolve_RevisionTwoAwardPartiallySettled_SendsOnlyTheRemainder()
    {
        long amount = BookingRefundRetryAmount.Resolve(
            factPolicyVersion: 2,
            factAwardedMinorUnits: 20000,
            capturedCeiling: 30000,
            alreadyRefundedMinorUnits: 5000,
            out Error? error);

        Assert.Null(error);
        Assert.Equal(15000, amount);
    }

    [Fact]
    public void Resolve_RevisionTwoAwardCappedByRemainingCapture_NeverExceedsTheBudget()
    {
        long amount = BookingRefundRetryAmount.Resolve(
            factPolicyVersion: 2,
            factAwardedMinorUnits: 40000,
            capturedCeiling: 30000,
            alreadyRefundedMinorUnits: 10000,
            out Error? error);

        Assert.Null(error);
        Assert.Equal(30000, amount);
    }

    [Fact]
    public void Resolve_RevisionTwoFactWithoutAnAward_FailsClosedInsteadOfFullCapture()
    {
        long amount = BookingRefundRetryAmount.Resolve(
            factPolicyVersion: 2,
            factAwardedMinorUnits: null,
            capturedCeiling: 40000,
            alreadyRefundedMinorUnits: 0,
            out Error? error);

        Assert.NotNull(error);
        Assert.Equal("Bookings.RefundNotEligible", error!.Code);
        Assert.Equal(0, amount);

        // The point of the guard: it must not silently behave like the legacy full-capture retry.
        Assert.NotEqual(40000, amount);
    }

    [Fact]
    public void Resolve_RevisionOneFact_KeepsTheLegacyRemainingCeiling()
    {
        long amount = BookingRefundRetryAmount.Resolve(
            factPolicyVersion: 1,
            factAwardedMinorUnits: null,
            capturedCeiling: 40000,
            alreadyRefundedMinorUnits: 0,
            out Error? error);

        Assert.Null(error);
        Assert.Equal(40000, amount);
    }

    [Fact]
    public void Resolve_NoFact_KeepsTheLegacyRemainingCeiling()
    {
        long amount = BookingRefundRetryAmount.Resolve(
            factPolicyVersion: null,
            factAwardedMinorUnits: null,
            capturedCeiling: 51500,
            alreadyRefundedMinorUnits: 0,
            out Error? error);

        Assert.Null(error);
        Assert.Equal(51500, amount);
    }

    // ---------------------------------------------------------------------------------------------
    // Through the handler, with a provider spy: 400 captured / 200 awarded.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Retry_AfterARevisionTwoPartialRefusal_SendsTheAwardAndNeverTheCapture()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = RevisionTwoPartialBooking(databaseName, family, elderly);

        var paymob = new RecordingPaymobClient
        {
            SucceedAfterCalls = 1
        };

        using FamiliesDbContext context = CreateContext(databaseName);
        var handler = new AdminRefundBookingCommandHandler(context, paymob);

        Result<BookingDetailResponse> first = await handler.Handle(
            new AdminRefundBookingCommand(booking.Id, DateTime.UtcNow),
            CancellationToken.None);

        // A definitive refusal is reported, and leaves the claim released for a bounded retry.
        Assert.False(first.IsSuccess);

        Result<BookingDetailResponse> retry = await handler.Handle(
            new AdminRefundBookingCommand(booking.Id, DateTime.UtcNow.AddMinutes(1)),
            CancellationToken.None);

        Assert.True(retry.IsSuccess);
        Assert.Equal(2, paymob.Calls.Count);

        // Both attempts send the 200.00 award; neither ever sends the 400.00 capture.
        Assert.Equal(200.00m, paymob.Calls[0].Amount);
        Assert.Equal(200.00m, paymob.Calls[1].Amount);
        Assert.DoesNotContain(paymob.Calls, call => call.Amount == 400.00m);

        using FamiliesDbContext stored = CreateContext(databaseName);
        Booking persisted = stored.Bookings.AsNoTracking().Single(b => b.Id == booking.Id);

        // Exactly one settlement: the ceiling advanced by the award, once.
        Assert.Equal(20000, persisted.RefundedMinorUnits);
        Assert.Equal(20000, persisted.RefundableCeilingMinorUnits());
        Assert.False(persisted.IsRefundAttemptInFlight);
    }

    [Fact]
    public async Task Retry_AfterTheAwardAlreadySettled_DoesNotCallTheGatewayAgain()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = RevisionTwoPartialBooking(databaseName, family, elderly);

        using (FamiliesDbContext seed = CreateContext(databaseName))
        {
            Booking tracked = seed.Bookings.Single(b => b.Id == booking.Id);
            tracked.RecordRefundedAmount(20000, DateTime.UtcNow);
            seed.SaveChanges();
        }

        var paymob = new RecordingPaymobClient();
        using FamiliesDbContext context = CreateContext(databaseName);
        var handler = new AdminRefundBookingCommandHandler(context, paymob);

        Result<BookingDetailResponse> result = await handler.Handle(
            new AdminRefundBookingCommand(booking.Id, DateTime.UtcNow),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Bookings.AlreadyRefunded", result.Error.Code);
        Assert.Empty(paymob.Calls);
    }

    [Fact]
    public async Task Retry_ForARevisionOneBooking_StillSendsTheFullRemainingCapture()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = RevisionOneCancelledBooking(databaseName, family, elderly);

        var paymob = new RecordingPaymobClient();

        using FamiliesDbContext context = CreateContext(databaseName);
        var handler = new AdminRefundBookingCommandHandler(context, paymob);

        Result<BookingDetailResponse> result = await handler.Handle(
            new AdminRefundBookingCommand(booking.Id, DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        RefundCall call = Assert.Single(paymob.Calls);
        Assert.Equal(400.00m, call.Amount);
    }

    // ---------------------------------------------------------------------------------------------
    // Fixtures.
    // ---------------------------------------------------------------------------------------------

    private static string NewDatabaseName() => $"ahlena-refund-retry-{Guid.NewGuid():N}";

    private static FamiliesDbContext CreateContext(string databaseName) =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);

    private static (Family Family, Elderly Elderly) SeedFamily(string databaseName)
    {
        using FamiliesDbContext context = CreateContext(databaseName);

        var family = Family.Create(UserId.New(), "Refund Retry Family");
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

        context.Families.Add(family);
        context.Elderlies.Add(elderly);
        context.SaveChanges();

        return (family, elderly);
    }

    /// <summary>
    /// A paid 400.00 booking, cancelled as a revision-2 booking two hours before its start, so the
    /// policy awards the clause-2 50% share (200.00) and the fact records it.
    /// </summary>
    private static Booking RevisionTwoPartialBooking(
        string databaseName,
        Family family,
        Elderly elderly)
    {
        DateTime now = DateTime.UtcNow;
        Booking booking = CreatePaidConfirmedBooking(family, elderly, now, refundPolicyVersion: 2);

        DateTime scheduledStart = booking.BookingDate.ToDateTime(booking.StartTime, DateTimeKind.Utc);
        DateTime actedOnUtc = scheduledStart - TimeSpan.FromHours(2);

        BookingCancellationFeedback feedback = BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.Emergency,
            "family emergency");

        BookingRefundDecision decision = BookingRefundPolicy.Decide(
            BookingRefundPolicyInput.ForFamilyCancellation(booking, actedOnUtc, feedback));

        booking.CancelByFamily("family emergency", actedOnUtc);

        BookingCancellationFact fact = BookingCancellationFact.Create(
            booking.Id,
            family.OwnerUserId,
            decision,
            feedback);

        using FamiliesDbContext context = CreateContext(databaseName);
        context.Bookings.Add(booking);
        context.BookingCancellationFacts.Add(fact);
        context.SaveChanges();

        return booking;
    }

    /// <summary>A paid 400.00 booking cancelled under revision 1, with an amount-free fact.</summary>
    private static Booking RevisionOneCancelledBooking(
        string databaseName,
        Family family,
        Elderly elderly)
    {
        DateTime now = DateTime.UtcNow;
        Booking booking = CreatePaidConfirmedBooking(family, elderly, now, refundPolicyVersion: 1);

        DateTime confirmedOnUtc = booking.ConfirmedOnUtc!.Value;
        DateTime actedOnUtc = confirmedOnUtc.AddMinutes(10);

        BookingCancellationDecision decision = BookingCancellationPolicy.Decide(
            BookingCancellationPolicyInput.ForCaregiverCancellation(
                BookingStatus.Confirmed,
                confirmedOnUtc,
                startedOnUtc: null,
                actedOnUtc,
                BookingCaptureEvidence.Captured,
                BookingCancellationFeedback.Create(
                    BookingCancellationReasonCategory.Emergency,
                    "caregiver emergency")));

        booking.CancelByCaregiver("caregiver emergency", actedOnUtc);

        BookingCancellationFact fact = BookingCancellationFact.Create(
            booking.Id,
            UserId.New(),
            decision);

        using FamiliesDbContext context = CreateContext(databaseName);
        context.Bookings.Add(booking);
        context.BookingCancellationFacts.Add(fact);
        context.SaveChanges();

        return booking;
    }

    private static Booking CreatePaidConfirmedBooking(
        Family family,
        Elderly elderly,
        DateTime now,
        int refundPolicyVersion)
    {
        Booking booking = Booking.Create(
            family.Id,
            family.OwnerUserId,
            elderly.Id,
            CaregiverId.New(),
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(now).AddDays(2),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "123 Nile St, Cairo",
            null,
            BookingPriceSnapshot.Calculate(400m, 0m),
            acceptanceDeadlineUtc: now.AddHours(24),
            currentDate: DateOnly.FromDateTime(now),
            createdOnUtc: now,
            refundPolicyVersion: refundPolicyVersion == 2
                ? BookingRefundPolicyVersion.ForNewBooking(scheduledStartPolicyActivated: true)
                : BookingRefundPolicyVersion.ForNewBooking(scheduledStartPolicyActivated: false));

        booking.RecordPaymentIntent("order", PaymentMethod.Card, now);
        booking.MarkAsPaid("order", "txn-1", now.AddMinutes(1));
        booking.AcceptByCaregiver(now.AddMinutes(2));
        return booking;
    }

    private sealed record RefundCall(string PaymobTransactionId, decimal Amount);

    /// <summary>
    /// Provider spy. <see cref="SucceedAfterCalls"/> lets a test model "the first attempt was
    /// definitively refused, the second one settled" without inventing a second code path.
    /// </summary>
    private sealed class RecordingPaymobClient : IPaymobClient
    {
        private int _calls;

        public List<RefundCall> Calls { get; } = [];

        /// <summary>Number of leading calls that are refused; 0 means every call succeeds.</summary>
        public int SucceedAfterCalls { get; init; }

        public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(
            PaymobPaymentIntentInput input,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<PaymobPaymentIntent>.Success(
                new PaymobPaymentIntent(input.BookingId.Value.ToString(), "intention", "secret", "key")));

        public Task<Result<string?>> RefundPaymentAsync(
            string paymobTransactionId,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new RefundCall(paymobTransactionId, amount));
            _calls++;

            if (_calls <= SucceedAfterCalls)
            {
                return Task.FromResult(Result<string?>.Failure(
                    new Error("Paymob.RefundFailed", "The gateway refused the refund.")));
            }

            return Task.FromResult(Result<string?>.Success($"refund-{_calls}"));
        }

        public Task<Result<PaymobRefundVerification>> VerifyRefundTransactionAsync(
            string refundTransactionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<PaymobRefundVerification>.Failure(new Error(
                "Paymob.VerificationUnavailable",
                "Refund verification is not used by this test.")));
    }
}
