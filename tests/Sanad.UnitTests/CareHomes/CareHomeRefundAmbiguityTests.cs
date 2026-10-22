using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.CareHomes.Application.Bookings;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;

namespace Sanad.UnitTests.CareHomes;

/// <summary>
/// The unconfirmed-refund-outcome gate (Owner decision #17): an attempt whose
/// outcome the gateway could not confirm raises a durable ambiguity marker that
/// no retry can bypass; an operator's verified reconciliation is the only thing
/// that clears it — a verified movement completes the recorded award exactly
/// once, a verified non-payment returns the booking to the retryable failed
/// state (the only path back to a provider attempt) — and every reconciliation
/// is fenced by the attempt's stable identity, so a stale operator can never
/// release or reconcile an attempt other than the one they verified. A claimed
/// attempt with no recorded provider outcome is never reset on evidence or age;
/// its recovery awaits the queued provider attempt-liveness seam.
/// </summary>
public sealed class CareHomeRefundAmbiguityTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
    private static long _transactionId;

    private static CareHomeBooking NewPaidAccepted()
    {
        CareHomeBooking booking = CareHomeBooking.Create(
            new CareHomeId(Guid.NewGuid()), UserId.New(), FamilyId.New(), ElderlyId.New(), Guid.NewGuid(),
            new DateOnly(2026, 10, 10), 12000m, 600m, 630m, 1, "ع", "Elderly", 75, null, "Contact", null, null, null, Now);
        booking.MarkPaid(++_transactionId, Now.AddMinutes(1));
        booking.Accept(Now.AddMinutes(2));
        return booking;
    }

    private static CareHomeBooking NewRefundPending()
    {
        CareHomeBooking booking = NewPaidAccepted();
        booking.CancelByFamily(Now.AddMinutes(5));
        return booking;
    }

    [Fact]
    public void An_unconfirmed_outcome_blocks_every_further_claim_until_reconciled()
    {
        var booking = NewRefundPending();
        Assert.True(booking.TryClaimRefund(Now.AddMinutes(6)));

        // The claim carries its stable attempt identity, persisted before the
        // provider call.
        Guid attemptClaimId = booking.RefundAttemptClaimId!.Value;
        Assert.NotEqual(Guid.Empty, attemptClaimId);

        booking.MarkRefundOutcomeAmbiguous("Paymob.RefundOutcomeUnknown", Now.AddMinutes(7));

        Assert.True(booking.IsRefundOutcomeAmbiguous);
        Assert.Equal("Paymob.RefundOutcomeUnknown", booking.RefundAmbiguityReason);
        Assert.Equal(Now.AddMinutes(7), booking.RefundAmbiguityRaisedOnUtc);
        // The exact award the attempt was moving is on the record, taken from
        // the booking's own durable refund state.
        Assert.Equal(13230m, booking.RefundAmbiguityAwardAmount);
        Assert.Equal(booking.RefundAmount, booking.RefundAmbiguityAwardAmount);

        // The claim (and its attempt identity) stays — folded into the marker
        // as the durable record of the in-flight attempt — and the gate refuses
        // every further claim while the marker stands: no blind retry can
        // double-refund a customer whose first refund silently succeeded.
        Assert.NotNull(booking.RefundClaimedOnUtc);
        Assert.Equal(attemptClaimId, booking.RefundAttemptClaimId);
        Assert.False(booking.TryClaimRefund(Now.AddMinutes(8)));
        // No money state moved: the attempt is unconfirmed, not completed.
        Assert.Equal(CareHomeRefundStatus.Pending, booking.RefundStatus);
        Assert.Equal(CareHomeBookingStatus.RefundPending, booking.Status);
    }

    [Fact]
    public void The_marker_can_only_be_raised_against_a_persisted_in_flight_claim()
    {
        // Without the pre-call claim there is no proven in-flight attempt to
        // fold into an ambiguity.
        var noClaim = NewRefundPending();
        Assert.Throws<InvalidOperationException>(
            () => noClaim.MarkRefundOutcomeAmbiguous("Paymob.RefundOutcomeUnknown", Now.AddMinutes(6)));

        // And the marker cannot be raised twice.
        var alreadyAmbiguous = NewRefundPending();
        Assert.True(alreadyAmbiguous.TryClaimRefund(Now.AddMinutes(6)));
        alreadyAmbiguous.MarkRefundOutcomeAmbiguous("Paymob.RefundOutcomeUnknown", Now.AddMinutes(7));
        Assert.Throws<InvalidOperationException>(
            () => alreadyAmbiguous.MarkRefundOutcomeAmbiguous("Paymob.RefundOutcomeUnknown", Now.AddMinutes(8)));
    }

    [Fact]
    public void Verified_success_reconciles_the_recorded_award_exactly_once_and_clears_the_held_claim()
    {
        var moved = NewRefundPending();
        Assert.True(moved.TryClaimRefund(Now.AddMinutes(6)));
        Guid attemptClaimId = moved.RefundAttemptClaimId!.Value;
        moved.MarkRefundOutcomeAmbiguous("Paymob.RefundOutcomeUnknown", Now.AddMinutes(7));
        moved.ReconcileAmbiguousRefund(
            "operator-1: provider console shows refund 904", refundMoved: true, "RF-904",
            attemptClaimId, Now.AddMinutes(8));

        Assert.False(moved.IsRefundOutcomeAmbiguous);
        Assert.Equal(Now.AddMinutes(8), moved.RefundAmbiguityClearedOnUtc);
        Assert.Equal("operator-1: provider console shows refund 904", moved.RefundAmbiguityClearedBy);
        Assert.Equal(CareHomeRefundStatus.Completed, moved.RefundStatus);
        Assert.Equal(CareHomeBookingStatus.Refunded, moved.Status);
        Assert.Equal(CareHomeBookingPaymentStatus.Refunded, moved.PaymentStatus);
        // Exactly the recorded award, completed once — never re-decided or re-sent.
        Assert.Equal(13230m, moved.RefundAmount);
        Assert.Equal("RF-904", moved.RefundReference);
        Assert.NotNull(moved.RefundCompletedOnUtc);
        // The verified completion discharges the held claim.
        Assert.Null(moved.RefundClaimedOnUtc);
        Assert.Null(moved.RefundAttemptClaimId);
        // The latest attempt's record is retained as the audit trail (a
        // second ambiguous attempt would overwrite it; the boolean gate stays
        // the only authoritative open-question signal).
        Assert.Equal("Paymob.RefundOutcomeUnknown", moved.RefundAmbiguityReason);
        Assert.Equal(Now.AddMinutes(7), moved.RefundAmbiguityRaisedOnUtc);
        Assert.Equal(13230m, moved.RefundAmbiguityAwardAmount);

        // A second reconciliation has nothing left to reconcile.
        Assert.Throws<InvalidOperationException>(
            () => moved.ReconcileAmbiguousRefund(
                "operator-2", refundMoved: true, "RF-905", Guid.NewGuid(), Now.AddMinutes(9)));
    }

    [Fact]
    public void Verified_non_payment_returns_the_booking_to_the_retryable_failed_state()
    {
        var notMoved = NewRefundPending();
        Assert.True(notMoved.TryClaimRefund(Now.AddMinutes(6)));
        Guid attemptClaimId = notMoved.RefundAttemptClaimId!.Value;
        notMoved.MarkRefundOutcomeAmbiguous("Paymob.RefundOutcomeUnknown", Now.AddMinutes(7));
        notMoved.ReconcileAmbiguousRefund(
            "operator-2: provider confirms no refund was made", refundMoved: false, null,
            attemptClaimId, Now.AddMinutes(8));

        Assert.False(notMoved.IsRefundOutcomeAmbiguous);
        Assert.Equal(CareHomeRefundStatus.Failed, notMoved.RefundStatus);
        Assert.Equal(CareHomeBookingStatus.RefundPending, notMoved.Status);
        Assert.Equal(CareHomeBookingPaymentStatus.Paid, notMoved.PaymentStatus);
        // The claim and its attempt identity were released with the verified
        // non-payment; the latest attempt's ambiguity record is retained.
        Assert.Null(notMoved.RefundClaimedOnUtc);
        Assert.Null(notMoved.RefundAttemptClaimId);
        Assert.Equal(13230m, notMoved.RefundAmbiguityAwardAmount);
        // The existing retry route — the only path back to a provider attempt.
        Assert.True(notMoved.TryClaimRefund(Now.AddMinutes(9)));
    }

    [Fact]
    public void Reconciliation_requires_the_marker_the_operator_and_evidence_grade_outcomes()
    {
        var booking = NewRefundPending();
        Assert.True(booking.TryClaimRefund(Now.AddMinutes(6)));
        Guid attemptClaimId = booking.RefundAttemptClaimId!.Value;
        booking.MarkRefundOutcomeAmbiguous("Paymob.RefundOutcomeUnknown", Now.AddMinutes(7));

        // The operator must be named.
        Assert.Throws<ArgumentException>(
            () => booking.ReconcileAmbiguousRefund(" ", refundMoved: false, null, attemptClaimId, Now));
        // A claimed movement without the provider reference is not evidence.
        Assert.Throws<ArgumentException>(
            () => booking.ReconcileAmbiguousRefund("op", refundMoved: true, null, attemptClaimId, Now));
        // The attempt identity must match exactly what the operator verified.
        Assert.Throws<InvalidOperationException>(
            () => booking.ReconcileAmbiguousRefund("op", refundMoved: false, null, Guid.NewGuid(), Now));

        // And with no marker standing there is nothing to reconcile.
        var unmarked = NewRefundPending();
        Assert.Throws<InvalidOperationException>(
            () => unmarked.ReconcileAmbiguousRefund("op", refundMoved: false, null, Guid.NewGuid(), Now));
    }

    [Fact]
    public void A_stale_operator_can_never_release_or_reconcile_a_newer_attempt()
    {
        var booking = NewRefundPending();
        Assert.True(booking.TryClaimRefund(Now.AddMinutes(6)));
        Guid attemptOne = booking.RefundAttemptClaimId!.Value;
        booking.MarkRefundOutcomeAmbiguous("Paymob.RefundOutcomeUnknown", Now.AddMinutes(7));
        booking.ReconcileAmbiguousRefund(
            "operator-1: provider confirms no refund was made", refundMoved: false, null, attemptOne, Now.AddMinutes(8));

        // Attempt one concluded; the bounded retry claims attempt two with a
        // fresh attempt identity.
        Assert.Equal(CareHomeRefundStatus.Failed, booking.RefundStatus);
        Assert.True(booking.TryClaimRefund(Now.AddMinutes(9)));
        Guid attemptTwo = booking.RefundAttemptClaimId!.Value;
        Assert.NotEqual(attemptOne, attemptTwo);
        booking.MarkRefundOutcomeAmbiguous("Paymob.RefundOutcomeUnknown", Now.AddMinutes(10));

        // The stale operator's record still names attempt one: the
        // compare-and-swap refuses it, the new attempt's marker stands
        // untouched, and only the holder of the current identity proceeds.
        var stale = Assert.Throws<InvalidOperationException>(
            () => booking.ReconcileAmbiguousRefund(
                "operator-1 (stale card): provider shows nothing", refundMoved: false, null,
                attemptOne, Now.AddMinutes(11)));
        Assert.Equal("CareHomes.Bookings.RefundAttemptMismatch", stale.Message);
        Assert.True(booking.IsRefundOutcomeAmbiguous);
        Assert.Equal(attemptTwo, booking.RefundAttemptClaimId);
        booking.ReconcileAmbiguousRefund(
            "operator-2: provider confirms no refund was made", refundMoved: false, null,
            attemptTwo, Now.AddMinutes(12));
        Assert.Equal(CareHomeRefundStatus.Failed, booking.RefundStatus);
    }

    [Fact]
    public void An_interrupted_claim_stays_claimed_and_recovers_only_through_its_own_attempt_identity()
    {
        // Crash window: the claim committed before the provider call and no
        // outcome was ever recorded. A competing claim loses its fence.
        var booking = NewRefundPending();
        Assert.True(booking.TryClaimRefund(Now.AddMinutes(6)));
        Guid attemptClaimId = booking.RefundAttemptClaimId!.Value;
        Assert.False(booking.TryClaimRefund(Now.AddMinutes(7)));
        Assert.Equal(attemptClaimId, booking.RefundAttemptClaimId);

        // Neither a guessed identity nor a foreign one can recover the claim:
        // elapsed age is not evidence, and the operator surface refuses the
        // class outright; even the queued recovery seam must present the
        // exact attempt it verified.
        Assert.Throws<InvalidOperationException>(
            () => booking.ReconcileInterruptedRefundClaim("op: guessed", refundMoved: false, null, null, Now.AddMinutes(8)));
        Assert.Throws<InvalidOperationException>(
            () => booking.ReconcileInterruptedRefundClaim("op: foreign id", refundMoved: false, null, Guid.NewGuid(), Now.AddMinutes(8)));

        // Still claimed — no reset, no resend, no second claim.
        Assert.Equal(CareHomeRefundStatus.Pending, booking.RefundStatus);
        Assert.NotNull(booking.RefundClaimedOnUtc);
        Assert.False(booking.TryClaimRefund(Now.AddMinutes(8)));

        // Only the holder of the exact attempt identity may recover it once a
        // provider-side seam can prove the attempt is dead.
        booking.ReconcileInterruptedRefundClaim(
            "ops: provider confirms the attempt is dead", refundMoved: false, null, attemptClaimId, Now.AddMinutes(9));
        Assert.Equal(CareHomeRefundStatus.Failed, booking.RefundStatus);
        Assert.Null(booking.RefundClaimedOnUtc);
        Assert.Null(booking.RefundAttemptClaimId);
        Assert.True(booking.TryClaimRefund(Now.AddMinutes(10)));
    }

    [Fact]
    public void A_definitive_provider_refusal_releases_the_claim_into_a_retryable_failure()
    {
        // Paymob.RefundRejected is the provider's own 4xx refusal: the request
        // arrived and was explicitly refused, so nothing moved and no
        // ambiguity is possible.
        var booking = NewRefundPending();
        Assert.True(booking.TryClaimRefund(Now.AddMinutes(6)));
        booking.MarkRefundFailed("Paymob.RefundRejected", Now.AddMinutes(7));

        Assert.Equal(CareHomeRefundStatus.Failed, booking.RefundStatus);
        Assert.Equal("Paymob.RefundRejected", booking.RefundFailureReason);
        Assert.Null(booking.RefundClaimedOnUtc);
        Assert.Null(booking.RefundAttemptClaimId);
        Assert.False(booking.IsRefundOutcomeAmbiguous);
        // The bounded retry route stays available.
        Assert.True(booking.TryClaimRefund(Now.AddMinutes(8)));
    }

    [Fact]
    public void An_uncertain_outcome_is_never_automatically_retried_or_released()
    {
        var booking = NewRefundPending();
        Assert.True(booking.TryClaimRefund(Now.AddMinutes(6)));
        booking.MarkRefundOutcomeAmbiguous("Paymob.RefundCallNotConfirmed", Now.AddMinutes(7));

        // No retry, and no failure-release that would silence the marker: the
        // folded claim (and its attempt identity) stays until an operator
        // reconciles.
        Assert.False(booking.TryClaimRefund(Now.AddMinutes(8)));
        Assert.Throws<InvalidOperationException>(
            () => booking.MarkRefundFailed("Paymob.RefundOutcomeUnknown", Now.AddMinutes(8)));
        Assert.True(booking.IsRefundOutcomeAmbiguous);
        Assert.NotNull(booking.RefundClaimedOnUtc);
        Assert.NotNull(booking.RefundAttemptClaimId);
    }

    [Fact]
    public void Provider_failure_classes_disclose_what_may_release_a_claim()
    {
        // Only provider meanings proven definitive may release a claim:
        // a pre-send configuration failure sent nothing, and a 4xx refusal
        // arrived and was refused.
        Assert.Equal(
            CareHomeRefundProcessor.RefundFailureClass.DefinitiveNoMovement,
            CareHomeRefundProcessor.ClassifyFailure(new Error("Paymob.NotConfigured", "not configured")));
        Assert.Equal(
            CareHomeRefundProcessor.RefundFailureClass.DefinitiveNoMovement,
            CareHomeRefundProcessor.ClassifyFailure(new Error("Paymob.RefundRejected", "4xx refusal")));

        // Everything that is not proven definitive stays uncertain — the
        // unknown outcome itself, an unrecognised future code, and by
        // extension a cancelled or timed-out call (mapped by the processors to
        // the ambiguous marker, never to a retryable failure).
        Assert.Equal(
            CareHomeRefundProcessor.RefundFailureClass.Uncertain,
            CareHomeRefundProcessor.ClassifyFailure(new Error("Paymob.RefundOutcomeUnknown", "lost response")));
        Assert.Equal(
            CareHomeRefundProcessor.RefundFailureClass.Uncertain,
            CareHomeRefundProcessor.ClassifyFailure(new Error("Paymob.Timeout", "unrecognised code")));
    }

    [Fact]
    public void Reconciliation_evidence_is_required_and_bounded_before_any_mutation()
    {
        var actor = UserId.New();

        // Evidence is required.
        Result<string> required = CareHomeRefundReconciliationEvidence.Compose(actor, "   ");
        Assert.True(required.IsFailure);
        Assert.Equal("CareHomes.RefundReconciliation.EvidenceRequired", required.Error!.Code);

        // The composed form must fit the actual RefundAmbiguityClearedBy
        // column (maxlength 2000): the 34-character actor prefix plus the
        // evidence must never overflow it.
        Result<string> tooLong = CareHomeRefundReconciliationEvidence.Compose(actor, new string('x', 1967));
        Assert.True(tooLong.IsFailure);
        Assert.Equal("CareHomes.RefundReconciliation.EvidenceTooLong", tooLong.Error!.Code);
        Result<string> longestAllowed = CareHomeRefundReconciliationEvidence.Compose(actor, new string('x', 1966));
        Assert.True(longestAllowed.IsSuccess);

        // The composed form trims the evidence and prefixes the actor.
        Result<string> composed = CareHomeRefundReconciliationEvidence.Compose(
            actor, "  provider console shows refund 904  ");
        Assert.True(composed.IsSuccess);
        Assert.Equal($"{actor.Value:N}: provider console shows refund 904", composed.Value);
        Assert.True(composed.Value.Length <= CareHomeRefundReconciliationEvidence.MaxComposedLength);
    }

    [Fact]
    public void The_retry_route_arms_only_a_failed_refund_or_a_pending_unclaimed_send()
    {
        // Pending with no claim is the crash window between the begin-refund
        // save and the claim save: a claim always precedes a send, so nothing
        // was sent — the retry route may proceed (the send claims atomically).
        var booking = NewRefundPending();
        Assert.True(booking.IsRefundSendArmed);

        // A claimed attempt is never armed from the retry route — live,
        // crashed-unmarked, or folded into the ambiguity marker.
        Assert.True(booking.TryClaimRefund(Now.AddMinutes(6)));
        Assert.False(booking.IsRefundSendArmed);
        booking.MarkRefundOutcomeAmbiguous("Paymob.RefundOutcomeUnknown", Now.AddMinutes(7));
        Assert.False(booking.IsRefundSendArmed);

        // A verified non-payment returns the booking to Failed+unclaimed: the
        // bounded retry is armed again, and the send claims before sending.
        booking.ReconcileAmbiguousRefund(
            "operator: provider confirms no refund was made", refundMoved: false, null,
            booking.RefundAttemptClaimId!.Value, Now.AddMinutes(8));
        Assert.True(booking.IsRefundSendArmed);
        Assert.True(booking.TryClaimRefund(Now.AddMinutes(9)));
        booking.MarkRefundInitiated("RF-ARMED", Now.AddMinutes(10));

        // A re-armed failure is the second claim path: a previous definitive
        // refusal releases the claim into Failed, the retry claims before its
        // send, and the held claim (status still Failed) must fence the route.
        var failed = NewRefundPending();
        Assert.True(failed.TryClaimRefund(Now.AddMinutes(6)));
        failed.MarkRefundFailed("Paymob.RefundRejected", Now.AddMinutes(7));
        Assert.True(failed.IsRefundSendArmed);
        Assert.True(failed.TryClaimRefund(Now.AddMinutes(8)));
        Assert.Equal(CareHomeRefundStatus.Failed, failed.RefundStatus);
        Assert.False(failed.IsRefundSendArmed);

        // Concluded states are never armed.
        Assert.False(booking.IsRefundSendArmed);
    }

    [Fact]
    public async Task Reconcile_without_an_attempt_id_is_a_stable_validation_failure()
    {
        await using var db = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var handler = new ReconcileCareHomeRefundHandler(db);

        var result = await handler.Handle(new ReconcileCareHomeRefundCommand(
            UserId.New(), Guid.NewGuid(), Guid.Empty, RefundDidMove: false, null, "operator evidence", Now), default);

        // The compare-and-swap key has no safe default: an absent id is a
        // stable 400-class validation failure (mapped by the queued mapper
        // row), distinct from authentication — never a 401 masquerade.
        Assert.True(result.IsFailure);
        Assert.Equal("CareHomes.RefundReconciliation.InvalidAttemptClaim", result.Error.Code);
    }

    [Fact]
    public async Task Admin_detail_exposes_the_attempt_identity_and_ambiguity_state_the_reconcile_requires()
    {
        await using var db = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        CareHomeBooking booking = NewPaidAccepted();
        booking.CancelByFamily(Now.AddMinutes(5));
        Assert.True(booking.TryClaimRefund(Now.AddMinutes(6)));
        Guid attemptClaimId = booking.RefundAttemptClaimId!.Value;
        booking.MarkRefundOutcomeAmbiguous("Paymob.RefundOutcomeUnknown", Now.AddMinutes(7));
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var detail = await new GetAdminCareHomeBookingHandler(db)
            .Handle(new GetAdminCareHomeBookingQuery(booking.Id), default);

        Assert.True(detail.IsSuccess);
        // The operator reads the compare-and-swap key from THIS response.
        Assert.Equal(attemptClaimId, detail.Value.RefundAttemptClaimId);
        Assert.True(detail.Value.IsRefundOutcomeAmbiguous);
        // The marker folded the claim: nothing is independently in flight.
        Assert.False(detail.Value.IsRefundAttemptInFlight);
        Assert.Equal(13230m, detail.Value.RefundAmbiguityAwardAmount);
        Assert.Equal("Paymob.RefundOutcomeUnknown", detail.Value.RefundAmbiguityReason);
        Assert.Equal(Now.AddMinutes(7), detail.Value.RefundAmbiguityRaisedOnUtc);
        Assert.Null(detail.Value.RefundAmbiguityClearedOnUtc);
        Assert.Equal(booking.RefundPolicyVersion, detail.Value.RefundPolicyVersion);
    }

    [Fact]
    public async Task A_mapping_failure_after_reconciliation_commits_nothing()
    {
        await using var db = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        // v2 booking (so the response view reads the stored terms), then
        // corrupted out of band: the reconciliation itself is valid — verified
        // movement — only the response mapping can fail.
        CareHomeBooking booking = CareHomeBooking.Create(
            new CareHomeId(Guid.NewGuid()), UserId.New(), FamilyId.New(), ElderlyId.New(), Guid.NewGuid(),
            new DateOnly(2026, 10, 10), 12000m, 600m, 630m, 1, "ع", "Elderly", 75, null, "Contact", null, null, null, Now,
            policySnapshot: CareHomePolicySnapshot.From(CareHomeRefundPolicyVersion.ForNewBooking(true), null, null));
        booking.MarkPaid(44001, Now.AddMinutes(1));
        booking.Accept(Now.AddMinutes(2));
        booking.CancelByFamily(Now.AddMinutes(5));
        Assert.True(booking.TryClaimRefund(Now.AddMinutes(6)));
        Guid attemptClaimId = booking.RefundAttemptClaimId!.Value;
        booking.MarkRefundOutcomeAmbiguous("Paymob.RefundOutcomeUnknown", Now.AddMinutes(7));
        typeof(CareHomeBooking).GetProperty(nameof(CareHomeBooking.CancellationTermsSnapshotJson))!
            .SetValue(booking, "{corrupted");
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var result = await new ReconcileCareHomeRefundHandler(db).Handle(
            new ReconcileCareHomeRefundCommand(UserId.New(), booking.Id, attemptClaimId,
                RefundDidMove: true, "RF-904", "operator: provider console shows refund 904", Now.AddMinutes(8)), default);

        // The response was mapped BEFORE the committing save: the mapping
        // failure is reported with nothing persisted — never an error after
        // the reconciliation committed. The verified-movement notification is
        // only enqueued, never saved: the outbox is a new entity that reaches
        // the store on SaveChanges alone, so an empty outbox distinguishes
        // "never committed" from "committed and then failed" (the in-memory
        // store aliases tracked booking instances, so the booking itself is
        // not a discriminating read).
        Assert.True(result.IsFailure);
        Assert.Equal("CareHomes.Bookings.UnreadableSnapshot", result.Error.Code);
        Assert.Equal(0, await db.BookingNotificationOutbox.CountAsync(o => o.BookingId == booking.Id));
        var persisted = await db.Bookings.AsNoTracking().SingleAsync(x => x.Id == booking.Id);
        Assert.NotNull(persisted.RefundClaimedOnUtc);
        Assert.Equal(Now.AddMinutes(7), persisted.RefundAmbiguityRaisedOnUtc);
        Assert.Null(persisted.RefundAmbiguityClearedOnUtc);
        Assert.Equal("RF-904", booking.RefundReference);
    }

    [Fact]
    public async Task A_committed_reconciliation_reports_exactly_the_committed_state()
    {
        await using var db = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        CareHomeBooking booking = NewRefundPending();
        Assert.True(booking.TryClaimRefund(Now.AddMinutes(6)));
        Guid attemptClaimId = booking.RefundAttemptClaimId!.Value;
        booking.MarkRefundOutcomeAmbiguous("Paymob.RefundOutcomeUnknown", Now.AddMinutes(7));
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var result = await new ReconcileCareHomeRefundHandler(db).Handle(
            new ReconcileCareHomeRefundCommand(UserId.New(), booking.Id, attemptClaimId,
                RefundDidMove: false, null, "operator: provider confirms no refund", Now.AddMinutes(8)), default);

        // The returned body is exactly the committed state; a client that
        // doubts it re-reads the same booking and sees the same outcome.
        Assert.True(result.IsSuccess);
        Assert.Equal(CareHomeRefundStatus.Failed, result.Value.RefundStatus);
        Assert.Equal(CareHomeBookingStatus.RefundPending, result.Value.Status);
        var persisted = await db.Bookings.AsNoTracking().SingleAsync(x => x.Id == booking.Id);
        Assert.False(persisted.IsRefundOutcomeAmbiguous);
        Assert.Null(persisted.RefundClaimedOnUtc);
        Assert.Null(persisted.RefundAttemptClaimId);
        Assert.Equal(CareHomeRefundStatus.Failed, persisted.RefundStatus);
    }
}

