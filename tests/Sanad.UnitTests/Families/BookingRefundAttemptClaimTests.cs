using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Sanad.UnitTests.Families;

/// <summary>
/// The durable refund claim. Every settlement entry point must commit the attempt — the exact
/// award, its correlation id and an in-flight flag — <em>before</em> it calls the payment provider,
/// so that an attempt interrupted between the claim and the send, or between the send and the
/// result save, is never silently re-sent.
/// <para>
/// Authored, NOT RUN. The two-context test asserts the optimistic-concurrency mechanism at the EF
/// persistence boundary using the in-memory provider; it is <b>not</b> evidence about relational
/// (PostgreSQL) row locking or isolation. Proving that two concurrent requests cannot both reach a
/// live gateway needs the approved disposable PostgreSQL gate.
/// </para>
/// </summary>
public sealed class BookingRefundAttemptClaimTests
{
    // ---------------------------------------------------------------------------------------------
    // Fix 3 — the claim is committed before the external call.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task FamilyCancel_CommitsTheRefundClaimBeforeCallingTheProvider()
    {
        string databaseName = NewDatabaseName();
        Family family = SeedFamily(databaseName);
        DateTime now = DateTime.UtcNow;
        BookingId bookingId = SeedPaidConfirmedBooking(databaseName, family, now);

        var paymob = new RecordingPaymobClient
        {
            InspectBeforeSending = persisted =>
            {
                // A separate DbContext can only see committed state, so this is the durability
                // boundary: by the time the gateway is called, the claim must already be stored.
                Assert.True(
                    persisted.IsRefundAttemptInFlight,
                    "the refund claim must be committed before the provider call");
                Assert.NotNull(persisted.RefundAttemptClaimId);
                Assert.NotNull(persisted.RefundAttemptClaimedOnUtc);
                Assert.Equal(20000, persisted.RefundAttemptAwardMinorUnits);
                Assert.True(persisted.RefundAttemptVersion > 0);
            },
            DatabaseName = databaseName,
            BookingIdToInspect = bookingId
        };

        using FamiliesDbContext context = CreateContext(databaseName);
        var handler = new CancelBookingCommandHandler(
            context,
            paymob,
            new StubCancellationFactRecorder());

        DateTime scheduledStart = ScheduledStart(context, bookingId);
        DateTime actedOnUtc = scheduledStart - TimeSpan.FromHours(2);

        Result result = await handler.Handle(
            new CancelBookingCommand(
                bookingId,
                family.OwnerUserId,
                "family emergency",
                (int)BookingCancellationReasonCategory.Emergency,
                actedOnUtc),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        // The award — the clause-2 50% share of 400.00 — is what the gateway was asked to move.
        (string transactionId, decimal amount) = Assert.Single(paymob.Calls);
        Assert.False(string.IsNullOrWhiteSpace(transactionId));
        Assert.Equal(200.00m, amount);

        // Settled exactly once: the claim was released and the ceiling advanced by the award.
        using FamiliesDbContext stored = CreateContext(databaseName);
        Booking persisted = stored.Bookings.AsNoTracking().Single(b => b.Id == bookingId);
        Assert.False(persisted.IsRefundAttemptInFlight);
        Assert.Equal(20000, persisted.RefundedMinorUnits);
        Assert.Equal(20000, persisted.RefundableCeilingMinorUnits());
    }

    [Fact]
    public async Task FamilyCancel_WhenTheGatewayOutcomeIsUnknown_ReadsPendingAndRefusesEveryRetry()
    {
        string databaseName = NewDatabaseName();
        Family family = SeedFamily(databaseName);
        DateTime now = DateTime.UtcNow;
        BookingId bookingId = SeedPaidConfirmedBooking(databaseName, family, now);

        var paymob = new RecordingPaymobClient
        {
            ErrorCode = "Paymob.RefundOutcomeUnknown",
            DatabaseName = databaseName,
            BookingIdToInspect = bookingId
        };

        DateTime scheduledStart = ScheduledStart(databaseName, bookingId);
        DateTime actedOnUtc = scheduledStart - TimeSpan.FromHours(2);

        using (FamiliesDbContext context = CreateContext(databaseName))
        {
            var handler = new CancelBookingCommandHandler(
                context,
                paymob,
                new StubCancellationFactRecorder());

            Result result = await handler.Handle(
                new CancelBookingCommand(
                    bookingId,
                    family.OwnerUserId,
                    "family emergency",
                    (int)BookingCancellationReasonCategory.Emergency,
                    actedOnUtc),
                CancellationToken.None);

            // An unconfirmed outcome is surfaced, never swallowed.
            Assert.True(result.IsFailure);
            Assert.Equal("Paymob.RefundOutcomeUnknown", result.Error.Code);
        }

        using (FamiliesDbContext stored = CreateContext(databaseName))
        {
            Booking booking = stored.Bookings.AsNoTracking().Single(b => b.Id == bookingId);
            BookingCancellationFact? fact = stored.BookingCancellationFacts
                .AsNoTracking()
                .SingleOrDefault(f => f.BookingId == bookingId);

            Assert.True(booking.IsRefundOutcomeAmbiguous);

            // The marker is the stricter state: the in-flight claim is folded into it.
            Assert.False(booking.IsRefundAttemptInFlight);

            // Nothing is booked against the ceiling, so the money cannot be paid twice.
            Assert.Equal(0, booking.RefundedMinorUnits);
            Assert.Equal(40000, booking.RefundableCeilingMinorUnits());

            // Truthful reads: unconfirmed is Pending, never Succeeded and never a plain failure.
            Assert.Equal(BookingRefundState.Pending, BookingRefundStates.ResolveWithFact(booking, fact));
        }

        // Even the operator's retry is refused until the attempt is reconciled.
        var retryPaymob = new RecordingPaymobClient
        {
            DatabaseName = databaseName,
            BookingIdToInspect = bookingId
        };

        using (FamiliesDbContext context = CreateContext(databaseName))
        {
            var retry = new AdminRefundBookingCommandHandler(context, retryPaymob);

            Result<BookingDetailResponse> result = await retry.Handle(
                new AdminRefundBookingCommand(bookingId, DateTime.UtcNow.AddMinutes(5)),
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal("Bookings.RefundOutcomeAmbiguous", result.Error.Code);
            Assert.Empty(retryPaymob.Calls);
        }
    }

    [Fact]
    public async Task FamilyCancel_WhenTheGatewayRefuses_ReleasesTheClaimForABoundedRetry()
    {
        string databaseName = NewDatabaseName();
        Family family = SeedFamily(databaseName);
        DateTime now = DateTime.UtcNow;
        BookingId bookingId = SeedPaidConfirmedBooking(databaseName, family, now);

        var paymob = new RecordingPaymobClient
        {
            ErrorCode = "Paymob.RefundFailed",
            DatabaseName = databaseName,
            BookingIdToInspect = bookingId
        };

        DateTime scheduledStart = ScheduledStart(databaseName, bookingId);
        DateTime actedOnUtc = scheduledStart - TimeSpan.FromHours(2);

        using (FamiliesDbContext context = CreateContext(databaseName))
        {
            var handler = new CancelBookingCommandHandler(
                context,
                paymob,
                new StubCancellationFactRecorder());

            Result result = await handler.Handle(
                new CancelBookingCommand(
                    bookingId,
                    family.OwnerUserId,
                    "family emergency",
                    (int)BookingCancellationReasonCategory.Emergency,
                    actedOnUtc),
                CancellationToken.None);

            // Pre-existing policy: a definitive refusal does not fail the family cancellation.
            Assert.True(result.IsSuccess);
        }

        using FamiliesDbContext stored = CreateContext(databaseName);
        Booking booking = stored.Bookings.AsNoTracking().Single(b => b.Id == bookingId);

        // A definitive refusal proves no money moved, so the attempt is not left in flight and the
        // ceiling is untouched — a bounded retry stays possible.
        Assert.False(booking.IsRefundAttemptInFlight);
        Assert.False(booking.IsRefundOutcomeAmbiguous);
        Assert.Equal(0, booking.RefundedMinorUnits);
        Assert.Equal(40000, booking.RefundableCeilingMinorUnits());
        Assert.Null(Record.Exception(() => booking.EnsureRefundAttemptAllowed()));
    }

    // ---------------------------------------------------------------------------------------------
    // Fix 3 — an interrupted attempt requires reconciliation, never an automatic resend.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task InterruptedClaim_IsNeverResent_AndOnlyReconciliationReopensTheBooking()
    {
        string databaseName = NewDatabaseName();
        Family family = SeedFamily(databaseName);
        DateTime now = DateTime.UtcNow;
        BookingId bookingId = SeedPaidConfirmedBooking(databaseName, family, now);

        // Simulate the crash window: the claim was committed, then the process died before the
        // provider was called, so no provider result exists. The revision-2 fact (the award the
        // policy owed) was committed in the same window, which is what tells a later retry that
        // only 200.00 of the 400.00 capture is owed.
        Guid claimId = Guid.NewGuid();
        (BookingRefundDecision partialDecision, BookingCancellationFeedback partialFeedback) =
            RevisionTwoPartialPolicy(now);

        using (FamiliesDbContext seed = CreateContext(databaseName))
        {
            Booking booking = seed.Bookings.Single(b => b.Id == bookingId);
            booking.CancelByFamily("family emergency", now);
            booking.ClaimRefundAttempt(claimId, 20000, now);
            seed.BookingCancellationFacts.Add(
                BookingCancellationFact.Create(
                    bookingId,
                    family.OwnerUserId,
                    partialDecision,
                    partialFeedback));
            seed.SaveChanges();
        }

        var paymob = new RecordingPaymobClient
        {
            DatabaseName = databaseName,
            BookingIdToInspect = bookingId
        };

        using (FamiliesDbContext context = CreateContext(databaseName))
        {
            var retry = new AdminRefundBookingCommandHandler(context, paymob);

            Result<BookingDetailResponse> result = await retry.Handle(
                new AdminRefundBookingCommand(bookingId, now.AddMinutes(1)),
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal("Bookings.RefundOutcomeAmbiguous", result.Error.Code);
            Assert.Empty(paymob.Calls);
        }

        using (FamiliesDbContext stored = CreateContext(databaseName))
        {
            Booking booking = stored.Bookings.AsNoTracking().Single(b => b.Id == bookingId);
            Assert.True(booking.IsRefundAttemptInFlight);
            Assert.Equal(claimId, booking.RefundAttemptClaimId);
            Assert.Throws<DomainException>(() => booking.EnsureRefundAttemptAllowed());
            Assert.Equal(BookingRefundState.Pending, BookingRefundStates.ResolveWithFact(booking, null));
        }

        // The claim may still be live at the provider. A bare "no refund" assertion is refused:
        // elapsed time and an operator note are not proof that the call is not still in flight.
        using (FamiliesDbContext context = CreateContext(databaseName))
        {
            var reconcile = new ReconcileBookingRefundAmbiguityCommandHandler(context);

            Result refused = await reconcile.Handle(
                new ReconcileBookingRefundAmbiguityCommand(
                    UserId.New(),
                    bookingId,
                    "gateway dashboard: no refund was issued for this transaction",
                    RefundDidMove: false,
                    now.AddMinutes(5),
                    ExpectedAttemptClaimId: claimId),
                CancellationToken.None);

            Assert.False(refused.IsSuccess);
            Assert.Equal("Bookings.RefundReconciliation.ClaimInFlight", refused.Error.Code);
        }

        // The only evidence that can conclude an in-flight claim is the provider's own refund
        // reference. A verified movement books the recorded award exactly once.
        using (FamiliesDbContext context = CreateContext(databaseName))
        {
            var reconcile = new ReconcileBookingRefundAmbiguityCommandHandler(context);

            Result reconciled = await reconcile.Handle(
                new ReconcileBookingRefundAmbiguityCommand(
                    UserId.New(),
                    bookingId,
                    "gateway dashboard: refund 9911 completed",
                    RefundDidMove: true,
                    now.AddMinutes(6),
                    ExpectedAttemptClaimId: claimId,
                    VerifiedProviderReference: "9911"),
                CancellationToken.None);

            Assert.True(reconciled.IsSuccess);
        }

        using (FamiliesDbContext stored = CreateContext(databaseName))
        {
            Booking booking = stored.Bookings.AsNoTracking().Single(b => b.Id == bookingId);
            Assert.False(booking.IsRefundAttemptInFlight);
            Assert.Equal(20000, booking.RefundedMinorUnits);
            Assert.Equal(20000, booking.RefundableCeilingMinorUnits());
            Assert.Null(Record.Exception(() => booking.EnsureRefundAttemptAllowed()));
        }

        // The award is settled; a retry must never pay it a second time.
        using (FamiliesDbContext context = CreateContext(databaseName))
        {
            var retry = new AdminRefundBookingCommandHandler(context, paymob);

            Result<BookingDetailResponse> result = await retry.Handle(
                new AdminRefundBookingCommand(bookingId, now.AddMinutes(10)),
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal("Bookings.AlreadyRefunded", result.Error.Code);
        }

        Assert.Empty(paymob.Calls);
    }

    [Fact]
    public async Task LateProviderConfirmation_AfterAConcurrentReconciliation_IsSavedIdempotently()
    {
        string databaseName = NewDatabaseName();
        Family family = SeedFamily(databaseName);
        DateTime now = DateTime.UtcNow;
        BookingId bookingId = SeedPaidConfirmedBooking(databaseName, family, now);

        // The attempt claims and commits before its provider call.
        Guid claimId = Guid.NewGuid();

        using (FamiliesDbContext claimContext = CreateContext(databaseName))
        {
            Booking booking = claimContext.Bookings.Single(b => b.Id == bookingId);
            booking.CancelByFamily("family emergency", now);
            booking.ClaimRefundAttempt(claimId, 20000, now);
            await claimContext.SaveChangesAsync();
        }

        // The request that is about to call the provider holds its own stale copy of the row.
        using FamiliesDbContext requestContext = CreateContext(databaseName);
        Booking stale = await requestContext.Bookings.SingleAsync(b => b.Id == bookingId);

        // While that call runs, an operator reconciliation records the same movement on a fresh row.
        using (FamiliesDbContext reconcileContext = CreateContext(databaseName))
        {
            Booking booking = reconcileContext.Bookings.Single(b => b.Id == bookingId);
            booking.RecordRefundedAmount(20000, now.AddMinutes(1));
            booking.ClearInterruptedRefundAttempt("operator: verified refund", now.AddMinutes(1));
            await reconcileContext.SaveChangesAsync();
        }

        // The provider now confirms success. The stale request loses the optimistic-concurrency
        // race at the save; a real settlement must not be discarded because of that.
        stale.MarkRefunded("refund-1", now.AddMinutes(2));
        stale.RecordRefundedAmount(20000, now.AddMinutes(2));
        stale.ReleaseRefundAttempt(now.AddMinutes(2));

        bool recorded = await BookingRefundPersistence.TrySaveClaimedOutcomeAsync(
            requestContext,
            bookingId,
            20000,
            CancellationToken.None);

        Assert.True(recorded);

        using FamiliesDbContext stored = CreateContext(databaseName);
        Booking persisted = stored.Bookings.AsNoTracking().Single(b => b.Id == bookingId);
        Assert.False(persisted.IsRefundAttemptInFlight);
        Assert.Equal(20000, persisted.RefundedMinorUnits);
    }

    [Fact]
    public async Task TwoContextsClaimingTheSameBooking_OnlyTheFirstCommitSucceeds()
    {
        string databaseName = NewDatabaseName();
        Family family = SeedFamily(databaseName);
        DateTime now = DateTime.UtcNow;
        BookingId bookingId = SeedPaidConfirmedBooking(databaseName, family, now);

        using FamiliesDbContext first = CreateContext(databaseName);
        using FamiliesDbContext second = CreateContext(databaseName);

        Booking firstCopy = await first.Bookings.SingleAsync(b => b.Id == bookingId);
        Booking secondCopy = await second.Bookings.SingleAsync(b => b.Id == bookingId);

        // Both requests read the same booking and both believe they may claim it.
        firstCopy.ClaimRefundAttempt(Guid.NewGuid(), 20000, now);
        await first.SaveChangesAsync();

        secondCopy.ClaimRefundAttempt(Guid.NewGuid(), 20000, now);

        // The stale claimant must fail at the persistence boundary, which is why it can never reach
        // the provider. Relational proof for the same rule needs the PostgreSQL gate.
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        using FamiliesDbContext stored = CreateContext(databaseName);
        Booking persisted = stored.Bookings.AsNoTracking().Single(b => b.Id == bookingId);
        Assert.True(persisted.IsRefundAttemptInFlight);
        Assert.Equal(20000, persisted.RefundAttemptAwardMinorUnits);
    }

    // ---------------------------------------------------------------------------------------------
    // Fix 4 — the refund state is derived from settled money against the owed award.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ResolveWithFact_PartialAwardDefinitivelyRefused_ReportsFailed()
    {
        (Booking booking, BookingCancellationFact fact) = RevisionTwoPartial();

        Assert.Equal(0L, booking.RefundedMinorUnits);
        Assert.Equal(BookingRefundState.Failed, BookingRefundStates.ResolveWithFact(booking, fact));
    }

    [Fact]
    public void ResolveWithFact_PartialAwardSettled_ReportsSucceeded()
    {
        (Booking booking, BookingCancellationFact fact) = RevisionTwoPartial();
        DateTime now = DateTime.UtcNow;

        booking.RecordRefundedAmount(20000, now);

        Assert.Equal(BookingRefundState.Succeeded, BookingRefundStates.ResolveWithFact(booking, fact));
    }

    [Fact]
    public void ResolveWithFact_PartialAwardOnlyPartlySettled_ReportsFailed()
    {
        (Booking booking, BookingCancellationFact fact) = RevisionTwoPartial();
        DateTime now = DateTime.UtcNow;

        booking.RecordRefundedAmount(5000, now);

        // Money moved, but the owed award did not settle: reporting success here would hide the
        // remainder and invite a double refund if it were ever retried blindly.
        Assert.Equal(BookingRefundState.Failed, BookingRefundStates.ResolveWithFact(booking, fact));
    }

    [Fact]
    public void ResolveWithFact_PartialAwardOutcomeUnconfirmed_ReportsPending()
    {
        (Booking booking, BookingCancellationFact fact) = RevisionTwoPartial();
        DateTime now = DateTime.UtcNow;

        booking.MarkRefundOutcomeAmbiguous(
            "txn-1",
            "Paymob.RefundOutcomeUnknown",
            now,
            20000);

        Assert.Equal(BookingRefundState.Pending, BookingRefundStates.ResolveWithFact(booking, fact));
    }

    [Fact]
    public void ResolveWithFact_PartialAwardStillInFlight_ReportsPending()
    {
        (Booking booking, BookingCancellationFact fact) = RevisionTwoPartial();
        DateTime now = DateTime.UtcNow;

        booking.ClaimRefundAttempt(Guid.NewGuid(), 20000, now);

        Assert.Equal(BookingRefundState.Pending, BookingRefundStates.ResolveWithFact(booking, fact));
    }

    /// <summary>
    /// The add-only guard rails on the fact-less read. The admin, caregiver and family list
    /// projections resolve the state without loading the cancellation fact, so an uncertain attempt
    /// must not fall through to the legacy "paid + closed + not refunded" reading of Failed — that
    /// would present a non-retryable booking as the ops retry queue.
    /// </summary>
    [Fact]
    public void Resolve_WithoutTheFact_ReportsAnUnconfirmedAttemptAsPending()
    {
        (Booking booking, _) = RevisionTwoPartial();

        booking.MarkRefundOutcomeAmbiguous(
            "txn-1",
            "Paymob.RefundOutcomeUnknown",
            DateTime.UtcNow,
            20000);

        Assert.Equal(BookingRefundState.Pending, BookingRefundStates.Resolve(booking));
        Assert.Equal(
            BookingRefundState.Pending,
            BookingRefundStates.ResolveWithFact(booking, fact: null));
    }

    [Fact]
    public void Resolve_WithoutTheFact_ReportsAnInFlightClaimAsPending()
    {
        (Booking booking, _) = RevisionTwoPartial();

        booking.ClaimRefundAttempt(Guid.NewGuid(), 20000, DateTime.UtcNow);

        Assert.Equal(BookingRefundState.Pending, BookingRefundStates.Resolve(booking));
    }

    // ---------------------------------------------------------------------------------------------
    // Fixtures.
    // ---------------------------------------------------------------------------------------------

    private static string NewDatabaseName() => $"sanad-refund-claim-{Guid.NewGuid():N}";

    private static FamiliesDbContext CreateContext(string databaseName) =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);

    private static DateTime ScheduledStart(string databaseName, BookingId bookingId)
    {
        using FamiliesDbContext context = CreateContext(databaseName);
        return ScheduledStart(context, bookingId);
    }

    private static DateTime ScheduledStart(FamiliesDbContext context, BookingId bookingId)
    {
        Booking booking = context.Bookings.AsNoTracking().Single(b => b.Id == bookingId);
        return booking.BookingDate.ToDateTime(booking.StartTime, DateTimeKind.Utc);
    }

    private static Family SeedFamily(string databaseName)
    {
        using FamiliesDbContext context = CreateContext(databaseName);

        var family = Family.Create(UserId.New(), "Refund Claim Family");
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

        return family;
    }

    /// <summary>
    /// A paid 400.00 booking in <c>Confirmed</c>, sold under revision 2. Cancelling it two hours
    /// before its scheduled start is the clause-2 case that owes the 50% share (200.00).
    /// </summary>
    private static BookingId SeedPaidConfirmedBooking(
        string databaseName,
        Family family,
        DateTime now)
    {
        using FamiliesDbContext context = CreateContext(databaseName);

        Elderly elderly = context.Elderlies.AsNoTracking().First(e => e.FamilyId == family.Id);
        Booking booking = CreatePaidConfirmedBooking(family, elderly, now);

        context.Bookings.Add(booking);
        context.SaveChanges();

        return booking.Id;
    }

    private static Booking CreatePaidConfirmedBooking(Family family, Elderly elderly, DateTime now)
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
            now.AddHours(24),
            DateOnly.FromDateTime(now),
            now,
            refundPolicyVersion: BookingRefundPolicyVersion.ForNewBooking(
                scheduledStartPolicyActivated: true));

        booking.RecordPaymentIntent("order-1", PaymentMethod.Card, now);
        booking.MarkAsPaid("order-1", "txn-1", now.AddMinutes(1));
        booking.AcceptByCaregiver(now.AddMinutes(2));
        return booking;
    }

    /// <summary>
    /// The revision-2 policy decision (and the feedback it was made with) for a booking created at
    /// <paramref name="now"/> and cancelled at the clause-2 boundary two hours before its start.
    /// The policy is asked on an untouched <c>Confirmed</c> copy, exactly as a handler does.
    /// </summary>
    private static (BookingRefundDecision Decision, BookingCancellationFeedback Feedback)
        RevisionTwoPartialPolicy(DateTime now)
    {
        var family = Family.Create(UserId.New(), "Refund State Family");
        var elderly = Elderly.Create(
            family.OwnerUserId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("مسن تجريبي"),
            FullName.Create("Elderly Test"),
            Gender.Male,
            DateOnly.FromDateTime(now.AddYears(-70)),
            DateOnly.FromDateTime(now));

        Booking pristine = CreatePaidConfirmedBooking(family, elderly, now);
        DateTime scheduledStart = pristine.BookingDate.ToDateTime(pristine.StartTime, DateTimeKind.Utc);
        DateTime actedOnUtc = scheduledStart - TimeSpan.FromHours(2);

        BookingCancellationFeedback feedback = BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.Emergency,
            "family emergency");

        BookingRefundDecision decision = BookingRefundPolicy.Decide(
            BookingRefundPolicyInput.ForFamilyCancellation(pristine, actedOnUtc, feedback));

        Assert.Equal(BookingRefundEntitlement.PartialRefund, decision.RefundEntitlement);
        Assert.Equal(20000, decision.AwardedMinorUnits);

        return (decision, feedback);
    }

    /// <summary>
    /// A revision-2 booking cancelled at the clause-2 50% boundary, plus the fact the handler would
    /// have recorded: 400.00 captured, 200.00 awarded, nothing settled yet.
    /// </summary>
    private static (Booking Booking, BookingCancellationFact Fact) RevisionTwoPartial()
    {
        DateTime now = DateTime.UtcNow;
        (BookingRefundDecision decision, BookingCancellationFeedback feedback) =
            RevisionTwoPartialPolicy(now);

        var family = Family.Create(UserId.New(), "Refund State Family");
        var elderly = Elderly.Create(
            family.OwnerUserId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("مسن تجريبي"),
            FullName.Create("Elderly Test"),
            Gender.Male,
            DateOnly.FromDateTime(now.AddYears(-70)),
            DateOnly.FromDateTime(now));

        Booking booking = CreatePaidConfirmedBooking(family, elderly, now);
        DateTime scheduledStart = booking.BookingDate.ToDateTime(booking.StartTime, DateTimeKind.Utc);

        booking.CancelByFamily("family emergency", scheduledStart - TimeSpan.FromHours(2));

        BookingCancellationFact fact = BookingCancellationFact.Create(
            booking.Id,
            family.OwnerUserId,
            decision,
            feedback);

        return (booking, fact);
    }

    private sealed class StubCancellationFactRecorder : IBookingCancellationFactRecorder
    {
        public Task RecordAsync(
            BookingCancellationFact fact,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>
    /// Provider spy. When <see cref="InspectBeforeSending"/> is set it runs against a fresh
    /// DbContext at the moment of the provider call, which is the only way to observe what was
    /// committed before any external effect.
    /// </summary>
    private sealed class RecordingPaymobClient : IPaymobClient
    {
        public List<(string PaymobTransactionId, decimal Amount)> Calls { get; } = [];

        public string? DatabaseName { get; init; }

        public BookingId? BookingIdToInspect { get; init; }

        public Action<Booking>? InspectBeforeSending { get; init; }

        /// <summary>When set, the refund fails with this code; otherwise it succeeds.</summary>
        public string? ErrorCode { get; init; }

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
            Calls.Add((paymobTransactionId, amount));

            if (InspectBeforeSending is not null
                && DatabaseName is not null
                && BookingIdToInspect is { } bookingId)
            {
                using FamiliesDbContext probe = CreateContext(DatabaseName);
                Booking persisted = probe.Bookings.AsNoTracking().Single(b => b.Id == bookingId);
                InspectBeforeSending(persisted);
            }

            if (ErrorCode is not null)
            {
                return Task.FromResult(Result<string?>.Failure(
                    new Error(ErrorCode, "The gateway did not complete the refund.")));
            }

            return Task.FromResult(Result<string?>.Success("refund-1"));
        }

        public Task<Result<PaymobRefundVerification>> VerifyRefundTransactionAsync(
            string refundTransactionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<PaymobRefundVerification>.Failure(new Error(
                "Paymob.VerificationUnavailable",
                "Refund verification is not used by this test.")));
    }
}
