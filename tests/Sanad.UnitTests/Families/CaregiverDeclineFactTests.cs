using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Caregivers;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Domain.Activities;
using Sanad.Modules.Families.Domain.Assessments;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Invitations;
using Sanad.Modules.Families.Domain.Medications;
using Sanad.Modules.Families.Domain.Notes;
using Sanad.Modules.Families.Infrastructure.Data;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Sanad.UnitTests.Families;

/// <summary>
/// Independent handler-level contract tests for <see cref="CaregiverDeclineBookingCommandHandler"/> as
/// merged at <c>fbcb8cb1d5d3c49e874d1f65af9cc9b7018015e1</c> (caregiver confirmed-cancel + reject facts,
/// B1-B3). Authored, not executed: no build, test run or migration is performed here; the owner is the
/// only build machine.
/// <para>
/// Rules these tests hold the handler to, read from the merged source:
/// <list type="bullet">
/// <item><description>
/// A caregiver rejection (decline) happens before acceptance, on a paid booking still in
/// <see cref="BookingStatus.PendingCaregiverApproval"/>: the feedback is an optional plain note, a
/// category may never be invented, the policy owes a full captured refund and the fact carries no
/// caregiver incident flag.
/// </description></item>
/// <item><description>
/// <see cref="BookingCancellationPolicy.Decide"/> runs on the untouched aggregate BEFORE the decline
/// mutation, so a paid-status booking whose capture evidence cannot be resolved is refused instead of
/// declining silently, and a decline on a Confirmed booking is refused by the policy (a change of
/// error code from the legacy path, pinned as-is).
/// </description></item>
/// <item><description>
/// The refund is attempted once for the succeeded transaction; <c>MarkRefunded</c> only on gateway
/// success; a refusal leaves the booking <see cref="BookingStatus.DeclinedByCaregiver"/>.
/// </description></item>
/// <item><description>
/// The rejection fact commits in the same single save as the decline itself, through the production
/// <see cref="BookingCancellationFactRecorder"/>. A fact unique violation on save maps to
/// <c>Bookings.Cancel.AlreadyProcessed</c> (the catch ordering the shared guard exists for); any other
/// save failure keeps the legacy <c>Bookings.TransitionFailed</c>.
/// </description></item>
/// </list>
/// </para>
/// <para>
/// Fixtures are built only through public application and domain entry points (checkout, payment
/// intent, the Paymob confirmation webhook, caregiver accept); no reflection, no internals. Stored
/// state is always read through a fresh context over the same in-memory database. The helpers in this
/// class are private on purpose; they are local copies of the patterns in
/// <c>CancelBookingCommandHandlerTests</c> and are not shared across test classes.
/// </para>
/// </summary>
public sealed class CaregiverDeclineFactTests
{
    /// <summary>Authoritative "now" for the declines under test; passed to the commands explicitly.</summary>
    private static readonly DateTime UtcNow = new(2026, 9, 18, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>Booking slot two hours after <see cref="UtcNow"/> (10:00 UTC), far from the decline times.</summary>
    private static readonly DateTime DistantStartUtc = new(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);

    // =============================================================================================
    // The reject fact and its money path.
    // =============================================================================================

    [Fact]
    public async Task B1_PaidPendingApprovalDeclineWithPlainNote_RefundsOnce_AndRecordsTheRejectFactWithoutCategoryOrIncident()
    {
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        Booking booking = await CheckoutAsync(dbContext, family, elderly, UtcNow.AddMinutes(-30), DistantStartUtc);
        booking = await PayAsync(dbContext, booking, paymob, UtcNow.AddMinutes(-10), 901100001);
        PaymentTransaction capture = Assert.Single(booking.PaymentTransactions);

        // 500 base + 15% platform fee, captured once at checkout pricing.
        Assert.Equal(575m, capture.Amount);

        const string rawNote = "  schedule conflict  ";

        Result result = await DeclineAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            rawNote,
            UtcNow);

        Assert.True(result.IsSuccess);

        Assert.Single(paymob.RefundCalls);
        Assert.Equal(capture.PaymobTransactionId, paymob.RefundCalls[0].PaymobTransactionId);
        Assert.Equal(capture.Amount, paymob.RefundCalls[0].Amount);

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        Assert.Equal(BookingStatus.Refunded, persisted.Status);
        Assert.Equal("schedule conflict", persisted.CancellationReason);
        Assert.Equal(UtcNow, persisted.CancelledOnUtc);
        Assert.Equal(UtcNow, persisted.RefundedOnUtc);
        Assert.Equal(paymob.RefundTransactionId, persisted.PaymobRefundTransactionId);
        Assert.Equal(PaymentTransactionStatus.Refunded, Assert.Single(persisted.PaymentTransactions).Status);

        BookingCancellationFact fact = await SingleFactAsync(store);

        AssertFactSpine(fact, booking.Id, caregiverUserId, UtcNow);
        Assert.Equal(BookingStatus.PendingCaregiverApproval, fact.StatusAtCancellation);
        Assert.Null(fact.ConfirmedOnUtcUsed);
        Assert.Null(fact.ReasonCategory);
        Assert.Equal("schedule conflict", fact.ReasonNote);
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, fact.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.CaregiverRejectionBeforeAcceptance, fact.RefundDecisionReason);
    }

    [Fact]
    public async Task B2_PaidDeclineWithBlankReason_StillSucceeds_WithNullNoteAndNullCategoryOnTheFact()
    {
        // Pre-acceptance feedback stays optional: a blank note normalizes to null everywhere, and
        // nothing becomes mandatory.
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        Booking booking = await CheckoutAsync(dbContext, family, elderly, UtcNow.AddMinutes(-30), DistantStartUtc);
        booking = await PayAsync(dbContext, booking, paymob, UtcNow.AddMinutes(-10), 901200001);

        Result result = await DeclineAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            "   ",
            UtcNow);

        Assert.True(result.IsSuccess);

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        Assert.Equal(BookingStatus.Refunded, persisted.Status);
        Assert.Null(persisted.CancellationReason);

        BookingCancellationFact fact = await SingleFactAsync(store);

        AssertFactSpine(fact, booking.Id, caregiverUserId, UtcNow);
        Assert.Equal(BookingStatus.PendingCaregiverApproval, fact.StatusAtCancellation);
        Assert.Null(fact.ReasonNote);
        Assert.Null(fact.ReasonCategory);
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, fact.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.CaregiverRejectionBeforeAcceptance, fact.RefundDecisionReason);
    }

    [Fact]
    public async Task B3_DeclineOfAPaidStatusBookingWhoseCaptureEvidenceCannotBeResolved_IsRefusedInsteadOfSilentlyDeclining()
    {
        // This pins the disclosed consequence of the policy running first: a decline now refuses
        // instead of silently dropping history. The state here says paid (PendingCaregiverApproval)
        // while no transaction was ever succeeded: the legacy markers alone make the capture
        // evidence Ambiguous — uncertainty, never proof that nothing was paid — and the policy fails
        // closed on it before any decline mutation.
        //
        // Reachability note: the exact "no succeeded transaction AND no legacy paid markers at all"
        // (NotCaptured) shape is unreachable through the public factories — every transition into
        // PendingCaregiverApproval (MarkAsPaid) writes both legacy markers, and no public member
        // removes them. The Ambiguous shape is the reachable manifestation of the same refusal.
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        Booking booking = await CheckoutAsync(dbContext, family, elderly, UtcNow.AddMinutes(-30), DistantStartUtc);

        // No payment intent was ever recorded: the legacy markers exist, but no transaction backs them.
        booking.MarkAsPaid("PM-ORDER-GHOST", "PM-TXN-GHOST", UtcNow.AddMinutes(-10));
        await dbContext.SaveChangesAsync();

        booking = await PersistedBookingAsync(dbContext, booking.Id);

        Assert.Equal(BookingStatus.PendingCaregiverApproval, booking.Status);
        Assert.Empty(booking.PaymentTransactions);
        Assert.Equal(
            BookingCaptureEvidence.Ambiguous,
            BookingCancellationPolicyInput.ResolveCaptureEvidence(booking));

        Result result = await DeclineAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            "unavailable",
            UtcNow);

        AssertFailedWith(result, "Bookings.Domain.InvalidOperation");
        Assert.Contains("missing or inconsistent", result.Error.Message);
        await AssertStoreUntouchedAsync(databaseName, paymob, booking.Id, BookingStatus.PendingCaregiverApproval);
    }

    [Fact]
    public async Task B4_DeclineOnAConfirmedBooking_IsRefusedWithDomainInvalidOperation_NotTheLegacyTransitionFailed()
    {
        // NEW disclosure from the coordinator review, pinned as-is: before this slice, the legacy
        // path answered "Bookings.TransitionFailed" for this case, because the status refusal came
        // from the aggregate inside the inner catch. The policy now runs first, so the code changed
        // from the 400 shape (Bookings.TransitionFailed, an unmapped code) to the 409 shape
        // (Bookings.Domain.InvalidOperation). No mutation, no money, no state difference — the
        // booking stays untouched in both versions. Flagged in case the owner wants the family-side
        // error vocabulary unified later; the production code is deliberately not changed here.
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        Booking booking = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, UtcNow.AddMinutes(-20), DistantStartUtc);

        Result result = await DeclineAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            "no longer possible",
            UtcNow);

        AssertFailedWith(result, "Bookings.Domain.InvalidOperation");
        Assert.Contains("only reject", result.Error.Message);
        await AssertStoreUntouchedAsync(databaseName, paymob, booking.Id, BookingStatus.Confirmed);
    }

    [Fact]
    public async Task B5_FactUniqueViolationOnDeclineSave_MapsToAlreadyProcessed_NotTheLegacyTransitionFailed()
    {
        // Pins the catch ordering in the decline handler: the fact-unique-violation filter is ordered
        // BEFORE the inner generic catch, so a parallel-decline race (a fact for this booking was
        // committed first) maps to Bookings.Cancel.AlreadyProcessed, never to the legacy
        // Bookings.TransitionFailed. That ordering is the whole point of the shared
        // CancellationPersistenceGuard.
        string databaseName = NewDatabaseName();
        using FamiliesDbContext arrangeContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(arrangeContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        Booking booking = await CheckoutAsync(arrangeContext, family, elderly, UtcNow.AddMinutes(-30), DistantStartUtc);
        booking = await PayAsync(arrangeContext, booking, paymob, UtcNow.AddMinutes(-10), 901500001);

        // The InMemory provider never enforces unique indexes, so the parallel-decline race is
        // arranged at the persistence seam instead: the handler's own save reports the unique
        // violation that PostgreSQL reports for ux_booking_cancellation_facts_booking.
        using FamiliesDbContext handlerContext = CreateDbContext(databaseName);
        var seam = new SaveSeamContext(handlerContext, FactUniqueViolationFailure());

        Result result = await DeclineWithAsync(
            seam,
            new BookingCancellationFactRecorder(handlerContext),
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            "conflict",
            UtcNow);

        AssertFailedWith(result, "Bookings.Cancel.AlreadyProcessed");

        // The fact reached the change tracker through the production recorder, and the blocked save
        // committed none of it.
        EntityEntry<BookingCancellationFact> trackedFact =
            Assert.Single(handlerContext.ChangeTracker.Entries<BookingCancellationFact>());
        Assert.Equal(EntityState.Added, trackedFact.State);

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        Assert.Equal(BookingStatus.PendingCaregiverApproval, persisted.Status);
        Assert.Null(persisted.CancelledOnUtc);
        Assert.Null(persisted.CancellationReason);
        Assert.Null(persisted.PaymobRefundTransactionId);
        Assert.Null(persisted.RefundedOnUtc);
        await AssertNoFactsAsync(store);

        // Order of operations as merged: the provider refund is attempted before the single save, and
        // a failed save does not undo a gateway call. Recorded here as observed behaviour.
        Assert.Single(paymob.RefundCalls);
    }

    [Fact]
    public async Task B6_UnrelatedDeclineSaveFailure_PreservesTheLegacyTransitionFailed()
    {
        // Control case: an unrelated persistence failure is not the fact unique violation, so it falls
        // through to the inner generic catch and keeps the legacy Bookings.TransitionFailed. The
        // refactor did not swallow or change the legacy failure behaviour.
        string databaseName = NewDatabaseName();
        using FamiliesDbContext arrangeContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(arrangeContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        Booking booking = await CheckoutAsync(arrangeContext, family, elderly, UtcNow.AddMinutes(-30), DistantStartUtc);
        booking = await PayAsync(arrangeContext, booking, paymob, UtcNow.AddMinutes(-10), 901600001);

        using FamiliesDbContext handlerContext = CreateDbContext(databaseName);
        var seam = new SaveSeamContext(handlerContext, UnrelatedPersistenceFailure());

        Result result = await DeclineWithAsync(
            seam,
            new BookingCancellationFactRecorder(handlerContext),
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            "conflict",
            UtcNow);

        AssertFailedWith(result, "Bookings.TransitionFailed");

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        Assert.Equal(BookingStatus.PendingCaregiverApproval, persisted.Status);
        Assert.Null(persisted.CancelledOnUtc);
        Assert.Null(persisted.CancellationReason);
        await AssertNoFactsAsync(store);

        // The refund was attempted before the single save (the merged order); nothing about that call
        // is rolled back by the failed save.
        Assert.Single(paymob.RefundCalls);
    }

    [Fact]
    public async Task B7_DeclineWithAnEmptyActorUserId_IsRefusedByTheFactConstructor_AsTheLegacyTransitionFailed()
    {
        // UserId.Empty as the actor: the fact constructor refuses it ("Cancelling user ID is
        // required."). This is the deliberate subject of the test — the hard rule bars using
        // UserId.Empty as a "negative" id in any other case, because validators reject it earlier and
        // asserting on it would pin the wrong thing. From the HTTP endpoint this path is unreachable:
        // CaregiverBookingsController always passes the authenticated user id. It surfaces as the
        // legacy "Bookings.TransitionFailed" (not Bookings.Domain.InvalidOperation) because the fact
        // is built inside the inner try, whose generic catch maps every non-unique-violation failure
        // to that code.
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();

        Booking booking = await CheckoutAsync(dbContext, family, elderly, UtcNow.AddMinutes(-30), DistantStartUtc);
        booking = await PayAsync(dbContext, booking, paymob, UtcNow.AddMinutes(-10), 901700001);

        Result result = await DeclineAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            UserId.Empty,
            booking.Id,
            "conflict",
            UtcNow);

        AssertFailedWith(result, "Bookings.TransitionFailed");
        Assert.Equal("Cancelling user ID is required.", result.Error.Message);

        // The booking was NOT declined and no fact was written. The in-memory aggregate was mutated
        // by the decline before the fact refused it, but nothing was ever saved: the store still
        // holds the paid, undeclined booking.
        using FamiliesDbContext store = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        Assert.Equal(BookingStatus.PendingCaregiverApproval, persisted.Status);
        Assert.Null(persisted.CancelledOnUtc);
        Assert.Null(persisted.CancellationReason);
        await AssertNoFactsAsync(store);

        // The fact never even reached the change tracker: Create threw before the recorder ran.
        Assert.Empty(dbContext.ChangeTracker.Entries<BookingCancellationFact>());
    }

    [Fact]
    public async Task B8_LegacyDeclineBehaviors_RemainIntact_NotFoundMessages_AndMarkRefundedOnlyOnGatewaySuccess()
    {
        // Three legacy behaviours this slice must not have touched:
        //  1. "Booking not found for this caregiver." for an unknown booking id AND for a booking that
        //     belongs to a different caregiver (same message on purpose — no existence leak);
        //  2. the refund is attempted exactly once for the succeeded transaction;
        //  3. MarkRefunded only on gateway success: a refused refund leaves the booking
        //     DeclinedByCaregiver — never Refunded — while the reject fact is still recorded.
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient { RefundSucceeds = false };
        UserId caregiverUserId = UserId.New();

        Booking booking = await CheckoutAsync(dbContext, family, elderly, UtcNow.AddMinutes(-30), DistantStartUtc);
        booking = await PayAsync(dbContext, booking, paymob, UtcNow.AddMinutes(-10), 900800001);
        PaymentTransaction capture = Assert.Single(booking.PaymentTransactions);

        // (2)+(3): the refund is attempted once for the succeeded transaction; the gateway refuses;
        // the decline itself still succeeds and the booking stays DeclinedByCaregiver.
        Result declined = await DeclineAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            booking.Id,
            "conflict",
            UtcNow);

        Assert.True(declined.IsSuccess);

        Assert.Single(paymob.RefundCalls);
        Assert.Equal(capture.PaymobTransactionId, paymob.RefundCalls[0].PaymobTransactionId);
        Assert.Equal(capture.Amount, paymob.RefundCalls[0].Amount);

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Booking persisted = await PersistedBookingAsync(store, booking.Id);

        Assert.Equal(BookingStatus.DeclinedByCaregiver, persisted.Status);
        Assert.Equal("conflict", persisted.CancellationReason);
        Assert.Equal(UtcNow, persisted.CancelledOnUtc);
        Assert.Null(persisted.PaymobRefundTransactionId);
        Assert.Null(persisted.RefundedOnUtc);
        Assert.Equal(PaymentTransactionStatus.Succeeded, Assert.Single(persisted.PaymentTransactions).Status);

        BookingCancellationFact fact = await SingleFactAsync(store);

        AssertFactSpine(fact, booking.Id, caregiverUserId, UtcNow);
        Assert.Equal(BookingStatus.PendingCaregiverApproval, fact.StatusAtCancellation);
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, fact.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.CaregiverRejectionBeforeAcceptance, fact.RefundDecisionReason);

        // (1a): an unknown booking id.
        Result unknown = await DeclineAsync(
            dbContext,
            paymob,
            booking.CaregiverId,
            caregiverUserId,
            BookingId.New(),
            "gone",
            UtcNow.AddMinutes(1));

        AssertFailedWith(unknown, "Bookings.NotFound");
        Assert.Equal("Booking not found for this caregiver.", unknown.Error.Message);

        // (1b): an existing booking that belongs to a different caregiver.
        Result foreign = await DeclineAsync(
            dbContext,
            paymob,
            CaregiverId.New(),
            caregiverUserId,
            booking.Id,
            "not mine",
            UtcNow.AddMinutes(2));

        AssertFailedWith(foreign, "Bookings.NotFound");
        Assert.Equal("Booking not found for this caregiver.", foreign.Error.Message);

        // The two lookups moved no money and wrote no further history.
        Assert.Single(paymob.RefundCalls);
        Assert.Single(await store.BookingCancellationFacts.ToListAsync());
    }

    [Fact]
    public async Task B9_OneBookingNeverCarriesTwoFacts_AcrossCancelThenDeclineOrDeclineThenCancel()
    {
        // A second cancellation act on an already-ended booking is refused by the policy on the
        // untouched aggregate, so the one-fact-per-booking invariant holds in both directions.
        string databaseName = NewDatabaseName();
        using FamiliesDbContext dbContext = CreateDbContext(databaseName);
        var (family, elderly) = SeedFamily(dbContext);
        var paymob = new RecordingPaymobClient();
        UserId caregiverUserId = UserId.New();

        // Direction 1: cancel first (a confirmed booking), decline second.
        Booking cancelledFirst = await ConfirmedBookingAsync(
            dbContext, family, elderly, paymob, UtcNow.AddMinutes(-20), DistantStartUtc, 900900001);

        Result firstCancel = await CancelAsync(
            dbContext,
            paymob,
            cancelledFirst.CaregiverId,
            caregiverUserId,
            cancelledFirst.Id,
            UtcNow,
            reason: "family emergency",
            reasonCategory: (int)BookingCancellationReasonCategory.Emergency);

        Assert.True(firstCancel.IsSuccess);

        Result secondDecline = await DeclineAsync(
            dbContext,
            paymob,
            cancelledFirst.CaregiverId,
            caregiverUserId,
            cancelledFirst.Id,
            "too late",
            UtcNow.AddMinutes(1));

        AssertFailedWith(secondDecline, "Bookings.Domain.InvalidOperation");

        // Direction 2: decline first (a paid booking awaiting approval), cancel second.
        Booking declinedFirst = await CheckoutAsync(dbContext, family, elderly, UtcNow.AddMinutes(-10), DistantStartUtc);
        declinedFirst = await PayAsync(dbContext, declinedFirst, paymob, UtcNow.AddMinutes(-5), 901000001);

        Result firstDecline = await DeclineAsync(
            dbContext,
            paymob,
            declinedFirst.CaregiverId,
            caregiverUserId,
            declinedFirst.Id,
            "conflict",
            UtcNow);

        Assert.True(firstDecline.IsSuccess);

        // No category on the second attempt: the booking is no longer Confirmed, so a supplied
        // category would take the pre-acceptance refusal branch instead of the policy.
        Result secondCancel = await CancelAsync(
            dbContext,
            paymob,
            declinedFirst.CaregiverId,
            caregiverUserId,
            declinedFirst.Id,
            UtcNow.AddMinutes(1),
            reason: null,
            reasonCategory: null);

        AssertFailedWith(secondCancel, "Bookings.Domain.InvalidOperation");

        using FamiliesDbContext store = CreateDbContext(databaseName);

        Assert.Single(await FactsForBookingAsync(store, cancelledFirst.Id));
        Assert.Single(await FactsForBookingAsync(store, declinedFirst.Id));

        // Exactly one provider call per successful act; the two refusals moved no money.
        Assert.Equal(2, paymob.RefundCalls.Count);
    }

    // =============================================================================================
    // Fixtures and doubles.
    // =============================================================================================

    private static string NewDatabaseName() => $"sanad-caregiver-decline-{Guid.NewGuid():N}";

    private static FamiliesDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new FamiliesDbContext(options);
    }

    private static (Family Family, Elderly Elderly) SeedFamily(FamiliesDbContext dbContext)
    {
        var family = Family.Create(UserId.New(), "Caregiver Decline Contract Family");

        var elderly = Elderly.Create(
            family.OwnerUserId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("مسن تجريبي"),
            FullName.Create("Elderly Test"),
            Gender.Male,
            DateOnly.FromDateTime(UtcNow.AddYears(-70)),
            DateOnly.FromDateTime(UtcNow));

        dbContext.Families.Add(family);
        dbContext.Elderlies.Add(elderly);
        dbContext.SaveChanges();

        return (family, elderly);
    }

    private static async Task<Booking> CheckoutAsync(
        FamiliesDbContext dbContext,
        Family family,
        Elderly elderly,
        DateTime createdAtUtc,
        DateTime scheduledStartUtc)
    {
        const decimal caregiverBaseFee = 500m;

        var handler = new CreateBookingCheckoutCommandHandler(
            dbContext,
            new FixedPricing(caregiverBaseFee));

        TimeOnly startTime = TimeOnly.FromDateTime(scheduledStartUtc);

        Result<BookingCheckoutResponse> result = await handler.Handle(
            new CreateBookingCheckoutCommand(
                family.OwnerUserId,
                elderly.Id,
                CaregiverId.New(),
                BookingShiftType.HomeVisit,
                DateOnly.FromDateTime(scheduledStartUtc),
                startTime,
                startTime.AddHours(2),
                "123 Nile St, Cairo",
                null,
                DateOnly.FromDateTime(createdAtUtc),
                createdAtUtc),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var bookingId = new BookingId(result.Value.BookingId);

        return await PersistedBookingAsync(dbContext, bookingId);
    }

    /// <summary>
    /// Settles the booking through the real payment application path (intent, then the Paymob
    /// confirmation webhook), so the aggregate carries a Succeeded transaction with a provider reference.
    /// </summary>
    private static async Task<Booking> PayAsync(
        FamiliesDbContext dbContext,
        Booking booking,
        RecordingPaymobClient paymob,
        DateTime paidAtUtc,
        long paymobTransactionId)
    {
        var intentHandler = new CreateBookingPaymentIntentCommandHandler(dbContext, paymob);

        Result<BookingPaymentIntentResponse> intent = await intentHandler.Handle(
            new CreateBookingPaymentIntentCommand(
                booking.Id,
                booking.CreatedByUserId,
                PaymentMethod.Card,
                new PaymobBillingData("Salma", "Hassan", "salma.hassan@example.com", "+201234567890"),
                paidAtUtc),
            CancellationToken.None);

        Assert.True(intent.IsSuccess);

        // The webhook compares the reported amount against the recorded intent, in cents.
        long amountCents = (long)decimal.Round(
            booking.PriceSnapshot.TotalPayableAmount * 100m, 0, MidpointRounding.ToEven);

        var confirmHandler = new ConfirmBookingPaymentCommandHandler(dbContext, paymob);

        Result<ConfirmBookingPaymentResponse> confirmation = await confirmHandler.Handle(
            new ConfirmBookingPaymentCommand(
                intent.Value.PaymobOrderId,
                paymobTransactionId,
                amountCents,
                Success: true,
                Pending: false,
                UtcNow: paidAtUtc),
            CancellationToken.None);

        Assert.True(confirmation.IsSuccess);
        Assert.Equal("Paid", confirmation.Value.Outcome);

        Booking paid = await PersistedBookingAsync(dbContext, booking.Id);
        PaymentTransaction transaction = Assert.Single(paid.PaymentTransactions);

        Assert.Equal(BookingStatus.PendingCaregiverApproval, paid.Status);
        Assert.Equal(PaymentTransactionStatus.Succeeded, transaction.Status);
        Assert.Equal(paymobTransactionId.ToString(), transaction.PaymobTransactionId);

        return paid;
    }

    /// <summary>
    /// Paid and accepted booking (Confirmed), reached through the checkout, webhook and caregiver-accept
    /// application paths.
    /// </summary>
    private static async Task<Booking> ConfirmedBookingAsync(
        FamiliesDbContext dbContext,
        Family family,
        Elderly elderly,
        RecordingPaymobClient paymob,
        DateTime acceptedAtUtc,
        DateTime scheduledStartUtc,
        long paymobTransactionId = 900500001)
    {
        Booking booking = await CheckoutAsync(
            dbContext, family, elderly, acceptedAtUtc.AddMinutes(-25), scheduledStartUtc);

        booking = await PayAsync(dbContext, booking, paymob, acceptedAtUtc.AddMinutes(-5), paymobTransactionId);

        var acceptHandler = new CaregiverAcceptBookingCommandHandler(dbContext);

        Result accepted = await acceptHandler.Handle(
            new CaregiverAcceptBookingCommand(booking.CaregiverId, booking.Id, acceptedAtUtc),
            CancellationToken.None);

        Assert.True(accepted.IsSuccess);

        Booking confirmed = await PersistedBookingAsync(dbContext, booking.Id);

        Assert.Equal(BookingStatus.Confirmed, confirmed.Status);
        Assert.Equal(acceptedAtUtc, confirmed.ConfirmedOnUtc);

        return confirmed;
    }

    private static Task<Result> DeclineAsync(
        FamiliesDbContext dbContext,
        RecordingPaymobClient paymob,
        CaregiverId caregiverId,
        UserId actorUserId,
        BookingId bookingId,
        string reason,
        DateTime utcNow) =>
        DeclineWithAsync(
            dbContext,
            new BookingCancellationFactRecorder(dbContext),
            paymob,
            caregiverId,
            actorUserId,
            bookingId,
            reason,
            utcNow);

    private static Task<Result> DeclineWithAsync(
        IFamiliesDbContext dbContext,
        IBookingCancellationFactRecorder cancellationFactRecorder,
        RecordingPaymobClient paymob,
        CaregiverId caregiverId,
        UserId actorUserId,
        BookingId bookingId,
        string reason,
        DateTime utcNow) =>
        new CaregiverDeclineBookingCommandHandler(dbContext, paymob, cancellationFactRecorder)
            .Handle(
                new CaregiverDeclineBookingCommand(caregiverId, actorUserId, bookingId, reason, utcNow),
                CancellationToken.None);

    private static Task<Result> CancelAsync(
        FamiliesDbContext dbContext,
        RecordingPaymobClient paymob,
        CaregiverId caregiverId,
        UserId actorUserId,
        BookingId bookingId,
        DateTime utcNow,
        string? reason = null,
        int? reasonCategory = null) =>
        new CaregiverCancelBookingCommandHandler(
                dbContext,
                paymob,
                new BookingCancellationFactRecorder(dbContext))
            .Handle(
                new CaregiverCancelBookingCommand(caregiverId, actorUserId, bookingId, reason, reasonCategory, utcNow),
                CancellationToken.None);

    private static async Task<Booking> PersistedBookingAsync(FamiliesDbContext dbContext, BookingId bookingId) =>
        await dbContext.Bookings.SingleAsync(b => b.Id == bookingId);

    private static async Task<BookingCancellationFact> SingleFactAsync(FamiliesDbContext dbContext)
    {
        List<BookingCancellationFact> facts = await dbContext.BookingCancellationFacts.ToListAsync();

        Assert.Single(facts);

        return facts[0];
    }

    private static async Task<List<BookingCancellationFact>> FactsForBookingAsync(
        FamiliesDbContext dbContext,
        BookingId bookingId) =>
        await dbContext.BookingCancellationFacts
            .Where(f => f.BookingId == bookingId)
            .ToListAsync();

    private static async Task AssertNoFactsAsync(FamiliesDbContext dbContext) =>
        Assert.Empty(await dbContext.BookingCancellationFacts.ToListAsync());

    private static void AssertFailedWith(Result result, string expectedErrorCode)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(expectedErrorCode, result.Error.Code);
    }

    /// <summary>
    /// The whole negative-first side effect contract: no fact row, the booking exactly as arranged,
    /// and the gateway never contacted by the handler under test.
    /// </summary>
    private static async Task AssertStoreUntouchedAsync(
        string databaseName,
        RecordingPaymobClient paymob,
        BookingId bookingId,
        BookingStatus expectedStatus)
    {
        using FamiliesDbContext store = CreateDbContext(databaseName);

        await AssertNoFactsAsync(store);

        Booking persisted = await PersistedBookingAsync(store, bookingId);

        Assert.Equal(expectedStatus, persisted.Status);
        Assert.Null(persisted.CancelledOnUtc);
        Assert.Null(persisted.CancellationReason);
        Assert.Null(persisted.PaymobRefundTransactionId);
        Assert.Null(persisted.RefundedOnUtc);
        Assert.Empty(paymob.RefundCalls);
    }

    /// <summary>
    /// Fields every caregiver rejection fact carries, whatever the details: caregiver side, reject
    /// action, the authorized actor, the caller's decline time, the current policy revision and NO
    /// caregiver incident flag (a pre-acceptance rejection is never an incident, by policy).
    /// </summary>
    private static void AssertFactSpine(
        BookingCancellationFact fact,
        BookingId bookingId,
        UserId actorUserId,
        DateTime cancelledOnUtc)
    {
        Assert.Equal(bookingId, fact.BookingId);
        Assert.Equal(actorUserId, fact.ActorUserId);
        Assert.Equal(BookingCancellationActorSide.Caregiver, fact.ActorSide);
        Assert.Equal(BookingCancellationAction.Reject, fact.Action);
        Assert.Equal(cancelledOnUtc, fact.CancelledOnUtc);
        Assert.Equal(BookingCancellationPolicy.CurrentPolicyVersion, fact.PolicyVersion);
        Assert.False(fact.IsCaregiverIncident);
    }

    /// <summary>
    /// What PostgreSQL reports for the one-fact-per-booking unique index, wrapped the way the provider
    /// surfaces it: a <see cref="DbUpdateException"/> whose inner chain carries the constraint name.
    /// </summary>
    private static DbUpdateException FactUniqueViolationFailure()
    {
        const string constraintName = "ux_booking_cancellation_facts_booking";

        var providerError = new InvalidOperationException(
            $"23505: duplicate key value violates unique constraint \"{constraintName}\"");

        return new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new Exception("See the inner exception for the provider error.", providerError));
    }

    private static DbUpdateException UnrelatedPersistenceFailure() =>
        new(
            "An error occurred while saving the entity changes.",
            new InvalidOperationException(
                "23503: insert or update on table \"booking_cancellation_facts\" violates foreign key "
                + "constraint \"fk_booking_cancellation_facts_bookings_booking_id\""));

    /// <summary>
    /// The persistence seam the handler depends on, with an optionally failing save. A subclass of
    /// <see cref="FamiliesDbContext"/> is not available — the production context is sealed — so the save
    /// is intercepted at the interface the handler actually consumes; reads are served by the real
    /// context, which is also the context the production recorder tracks the fact on.
    /// </summary>
    private sealed class SaveSeamContext(FamiliesDbContext inner, Exception? saveFailure = null) : IFamiliesDbContext
    {
        public DbSet<Family> Families => inner.Families;

        public DbSet<Elderly> Elderlies => inner.Elderlies;

        public DbSet<FamilyInvitation> Invitations => inner.Invitations;

        public DbSet<Booking> Bookings => inner.Bookings;

        public DbSet<AssessmentQuestion> AssessmentQuestions => inner.AssessmentQuestions;

        public DbSet<AssessmentTier> AssessmentTiers => inner.AssessmentTiers;

        public DbSet<CareAssessment> CareAssessments => inner.CareAssessments;

        public DbSet<Medication> Medications => inner.Medications;

        public DbSet<MedicationDoseLog> MedicationDoseLogs => inner.MedicationDoseLogs;

        public DbSet<ElderlyNote> ElderlyNotes => inner.ElderlyNotes;

        public DbSet<ElderlyActivityLog> ElderlyActivityLogs => inner.ElderlyActivityLogs;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            saveFailure is null
                ? inner.SaveChangesAsync(cancellationToken)
                : Task.FromException<int>(saveFailure);
    }

    private sealed class FixedPricing(decimal baseFee) : ICaregiverBookingPricing
    {
        public Task<Result<CaregiverBookingPrice>> GetBookingPriceAsync(
            CaregiverId caregiverId,
            BookingShiftType shiftType,
            TimeOnly startTime,
            TimeOnly endTime,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<CaregiverBookingPrice>.Success(
                new CaregiverBookingPrice(BookingCaregiverType.Medical, baseFee)));
    }

    private sealed record RefundCall(string PaymobTransactionId, decimal Amount);

    /// <summary>
    /// Payment gateway double: every refund attempt is recorded with its transaction reference and
    /// amount, and the outcome is configurable so a refused refund can be arranged.
    /// </summary>
    private sealed class RecordingPaymobClient : IPaymobClient
    {
        private readonly List<RefundCall> _refundCalls = [];

        public IReadOnlyList<RefundCall> RefundCalls => _refundCalls;

        public bool RefundSucceeds { get; init; } = true;

        public string RefundTransactionId { get; init; } = "test-refund-transaction";

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

            return Task.FromResult(RefundSucceeds
                ? Result<string?>.Success(RefundTransactionId)
                : Result<string?>.Failure(
                    new Error("Paymob.RefundFailed", "The gateway refused the refund.")));
        }
    }
}
