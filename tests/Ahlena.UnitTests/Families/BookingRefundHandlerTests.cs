using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Application.Abstractions.Payments;
using Ahlena.Modules.Families.Application.Bookings;
using Ahlena.Modules.Families.Domain.Bookings;
using Ahlena.Modules.Families.Domain.Elderlies;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Infrastructure.Data;
using Ahlena.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Ahlena.UnitTests.Families;

/// <summary>
/// Handler-level contract tests for the revision-2 care-booking refund slice: the family cancel
/// path dispatching on the booking's sold policy revision, and the caregiver provider-outcome path
/// recording a full-refund disposition for a provider cancellation or no-show.
/// <para>
/// Fixtures are built through public domain entry points and persisted through the real
/// infrastructure recorder, so the booking mutation and its cancellation fact commit together.
/// Timestamps are fixed UTC constants.
/// </para>
/// <para>
/// NOT RUN. Authored in the W3-F1 slice; the single build/test gate belongs to Mastermind.
/// </para>
/// </summary>
public sealed class BookingRefundHandlerTests
{
    private static readonly DateTime CreatedOnUtc = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AcceptedAtUtc = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ScheduledStartUtc = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------------------------------------
    // Family cancel: revision dispatch.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task FamilyCancel_LegacyBooking_UsesTheAcceptanceRelativePolicy()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedBooking(databaseName, family, elderly, revision: 1, accept: true);
        var paymob = new RecordingPaymobClient();

        // 25 minutes after acceptance: inside the legacy 60-minute window → full refund.
        Result result = await CancelAsync(
            databaseName, paymob, booking.Id, family.OwnerUserId, AcceptedAtUtc.AddMinutes(25));

        Assert.True(result.IsSuccess);
        Assert.Single(paymob.RefundCalls);
    }

    [Fact]
    public async Task FamilyCancel_RevisionTwoBooking_UsesTheScheduledStartPolicy()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedBooking(databaseName, family, elderly, revision: 2, accept: true);
        var paymob = new RecordingPaymobClient();

        // Exactly 24 hours before start → full refund under revision 2.
        Result result = await CancelAsync(
            databaseName,
            paymob,
            booking.Id,
            family.OwnerUserId,
            ScheduledStartUtc - TimeSpan.FromHours(24));

        Assert.True(result.IsSuccess);
        Assert.Single(paymob.RefundCalls);
    }

    [Fact]
    public async Task FamilyCancel_RevisionTwoInsideWindow_SettlesTheAwardWithTheProvider()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedBooking(databaseName, family, elderly, revision: 2, accept: true);
        var paymob = new RecordingPaymobClient();

        // Two hours before start → 50% award of the 400.00 captured amount.
        Result result = await CancelAsync(
            databaseName, paymob, booking.Id, family.OwnerUserId, ScheduledStartUtc.AddHours(-2));

        Assert.True(result.IsSuccess);

        // BLK-1: the gateway is asked for the award (200.00), never the captured amount (400.00).
        RefundCall call = Assert.Single(paymob.RefundCalls);
        Assert.Equal(200.00m, call.Amount);
    }

    [Fact]
    public async Task FamilyCancel_RecordsTheAwardAndPartialEntitlementOnTheFact()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedBooking(databaseName, family, elderly, revision: 2, accept: true);
        var paymob = new RecordingPaymobClient();

        Result result = await CancelAsync(
            databaseName, paymob, booking.Id, family.OwnerUserId, ScheduledStartUtc.AddHours(-2));

        Assert.True(result.IsSuccess);

        using FamiliesDbContext context = CreateContext(databaseName);
        BookingCancellationFact fact = context.BookingCancellationFacts
            .AsNoTracking()
            .Single(f => f.BookingId == booking.Id);

        // MAJ-6: the persisted fact carries the exact award and the partial basis, so the wire
        // stops labelling a 50% award as a full refund and ops can reconstruct the amount.
        Assert.Equal(BookingRefundEntitlement.PartialRefund, fact.RefundEntitlement);
        Assert.Equal(20000, fact.AwardedMinorUnits);
        Assert.Equal(40000, fact.PartialEntitlementMinorUnits);
    }

    [Fact]
    public async Task FamilyCancel_RevisionTwoAtStart_DoesNotTouchTheProvider()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedBooking(databaseName, family, elderly, revision: 2, accept: true);
        var paymob = new RecordingPaymobClient();

        Result result = await CancelAsync(
            databaseName, paymob, booking.Id, family.OwnerUserId, ScheduledStartUtc);

        Assert.True(result.IsSuccess);
        Assert.Empty(paymob.RefundCalls);
    }

    // ---------------------------------------------------------------------------------------------
    // Caregiver provider outcome.
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task FamilyCancel_AGatewayRefusalStillCancelsButDoesNotAdvanceTheCeiling()
    {
        // The family cancel endpoint keeps its documented legacy tolerance: a gateway REFUSAL does
        // not fail the cancellation. What changed (BLK-1/MAJ-4) is that the refusal moves no money,
        // so the ceiling stays open and the outstanding award can be retried by ops.
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedBooking(databaseName, family, elderly, revision: 2, accept: true);
        var paymob = new RecordingPaymobClient { RefundSucceeds = false };

        Result result = await CancelAsync(
            databaseName, paymob, booking.Id, family.OwnerUserId, ScheduledStartUtc.AddHours(-2));

        Assert.True(result.IsSuccess);

        using FamiliesDbContext context = CreateContext(databaseName);
        Booking stored = context.Bookings.AsNoTracking().Single(b => b.Id == booking.Id);

        Assert.Equal(BookingStatus.CancelledByFamily, stored.Status);
        Assert.False(stored.IsRefundOutcomeAmbiguous);
        Assert.Equal(0, stored.RefundedMinorUnits);
        Assert.Equal(40000, stored.RefundableCeilingMinorUnits());
    }

    [Fact]
    public async Task ProviderNoShow_RecordsAFullRefundDispositionAndSettlesInFull()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedBooking(databaseName, family, elderly, revision: 2, accept: true);
        var paymob = new RecordingPaymobClient();

        using FamiliesDbContext context = CreateContext(databaseName);
        var handler = new CaregiverRecordProviderOutcomeCommandHandler(
            context, paymob, new BookingCancellationFactRecorder(context));

        Result result = await handler.Handle(
            new CaregiverRecordProviderOutcomeCommand(
                booking.CaregiverId,
                UserId.New(),
                booking.Id,
                (int)BookingRefundOutcomeReason.ProviderNoShow,
                "provider did not arrive",
                (int)BookingCancellationReasonCategory.Other,
                DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        RefundCall call = Assert.Single(paymob.RefundCalls);
        Assert.Equal(400.00m, call.Amount);
    }

    [Fact]
    public async Task ProviderOutcome_WithAnUnknownReason_IsRejectedBeforeAnyStateIsRead()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedBooking(databaseName, family, elderly, revision: 2, accept: true);
        var paymob = new RecordingPaymobClient();

        using FamiliesDbContext context = CreateContext(databaseName);
        var handler = new CaregiverRecordProviderOutcomeCommandHandler(
            context, paymob, new BookingCancellationFactRecorder(context));

        Result result = await handler.Handle(
            new CaregiverRecordProviderOutcomeCommand(
                booking.CaregiverId,
                UserId.New(),
                booking.Id,
                999,
                "unknown",
                (int)BookingCancellationReasonCategory.Other,
                DateTime.UtcNow),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Bookings.Outcome.InvalidReason", result.Error.Code);
        Assert.Empty(paymob.RefundCalls);
    }

    [Fact]
    public async Task ProviderOutcome_ForAnOutcomeACaregiverMayNotAttest_IsRejected()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedBooking(databaseName, family, elderly, revision: 2, accept: true);
        var paymob = new RecordingPaymobClient();

        using FamiliesDbContext context = CreateContext(databaseName);
        var handler = new CaregiverRecordProviderOutcomeCommandHandler(
            context, paymob, new BookingCancellationFactRecorder(context));

        Result result = await handler.Handle(
            new CaregiverRecordProviderOutcomeCommand(
                booking.CaregiverId,
                UserId.New(),
                booking.Id,
                (int)BookingRefundOutcomeReason.ForceMajeure,
                "storm",
                (int)BookingCancellationReasonCategory.Other,
                DateTime.UtcNow),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Bookings.Outcome.ReasonNotAllowed", result.Error.Code);
    }

    // ---------------------------------------------------------------------------------------------
    // Decision #17: bounded durable ambiguity marker and operator reconciliation.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnUnconfirmedRefundOutcome_MarksTheBookingAndRefusesFurtherAttempts()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedBooking(databaseName, family, elderly, revision: 2, accept: true);
        var paymob = new RecordingPaymobClient { RefundSucceeds = false, RefundOutcomeUnknown = true };

        Result first = await CancelAsync(
            databaseName, paymob, booking.Id, family.OwnerUserId, ScheduledStartUtc.AddHours(-2));

        // The cancellation is recorded, but the movement is explicitly not claimed as completed.
        Assert.False(first.IsSuccess);
        Assert.Equal("Paymob.RefundOutcomeUnknown", first.Error.Code);

        using FamiliesDbContext context = CreateContext(databaseName);
        Booking stored = context.Bookings.AsNoTracking().Single(b => b.Id == booking.Id);
        Assert.True(stored.IsRefundOutcomeAmbiguous);

        // With the marker standing, no retry path may move money — not even a fresh re-issue.
        // The re-issue is refused before any further movement (either at the state guard or at the
        // decision #17 marker), and the gateway is not contacted a second time.
        Result replay = await CancelAsync(
            databaseName, paymob, booking.Id, family.OwnerUserId, ScheduledStartUtc.AddHours(-2));

        Assert.False(replay.IsSuccess);

        using FamiliesDbContext afterReplay = CreateContext(databaseName);
        Booking stillMarked = afterReplay.Bookings.AsNoTracking().Single(b => b.Id == booking.Id);
        Assert.True(stillMarked.IsRefundOutcomeAmbiguous);
        Assert.Equal(0, stillMarked.RefundedMinorUnits);
        Assert.Single(paymob.RefundCalls);
    }

    [Fact]
    public async Task Reconcile_WithAnUnconfirmedAttempt_OnEvidenceThatMoved_BooksTheAwardAndClearsTheMarker()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedBooking(databaseName, family, elderly, revision: 2, accept: true);
        var failing = new RecordingPaymobClient { RefundSucceeds = false, RefundOutcomeUnknown = true };

        await CancelAsync(
            databaseName, failing, booking.Id, family.OwnerUserId, ScheduledStartUtc.AddHours(-2));

        using FamiliesDbContext context = CreateContext(databaseName);
        Guid? attemptClaimId = context.Bookings.AsNoTracking()
            .Where(b => b.Id == booking.Id)
            .Select(b => b.RefundAttemptClaimId)
            .Single();
        var handler = new ReconcileBookingRefundAmbiguityCommandHandler(context);

        Result reconciled = await handler.Handle(
            new ReconcileBookingRefundAmbiguityCommand(
                UserId.New(),
                booking.Id,
                "gateway ledger shows refund 8812 completed",
                RefundDidMove: true,
                ScheduledStartUtc.AddHours(-1),
                ExpectedAttemptClaimId: attemptClaimId,
                VerifiedProviderReference: "8812"),
            CancellationToken.None);

        Assert.True(reconciled.IsSuccess);

        Booking stored = context.Bookings.AsNoTracking().Single(b => b.Id == booking.Id);
        Assert.False(stored.IsRefundOutcomeAmbiguous);
        Assert.NotNull(stored.RefundAmbiguityClearedOnUtc);
        Assert.NotNull(stored.RefundAmbiguityClearedBy);

        // The reconciled movement advances the ceiling, so the money cannot be refunded again.
        Assert.Equal(20000, stored.RefundedMinorUnits);
    }

    [Fact]
    public async Task Reconcile_WithAnUnconfirmedAttempt_OnEvidenceThatDidNotMove_LeavesTheCeilingOpen()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedBooking(databaseName, family, elderly, revision: 2, accept: true);
        var failing = new RecordingPaymobClient { RefundSucceeds = false, RefundOutcomeUnknown = true };

        await CancelAsync(
            databaseName, failing, booking.Id, family.OwnerUserId, ScheduledStartUtc.AddHours(-2));

        using FamiliesDbContext context = CreateContext(databaseName);
        Guid? attemptClaimId = context.Bookings.AsNoTracking()
            .Where(b => b.Id == booking.Id)
            .Select(b => b.RefundAttemptClaimId)
            .Single();
        var handler = new ReconcileBookingRefundAmbiguityCommandHandler(context);

        Result reconciled = await handler.Handle(
            new ReconcileBookingRefundAmbiguityCommand(
                UserId.New(),
                booking.Id,
                "gateway ledger shows no refund for this transaction",
                RefundDidMove: false,
                ScheduledStartUtc.AddHours(-1),
                ExpectedAttemptClaimId: attemptClaimId),
            CancellationToken.None);

        Assert.True(reconciled.IsSuccess);

        Booking stored = context.Bookings.AsNoTracking().Single(b => b.Id == booking.Id);
        Assert.False(stored.IsRefundOutcomeAmbiguous);
        Assert.Equal(0, stored.RefundedMinorUnits);
        Assert.Equal(40000, stored.RefundableCeilingMinorUnits());
    }

    [Fact]
    public async Task Reconcile_WithoutAnAmbiguousAttempt_IsRefused()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking booking = SeedBooking(databaseName, family, elderly, revision: 2, accept: true);

        using FamiliesDbContext context = CreateContext(databaseName);
        var handler = new ReconcileBookingRefundAmbiguityCommandHandler(context);

        Result result = await handler.Handle(
            new ReconcileBookingRefundAmbiguityCommand(
                UserId.New(),
                booking.Id,
                "nothing to reconcile",
                RefundDidMove: false,
                AcceptedAtUtc),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Bookings.RefundReconciliation.NoAmbiguousAttempt", result.Error.Code);
    }

    // ---------------------------------------------------------------------------------------------
    // Fixtures.
    // ---------------------------------------------------------------------------------------------

    private static string NewDatabaseName() => $"ahlena-refund-handler-{Guid.NewGuid():N}";

    private static FamiliesDbContext CreateContext(string databaseName) =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);

    private static (Family Family, Elderly Elderly) SeedFamily(string databaseName)
    {
        using FamiliesDbContext context = CreateContext(databaseName);

        var family = Family.Create(UserId.New(), "Refund Contract Family");

        var elderly = Elderly.Create(
            family.OwnerUserId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("مسن تجريبي"),
            FullName.Create("Elderly Test"),
            Gender.Male,
            DateOnly.FromDateTime(CreatedOnUtc.AddYears(-70)),
            DateOnly.FromDateTime(CreatedOnUtc));

        context.Families.Add(family);
        context.Elderlies.Add(elderly);
        context.SaveChanges();

        return (family, elderly);
    }

    private static Booking SeedBooking(
        string databaseName,
        Family family,
        Elderly elderly,
        int revision,
        bool accept)
    {
        Booking booking = Booking.Create(
            family.Id,
            family.OwnerUserId,
            elderly.Id,
            CaregiverId.New(),
            BookingCaregiverType.Companion,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(ScheduledStartUtc),
            TimeOnly.FromDateTime(ScheduledStartUtc),
            TimeOnly.FromDateTime(ScheduledStartUtc).AddHours(2),
            "123 Nile St, Cairo",
            null,
            BookingPriceSnapshot.Calculate(400m, 0m, 0m, 1),
            acceptanceDeadlineUtc: ScheduledStartUtc.AddHours(-1),
            currentDate: DateOnly.FromDateTime(CreatedOnUtc),
            createdOnUtc: CreatedOnUtc,
            refundPolicyVersion: revision == 2
                ? BookingRefundPolicyVersion.ForNewBooking(scheduledStartPolicyActivated: true)
                : BookingRefundPolicyVersion.ForNewBooking(scheduledStartPolicyActivated: false));

        booking.RecordPaymentIntent("order", PaymentMethod.Card, CreatedOnUtc);
        booking.MarkAsPaid("order", "txn", CreatedOnUtc);

        if (accept)
        {
            booking.AcceptByCaregiver(AcceptedAtUtc);
        }

        using FamiliesDbContext context = CreateContext(databaseName);
        context.Bookings.Add(booking);
        context.SaveChanges();

        return booking;
    }

    private static async Task<Result> CancelAsync(
        string databaseName,
        RecordingPaymobClient paymob,
        BookingId bookingId,
        UserId actorUserId,
        DateTime utcNow)
    {
        using FamiliesDbContext context = CreateContext(databaseName);

        var handler = new CancelBookingCommandHandler(
            context,
            paymob,
            new BookingCancellationFactRecorder(context));

        return await handler.Handle(
            new CancelBookingCommand(
                bookingId,
                actorUserId,
                "family emergency",
                (int)BookingCancellationReasonCategory.Emergency,
                utcNow),
            CancellationToken.None);
    }

    private sealed record RefundCall(string PaymobTransactionId, decimal Amount);

    private sealed class RecordingPaymobClient : IPaymobClient
    {
        private readonly List<RefundCall> _refundCalls = [];

        public IReadOnlyList<RefundCall> RefundCalls => _refundCalls;

        public bool RefundSucceeds { get; init; } = true;

        /// <summary>Reports "could not confirm" instead of a clean refusal — the #17 case.</summary>
        public bool RefundOutcomeUnknown { get; init; }

        public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(
            PaymobPaymentIntentInput input,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<PaymobPaymentIntent>.Success(
                new PaymobPaymentIntent(
                    input.BookingId.Value.ToString(),
                    "test-intention",
                    "test-client-secret",
                    "test-public-key")));

        public Task<Result<string?>> RefundPaymentAsync(
            string paymobTransactionId,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            _refundCalls.Add(new RefundCall(paymobTransactionId, amount));

            if (RefundSucceeds)
                return Task.FromResult(Result<string?>.Success("test-refund-transaction"));

            return Task.FromResult(Result<string?>.Failure(new Error(
                RefundOutcomeUnknown ? "Paymob.RefundOutcomeUnknown" : "Paymob.RefundFailed",
                "The gateway did not confirm the refund.")));
        }

        public Task<Result<PaymobRefundVerification>> VerifyRefundTransactionAsync(
            string refundTransactionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<PaymobRefundVerification>.Failure(new Error(
                "Paymob.VerificationUnavailable",
                "Refund verification is not used by this test.")));
    }
}
