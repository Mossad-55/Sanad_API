using System.Globalization;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Families.Domain.Bookings;
using Xunit;

namespace Sanad.UnitTests.Families;

/// <summary>
/// Policy matrix for <see cref="BookingCancellationPolicy.Decide"/>: entitlement, reason and flags
/// come only from booking state, actor side, action and elapsed time measured from the supplied
/// acceptance time; capture evidence is a cross-check that fails closed instead of answering for it.
/// All timestamps are fixed UTC constants — the tests never read a clock and never sleep.
/// </summary>
public sealed class BookingCancellationPolicyTests
{
    private static readonly DateTime AcceptanceOnUtc =
        new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime CancelledAtAcceptance = AcceptanceOnUtc;

    /// <summary>Elapsed time as a tick-exact string, so boundaries are not rounded by seconds.</summary>
    private static DateTime CancelAfter(string elapsed) =>
        AcceptanceOnUtc + TimeSpan.Parse(elapsed, CultureInfo.InvariantCulture);

    /// <summary>The reason an accepted booking requires: a known category plus a bounded note.</summary>
    private static BookingCancellationFeedback AcceptedReason() =>
        BookingCancellationFeedback.Create(BookingCancellationReasonCategory.Emergency, "family emergency");

    private static BookingCancellationFeedback? NoteOnlyReason(string note) =>
        BookingCancellationFeedback.CreateOptionalNote(note);

    private static BookingCancellationPolicyInput FamilyCancellation(
        BookingStatus status,
        DateTime? confirmedOnUtc,
        DateTime? startedOnUtc,
        DateTime cancelledOnUtc,
        BookingCaptureEvidence captureEvidence,
        BookingCancellationFeedback? feedback = null) =>
        BookingCancellationPolicyInput.ForFamilyCancellation(
            status, confirmedOnUtc, startedOnUtc, cancelledOnUtc, captureEvidence, feedback);

    [Fact]
    public void PublishedPolicyConstants_MatchTheApprovedRule()
    {
        // The boundary tests below pin literal tick-exact times; these constants are what those
        // literals are standing in for.
        Assert.Equal(1, BookingCancellationPolicy.CurrentPolicyVersion);
        Assert.Equal(TimeSpan.FromMinutes(60), BookingCancellationPolicy.FamilyCancellationGraceWindow);
    }

    // ---------------------------------------------------------------------------------------------
    // PendingPayment: genuinely unpaid. Nothing was captured, so nothing is due.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void PendingPayment_FamilyCancellationWithoutCaptureEvidence_RefundsNothing()
    {
        BookingCancellationPolicyInput input = FamilyCancellation(
            BookingStatus.PendingPayment,
            confirmedOnUtc: null,
            startedOnUtc: null,
            CancelledAtAcceptance,
            BookingCaptureEvidence.NotCaptured);

        BookingCancellationDecision decision = BookingCancellationPolicy.Decide(input);

        Assert.Equal(BookingRefundEntitlement.NoRefundDue, decision.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.NothingCaptured, decision.RefundDecisionReason);
        Assert.False(decision.IsCaregiverIncident);
        Assert.False(decision.IsReasonFeedbackRequired);
        Assert.Null(decision.Feedback);
        Assert.Null(decision.ConfirmedOnUtcUsed);
        Assert.Equal(BookingStatus.PendingPayment, decision.StatusAtCancellation);
        Assert.Equal(BookingCancellationActorSide.Family, decision.ActorSide);
        Assert.Equal(BookingCancellationAction.Cancel, decision.Action);
        Assert.Equal(CancelledAtAcceptance, decision.CancelledOnUtc);
        Assert.Equal(1, decision.PolicyVersion);
    }

    [Fact]
    public void PendingPayment_FamilyCancellation_KeepsOptionalNoteWithoutFabricatingCategory()
    {
        BookingCancellationFeedback? note = NoteOnlyReason("  changed my mind  ");
        Assert.NotNull(note);

        BookingCancellationDecision decision = BookingCancellationPolicy.Decide(
            FamilyCancellation(
                BookingStatus.PendingPayment,
                null,
                null,
                CancelledAtAcceptance,
                BookingCaptureEvidence.NotCaptured,
                note));

        Assert.Equal(BookingRefundEntitlement.NoRefundDue, decision.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.NothingCaptured, decision.RefundDecisionReason);
        Assert.False(decision.IsReasonFeedbackRequired);
        Assert.False(decision.IsCaregiverIncident);

        // The exact validated reason travels on the decision; no category is invented for it.
        Assert.Same(note, decision.Feedback);
        Assert.Null(decision.Feedback!.Category);
        Assert.Equal("changed my mind", decision.Feedback.Note);
    }

    // ---------------------------------------------------------------------------------------------
    // PendingCaregiverApproval: paid but not yet accepted. Both sides get the full captured amount.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void PendingCaregiverApproval_FamilyCancellation_RefundsFullCapturedAmountWithoutFeedback()
    {
        BookingCancellationPolicyInput input = BookingCancellationPolicyInput.ForFamilyCancellation(
            BookingStatus.PendingCaregiverApproval,
            confirmedOnUtc: null,
            startedOnUtc: null,
            CancelledAtAcceptance,
            BookingCaptureEvidence.Captured);

        BookingCancellationDecision decision = BookingCancellationPolicy.Decide(input);

        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, decision.RefundEntitlement);
        Assert.Equal(
            BookingRefundDecisionReason.FamilyCancellationBeforeAcceptance,
            decision.RefundDecisionReason);
        Assert.False(decision.IsCaregiverIncident);
        Assert.False(decision.IsReasonFeedbackRequired);
        Assert.Null(decision.Feedback);
        Assert.Null(decision.ConfirmedOnUtcUsed);
    }

    [Fact]
    public void PendingCaregiverApproval_CaregiverRejection_RefundsFullCapturedAmountWithItsOwnReason()
    {
        BookingCancellationPolicyInput input = BookingCancellationPolicyInput.ForCaregiverRejection(
            BookingStatus.PendingCaregiverApproval,
            CancelledAtAcceptance,
            BookingCaptureEvidence.Captured);

        BookingCancellationDecision decision = BookingCancellationPolicy.Decide(input);

        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, decision.RefundEntitlement);
        Assert.Equal(
            BookingRefundDecisionReason.CaregiverRejectionBeforeAcceptance,
            decision.RefundDecisionReason);
        Assert.Equal(BookingCancellationAction.Reject, decision.Action);
        Assert.Equal(BookingCancellationActorSide.Caregiver, decision.ActorSide);
        Assert.False(decision.IsCaregiverIncident);
        Assert.False(decision.IsReasonFeedbackRequired);
    }

    [Theory]
    [InlineData(BookingCancellationActorSide.Family)]
    [InlineData(BookingCancellationActorSide.Caregiver)]
    public void PendingCaregiverApproval_ANoteOnlyReasonIsPreservedAsOptional(
        BookingCancellationActorSide actorSide)
    {
        BookingCancellationFeedback? note = NoteOnlyReason("no longer needed");
        DateTime cancelledOnUtc = CancelAfter("00:05:00");

        BookingCancellationPolicyInput input = actorSide == BookingCancellationActorSide.Family
            ? BookingCancellationPolicyInput.ForFamilyCancellation(
                BookingStatus.PendingCaregiverApproval, null, null, cancelledOnUtc,
                BookingCaptureEvidence.Captured, note)
            : BookingCancellationPolicyInput.ForCaregiverRejection(
                BookingStatus.PendingCaregiverApproval, cancelledOnUtc,
                BookingCaptureEvidence.Captured, note);

        BookingCancellationDecision decision = BookingCancellationPolicy.Decide(input);

        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, decision.RefundEntitlement);
        Assert.False(decision.IsReasonFeedbackRequired);
        Assert.False(decision.IsCaregiverIncident);
        Assert.Same(note, decision.Feedback);
        Assert.Null(decision.Feedback!.Category);
        Assert.Equal("no longer needed", decision.Feedback.Note);
    }

    // ---------------------------------------------------------------------------------------------
    // Confirmed, family: full refund strictly inside the 60 minute window, denial from exactly 60m.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("00:00:00", BookingRefundEntitlement.FullCapturedRefund,
        BookingRefundDecisionReason.FamilyCancellationWithinGraceWindow)]
    [InlineData("00:59:59", BookingRefundEntitlement.FullCapturedRefund,
        BookingRefundDecisionReason.FamilyCancellationWithinGraceWindow)]
    // One tick before the window closes is still inside it.
    [InlineData("00:59:59.9999999", BookingRefundEntitlement.FullCapturedRefund,
        BookingRefundDecisionReason.FamilyCancellationWithinGraceWindow)]
    // Exactly 60 minutes closes the window.
    [InlineData("01:00:00", BookingRefundEntitlement.NoRefundDue,
        BookingRefundDecisionReason.FamilyCancellationOutsideGraceWindow)]
    // One tick after 60 minutes.
    [InlineData("01:00:00.0000001", BookingRefundEntitlement.NoRefundDue,
        BookingRefundDecisionReason.FamilyCancellationOutsideGraceWindow)]
    [InlineData("01:10:00", BookingRefundEntitlement.NoRefundDue,
        BookingRefundDecisionReason.FamilyCancellationOutsideGraceWindow)]
    public void Confirmed_FamilyCancellation_BoundaryIsMeasuredFromTheAcceptanceTime(
        string elapsed,
        BookingRefundEntitlement expectedEntitlement,
        BookingRefundDecisionReason expectedReason)
    {
        DateTime cancelledOnUtc = CancelAfter(elapsed);

        BookingCancellationDecision decision = BookingCancellationPolicy.Decide(
            FamilyCancellation(
                BookingStatus.Confirmed,
                AcceptanceOnUtc,
                startedOnUtc: null,
                cancelledOnUtc,
                BookingCaptureEvidence.Captured,
                AcceptedReason()));

        Assert.Equal(expectedEntitlement, decision.RefundEntitlement);
        Assert.Equal(expectedReason, decision.RefundDecisionReason);
        Assert.False(decision.IsCaregiverIncident);
        Assert.True(decision.IsReasonFeedbackRequired);
        Assert.Equal(AcceptanceOnUtc, decision.ConfirmedOnUtcUsed);
        Assert.Equal(cancelledOnUtc, decision.CancelledOnUtc);
        Assert.Equal(1, decision.PolicyVersion);
    }

    [Fact]
    public void Confirmed_FamilyCancellation_WindowIgnoresCreationAndScheduledTimes()
    {
        // Same elapsed time, a whole year later: the entitlement is unchanged and the exact
        // timestamps used are the ones recorded on the decision. The input carries no creation or
        // scheduled-slot field at all, so neither can influence the boundary.
        DateTime shiftedAcceptance = AcceptanceOnUtc.AddYears(1);
        DateTime shiftedCancellation = CancelAfter("00:10:00").AddYears(1);

        BookingCancellationDecision original = BookingCancellationPolicy.Decide(
            FamilyCancellation(
                BookingStatus.Confirmed, AcceptanceOnUtc, null, CancelAfter("00:10:00"),
                BookingCaptureEvidence.Captured, AcceptedReason()));
        BookingCancellationDecision shifted = BookingCancellationPolicy.Decide(
            FamilyCancellation(
                BookingStatus.Confirmed, shiftedAcceptance, null, shiftedCancellation,
                BookingCaptureEvidence.Captured, AcceptedReason()));

        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, original.RefundEntitlement);
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, shifted.RefundEntitlement);
        Assert.Equal(
            original.RefundDecisionReason,
            shifted.RefundDecisionReason);
        Assert.Equal(shiftedAcceptance, shifted.ConfirmedOnUtcUsed);
        Assert.Equal(shiftedCancellation, shifted.CancelledOnUtc);
    }

    [Fact]
    public void Confirmed_CapturedEvidenceAtExactlySixtyMinutes_KeepsPolicyDenialReason()
    {
        // A paid booking whose capture evidence is present must still be answered by policy: an
        // expired grace window is a denial, never a "nothing was captured" report.
        BookingCancellationDecision decision = BookingCancellationPolicy.Decide(
            FamilyCancellation(
                BookingStatus.Confirmed,
                AcceptanceOnUtc,
                null,
                CancelAfter("01:00:00"),
                BookingCaptureEvidence.Captured,
                AcceptedReason()));

        Assert.Equal(BookingRefundEntitlement.NoRefundDue, decision.RefundEntitlement);
        Assert.Equal(
            BookingRefundDecisionReason.FamilyCancellationOutsideGraceWindow,
            decision.RefundDecisionReason);
        Assert.NotEqual(BookingRefundDecisionReason.NothingCaptured, decision.RefundDecisionReason);
    }

    // ---------------------------------------------------------------------------------------------
    // Confirmed, caregiver: full refund plus the incident review flag, at any elapsed time.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Confirmed_CaregiverCancellation_LongAfterAcceptance_RefundsInFullAndFlagsIncident()
    {
        BookingCancellationFeedback reason = BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.MedicalIssues,
            "caregiver became unavailable");
        DateTime cancelledOnUtc = CancelAfter("07:30:00");

        BookingCancellationDecision decision = BookingCancellationPolicy.Decide(
            BookingCancellationPolicyInput.ForCaregiverCancellation(
                BookingStatus.Confirmed,
                AcceptanceOnUtc,
                startedOnUtc: null,
                cancelledOnUtc,
                BookingCaptureEvidence.Captured,
                reason));

        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, decision.RefundEntitlement);
        Assert.Equal(
            BookingRefundDecisionReason.CaregiverCancellationAfterAcceptance,
            decision.RefundDecisionReason);
        Assert.True(decision.IsCaregiverIncident);
        Assert.True(decision.IsReasonFeedbackRequired);
        Assert.Same(reason, decision.Feedback);
        Assert.Equal(AcceptanceOnUtc, decision.ConfirmedOnUtcUsed);
        Assert.Equal(cancelledOnUtc, decision.CancelledOnUtc);

        // The family grace window must not be reported for a caregiver cancellation.
        Assert.NotEqual(
            BookingRefundDecisionReason.FamilyCancellationOutsideGraceWindow,
            decision.RefundDecisionReason);
    }

    [Fact]
    public void Confirmed_CaregiverCancellation_ImmediatelyAfterAcceptance_StillUsesCaregiverRule()
    {
        BookingCancellationDecision decision = BookingCancellationPolicy.Decide(
            BookingCancellationPolicyInput.ForCaregiverCancellation(
                BookingStatus.Confirmed,
                AcceptanceOnUtc,
                null,
                AcceptanceOnUtc,
                BookingCaptureEvidence.Captured,
                AcceptedReason()));

        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, decision.RefundEntitlement);
        Assert.Equal(
            BookingRefundDecisionReason.CaregiverCancellationAfterAcceptance,
            decision.RefundDecisionReason);
        Assert.True(decision.IsCaregiverIncident);
    }

    [Fact]
    public void Confirmed_CaregiverIncidentFlagAndEntitlementDifferFromFamilyOnSameFacts()
    {
        DateTime cancelledOnUtc = CancelAfter("00:10:00");

        BookingCancellationDecision family = BookingCancellationPolicy.Decide(
            FamilyCancellation(
                BookingStatus.Confirmed, AcceptanceOnUtc, null, cancelledOnUtc,
                BookingCaptureEvidence.Captured, AcceptedReason()));
        BookingCancellationDecision caregiver = BookingCancellationPolicy.Decide(
            BookingCancellationPolicyInput.ForCaregiverCancellation(
                BookingStatus.Confirmed, AcceptanceOnUtc, null, cancelledOnUtc,
                BookingCaptureEvidence.Captured, AcceptedReason()));

        Assert.False(family.IsCaregiverIncident);
        Assert.True(caregiver.IsCaregiverIncident);
        Assert.NotEqual(family, caregiver);
    }

    [Theory]
    [InlineData(BookingCancellationActorSide.Family)]
    [InlineData(BookingCancellationActorSide.Caregiver)]
    public void Confirmed_CancellationWithoutAnyReason_IsRefused(BookingCancellationActorSide actorSide)
    {
        BookingCancellationPolicyInput input = actorSide == BookingCancellationActorSide.Family
            ? FamilyCancellation(
                BookingStatus.Confirmed, AcceptanceOnUtc, null, CancelAfter("00:10:00"),
                BookingCaptureEvidence.Captured)
            : BookingCancellationPolicyInput.ForCaregiverCancellation(
                BookingStatus.Confirmed, AcceptanceOnUtc, null, CancelAfter("00:10:00"),
                BookingCaptureEvidence.Captured);

        DomainException error = Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(input));

        Assert.Contains("requires a reason category", error.Message);
    }

    // ---------------------------------------------------------------------------------------------
    // Illegal actor / action combinations.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(BookingStatus.PendingCaregiverApproval, BookingCancellationActorSide.Family,
        BookingCancellationAction.Reject, BookingCaptureEvidence.Captured)]
    [InlineData(BookingStatus.PendingPayment, BookingCancellationActorSide.Caregiver,
        BookingCancellationAction.Reject, BookingCaptureEvidence.NotCaptured)]
    [InlineData(BookingStatus.PendingPayment, BookingCancellationActorSide.Caregiver,
        BookingCancellationAction.Cancel, BookingCaptureEvidence.NotCaptured)]
    [InlineData(BookingStatus.PendingCaregiverApproval, BookingCancellationActorSide.Caregiver,
        BookingCancellationAction.Cancel, BookingCaptureEvidence.Captured)]
    [InlineData(BookingStatus.Confirmed, BookingCancellationActorSide.Caregiver,
        BookingCancellationAction.Reject, BookingCaptureEvidence.Captured)]
    public void IllegalActorActionCombinations_AreRefusedRegardlessOfOtherFacts(
        BookingStatus status,
        BookingCancellationActorSide actorSide,
        BookingCancellationAction action,
        BookingCaptureEvidence captureEvidence)
    {
        // Capture evidence and timestamps are supplied consistently with the status, so the only
        // violated rule is the actor / action combination itself.
        BookingCancellationPolicyInput input = new(
            status,
            actorSide,
            action,
            status == BookingStatus.Confirmed ? AcceptanceOnUtc : null,
            startedOnUtc: null,
            CancelledAtAcceptance,
            captureEvidence,
            status == BookingStatus.Confirmed ? AcceptedReason() : null);

        Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(input));
    }

    // ---------------------------------------------------------------------------------------------
    // States the policy must refuse to cancel.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(BookingStatus.InProgress)]
    [InlineData(BookingStatus.Completed)]
    [InlineData(BookingStatus.CancelledByFamily)]
    [InlineData(BookingStatus.CancelledByCaregiver)]
    [InlineData(BookingStatus.DeclinedByCaregiver)]
    [InlineData(BookingStatus.Expired)]
    [InlineData(BookingStatus.Refunded)]
    public void EndedOrStartedStates_FamilyCancellationIsRefused(BookingStatus status)
    {
        BookingCancellationPolicyInput input = FamilyCancellation(
            status,
            confirmedOnUtc: null,
            startedOnUtc: null,
            CancelledAtAcceptance,
            BookingCaptureEvidence.Captured,
            AcceptedReason());

        Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(input));
    }

    [Fact]
    public void CompletedState_CaregiverCancellationIsRefused()
    {
        BookingCancellationPolicyInput input = BookingCancellationPolicyInput.ForCaregiverCancellation(
            BookingStatus.Completed,
            null,
            null,
            CancelledAtAcceptance,
            BookingCaptureEvidence.Captured,
            AcceptedReason());

        Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(input));
    }

    // ---------------------------------------------------------------------------------------------
    // Undefined enum values, including the numeric default zero.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public void UndefinedEnumValues_AreRefusedIncludingTheNumericDefault(int raw)
    {
        BookingCancellationPolicyInput Valid() => FamilyCancellation(
            BookingStatus.Confirmed, AcceptanceOnUtc, null, CancelledAtAcceptance,
            BookingCaptureEvidence.Captured, AcceptedReason());

        Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(
            Valid() with { Status = (BookingStatus)raw }));
        Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(
            Valid() with { ActorSide = (BookingCancellationActorSide)raw }));
        Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(
            Valid() with { Action = (BookingCancellationAction)raw }));
        Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(
            Valid() with { CaptureEvidence = (BookingCaptureEvidence)raw }));
    }

    [Fact]
    public void NullInput_IsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => BookingCancellationPolicy.Decide(null!));
    }

    // ---------------------------------------------------------------------------------------------
    // Timestamp integrity: the policy refuses to guess on missing, default or non-UTC values.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void NonUtcCancellationTime_IsRefused(DateTimeKind kind)
    {
        DateTime notUtc = DateTime.SpecifyKind(AcceptanceOnUtc, kind);

        BookingCancellationPolicyInput input = FamilyCancellation(
            BookingStatus.Confirmed, AcceptanceOnUtc, null, notUtc,
            BookingCaptureEvidence.Captured, AcceptedReason());

        Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(input));
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void NonUtcAcceptanceTime_IsRefused(DateTimeKind kind)
    {
        DateTime notUtc = DateTime.SpecifyKind(AcceptanceOnUtc, kind);

        BookingCancellationPolicyInput input = FamilyCancellation(
            BookingStatus.Confirmed, notUtc, null, AcceptanceOnUtc,
            BookingCaptureEvidence.Captured, AcceptedReason());

        Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(input));
    }

    [Fact]
    public void DefaultTimestamps_AreRefusedAsMissing()
    {
        Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(
            FamilyCancellation(
                BookingStatus.Confirmed, AcceptanceOnUtc, null, default,
                BookingCaptureEvidence.Captured, AcceptedReason())));

        Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(
            FamilyCancellation(
                BookingStatus.Confirmed, default, null, CancelledAtAcceptance,
                BookingCaptureEvidence.Captured, AcceptedReason())));

        Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(
            FamilyCancellation(
                BookingStatus.Confirmed, AcceptanceOnUtc, default, CancelledAtAcceptance,
                BookingCaptureEvidence.Captured, AcceptedReason())));
    }

    [Fact]
    public void Confirmed_WithoutAcceptanceTime_IsRefused()
    {
        Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(
            FamilyCancellation(
                BookingStatus.Confirmed, confirmedOnUtc: null, null, CancelledAtAcceptance,
                BookingCaptureEvidence.Captured, AcceptedReason())));
    }

    [Fact]
    public void Confirmed_AcceptanceTimeInTheFutureAgainstCancellation_IsRefused()
    {
        Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(
            FamilyCancellation(
                BookingStatus.Confirmed,
                CancelAfter("00:10:00"),
                null,
                AcceptanceOnUtc,
                BookingCaptureEvidence.Captured,
                AcceptedReason())));
    }

    [Fact]
    public void Confirmed_WithRecordedVisitStart_IsRefused()
    {
        Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(
            FamilyCancellation(
                BookingStatus.Confirmed,
                AcceptanceOnUtc,
                startedOnUtc: AcceptanceOnUtc.AddMinutes(5),
                CancelAfter("00:10:00"),
                BookingCaptureEvidence.Captured,
                AcceptedReason())));
    }

    [Fact]
    public void UnacceptedStates_CannotCarryAcceptanceOrVisitStartFacts()
    {
        // PendingCaregiverApproval and PendingPayment were never accepted, so an acceptance or
        // visit-start timestamp on them is contradictory input rather than a refundable cancellation.
        BookingCancellationFeedback? note = NoteOnlyReason("note");

        Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(
            FamilyCancellation(
                BookingStatus.PendingCaregiverApproval, AcceptanceOnUtc, null, CancelledAtAcceptance,
                BookingCaptureEvidence.Captured, note)));

        Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(
            FamilyCancellation(
                BookingStatus.PendingCaregiverApproval, null, AcceptanceOnUtc, CancelledAtAcceptance,
                BookingCaptureEvidence.Captured, note)));

        Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(
            FamilyCancellation(
                BookingStatus.PendingPayment, null, CancelledAtAcceptance, CancelledAtAcceptance,
                BookingCaptureEvidence.NotCaptured, note)));
    }

    // ---------------------------------------------------------------------------------------------
    // Capture evidence: contradiction fails closed, it never becomes a refund answer.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(BookingStatus.PendingCaregiverApproval, BookingCaptureEvidence.NotCaptured)]
    [InlineData(BookingStatus.PendingCaregiverApproval, BookingCaptureEvidence.Ambiguous)]
    [InlineData(BookingStatus.Confirmed, BookingCaptureEvidence.NotCaptured)]
    [InlineData(BookingStatus.Confirmed, BookingCaptureEvidence.Ambiguous)]
    public void PaidBooking_WithoutConclusiveCaptureEvidence_IsRefusedNotAnsweredAsNoRefund(
        BookingStatus status,
        BookingCaptureEvidence captureEvidence)
    {
        BookingCancellationPolicyInput input = status == BookingStatus.Confirmed
            ? FamilyCancellation(
                status, AcceptanceOnUtc, null, CancelAfter("00:10:00"), captureEvidence, AcceptedReason())
            : FamilyCancellation(
                status, null, null, CancelledAtAcceptance, captureEvidence, NoteOnlyReason("note"));

        DomainException error = Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(input));

        Assert.Contains("cannot be decided", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(BookingCaptureEvidence.Captured)]
    [InlineData(BookingCaptureEvidence.Ambiguous)]
    public void PendingPayment_CapturedOrAmbiguousEvidence_IsRefused(BookingCaptureEvidence captureEvidence)
    {
        BookingCancellationPolicyInput input = FamilyCancellation(
            BookingStatus.PendingPayment, null, null, CancelledAtAcceptance, captureEvidence);

        Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(input));
    }

    [Fact]
    public void PaidBooking_AmbiguousEvidenceIsNeverSilentlyTreatedAsUnpaid()
    {
        // If the evidence were read as "nothing captured", this input would answer NoRefundDue /
        // NothingCaptured instead of throwing. Asserting the throw is the whole point.
        BookingCancellationPolicyInput input = new(
            BookingStatus.Confirmed,
            BookingCancellationActorSide.Family,
            BookingCancellationAction.Cancel,
            AcceptanceOnUtc,
            null,
            CancelAfter("02:00:00"),
            BookingCaptureEvidence.Ambiguous,
            AcceptedReason());

        Assert.Throws<DomainException>(() => BookingCancellationPolicy.Decide(input));
    }
}
