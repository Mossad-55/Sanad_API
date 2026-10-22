namespace Sanad.Modules.Families.Domain.Bookings;

/// <summary>
/// Immutable, self-contained answer of <see cref="BookingRefundPolicy.Decide"/>: which entitlement
/// applies, why, and the exact entitlement-bearing amounts in piastres.
/// <para>
/// Unlike the decision-version-1 <see cref="BookingCancellationDecision"/>, this decision carries
/// money because the approved policy is no longer only "full or nothing": a family cancellation
/// inside 24 hours of the scheduled start is a 50% award, and a multi-session cancellation awards
/// the sum of specific allocated sessions. Carrying the amounts on the decision — rather than
/// letting a caller recompute them — is what keeps the recorded fact and the money moved in
/// agreement.
/// </para>
/// <para>
/// A decision can only be produced by the policy (private constructor, domain-internal factory,
/// get-only properties, no <c>with</c> clone). A caller therefore cannot forge or alter an
/// entitlement, an award, a ceiling or a reason.
/// </para>
/// </summary>
public sealed class BookingRefundDecision
{
    private BookingRefundDecision(
        BookingStatus statusAtAct,
        BookingCancellationActorSide actorSide,
        BookingCancellationAction action,
        BookingRefundEntitlement refundEntitlement,
        BookingRefundDecisionReason refundDecisionReason,
        BookingRefundOutcomeReason? outcomeReason,
        BookingRefundPolicyVersion policyVersion,
        DateTime? scheduledStartUtcUsed,
        DateTime? confirmedOnUtcUsed,
        DateTime actedOnUtc,
        bool isReasonFeedbackRequired,
        long capturedMinorUnits,
        long refundableCeilingMinorUnits,
        long awardedMinorUnits,
        long? awardedProrationBasisUnits,
        bool isDeliveredSessionNonRefundable,
        IReadOnlyList<BookingSessionAllocation> refundedSessions)
    {
        StatusAtAct = statusAtAct;
        ActorSide = actorSide;
        Action = action;
        RefundEntitlement = refundEntitlement;
        RefundDecisionReason = refundDecisionReason;
        OutcomeReason = outcomeReason;
        PolicyVersion = policyVersion;
        ScheduledStartUtcUsed = scheduledStartUtcUsed;
        ConfirmedOnUtcUsed = confirmedOnUtcUsed;
        ActedOnUtc = actedOnUtc;
        IsReasonFeedbackRequired = isReasonFeedbackRequired;
        CapturedMinorUnits = capturedMinorUnits;
        RefundableCeilingMinorUnits = refundableCeilingMinorUnits;
        AwardedMinorUnits = awardedMinorUnits;
        AwardedProrationBasisUnits = awardedProrationBasisUnits;
        IsDeliveredSessionNonRefundable = isDeliveredSessionNonRefundable;
        RefundedSessions = refundedSessions;
    }

    /// <summary>Booking state the decision was made for.</summary>
    public BookingStatus StatusAtAct { get; }

    /// <summary>Family or caregiver.</summary>
    public BookingCancellationActorSide ActorSide { get; }

    /// <summary>Cancel, or caregiver rejection.</summary>
    public BookingCancellationAction Action { get; }

    /// <summary>Policy eligibility: nothing, a partial share, or the whole captured amount.</summary>
    public BookingRefundEntitlement RefundEntitlement { get; }

    /// <summary>Why that eligibility was reached (policy-vocabulary reason).</summary>
    public BookingRefundDecisionReason RefundDecisionReason { get; }

    /// <summary>
    /// The clause 4 / clause 5 outcome reason, or null for an ordinary cancellation. Set exactly
    /// when the act was a provider cancellation, no-show, excessive lateness, inaccessibility,
    /// safety cancellation or force majeure, so a read model can tell an ordinary cancellation
    /// apart from an external failure without re-deriving it.
    /// </summary>
    public BookingRefundOutcomeReason? OutcomeReason { get; }

    /// <summary>The policy revision the acting booking was sold under.</summary>
    public BookingRefundPolicyVersion PolicyVersion { get; }

    /// <summary>The scheduled-start instant the decision used; null when the booking carried none.</summary>
    public DateTime? ScheduledStartUtcUsed { get; }

    /// <summary>
    /// The acceptance time the decision was made against; null while the booking was unaccepted.
    /// Carried so the recorded fact preserves the evidence the decision actually used instead of
    /// re-reading a mutable aggregate after the fact.
    /// </summary>
    public DateTime? ConfirmedOnUtcUsed { get; }

    /// <summary>Authoritative UTC time of the act.</summary>
    public DateTime ActedOnUtc { get; }

    /// <summary>True when a known category and a non-blank note are mandatory (accepted bookings).</summary>
    public bool IsReasonFeedbackRequired { get; }

    /// <summary>Money captured for the booking at decision time, in piastres.</summary>
    public long CapturedMinorUnits { get; }

    /// <summary>
    /// The most that may be awarded right now: <see cref="CapturedMinorUnits"/> less previous
    /// refunds, floored at zero. Owner decision #4 is enforced as <c>award ≤ ceiling</c>.
    /// </summary>
    public long RefundableCeilingMinorUnits { get; }

    /// <summary>Amount this decision awards, in piastres. Zero when the entitlement denies a refund.</summary>
    public long AwardedMinorUnits { get; }

    /// <summary>
    /// For a percentage award, the pre-proration basis in piastres (the amount a full refund would
    /// have awarded). Null when the award is not a prorated share. Carried so a review can see the
    /// arithmetic the policy applied rather than only its result.
    /// </summary>
    public long? AwardedProrationBasisUnits { get; }

    /// <summary>
    /// True when a delivered session was excluded from the award under clause 3. Recorded so a
    /// denial caused by an already-delivered session is distinguishable from a plain denial.
    /// </summary>
    public bool IsDeliveredSessionNonRefundable { get; }

    /// <summary>
    /// The sessions this award refunded, at their allocated package prices. Empty for a
    /// single-session booking and for a denial.
    /// </summary>
    public IReadOnlyList<BookingSessionAllocation> RefundedSessions { get; }

    /// <summary>True when the decision moves no money.</summary>
    public bool IsZeroAward => AwardedMinorUnits == 0;

    /// <summary>
    /// Sole construction path, reachable only from inside the domain assembly. The policy has
    /// already validated every value and clamped the award to the decision #4 ceiling.
    /// </summary>
    internal static BookingRefundDecision Create(
        BookingStatus statusAtAct,
        BookingCancellationActorSide actorSide,
        BookingCancellationAction action,
        BookingRefundEntitlement refundEntitlement,
        BookingRefundDecisionReason refundDecisionReason,
        BookingRefundOutcomeReason? outcomeReason,
        BookingRefundPolicyVersion policyVersion,
        DateTime? scheduledStartUtcUsed,
        DateTime? confirmedOnUtcUsed,
        DateTime actedOnUtc,
        bool isReasonFeedbackRequired,
        long capturedMinorUnits,
        long refundableCeilingMinorUnits,
        long awardedMinorUnits,
        long? awardedProrationBasisUnits,
        bool isDeliveredSessionNonRefundable,
        IReadOnlyList<BookingSessionAllocation>? refundedSessions) =>
        new(
            statusAtAct,
            actorSide,
            action,
            refundEntitlement,
            refundDecisionReason,
            outcomeReason,
            policyVersion,
            scheduledStartUtcUsed,
            confirmedOnUtcUsed,
            actedOnUtc,
            isReasonFeedbackRequired,
            capturedMinorUnits,
            refundableCeilingMinorUnits,
            awardedMinorUnits,
            awardedProrationBasisUnits,
            isDeliveredSessionNonRefundable,
            refundedSessions ?? []);

    /// <summary>
    /// Integration seam for the single-argument cancellation-record factory: describes this
    /// decision in the decision-version-1 vocabulary without changing any amount. The
    /// classification is deliberately biased towards the refund vocabulary — a non-zero award
    /// maps to a full award and a zero award maps to a denial — so a legacy factory can never
    /// report "nothing captured" for a booking the new policy actually declined or awarded. The
    /// exact reason and the exact amount travel on this object.
    /// </summary>
    internal (BookingRefundEntitlement Entitlement, BookingRefundDecisionReason Reason, bool IsCaregiverIncident) DescribeForCancellationFact()
    {
        BookingRefundEntitlement entitlement = AwardedMinorUnits > 0
            ? BookingRefundEntitlement.FullCapturedRefund
            : BookingRefundEntitlement.NoRefundDue;

        BookingRefundDecisionReason reason = RefundDecisionReason switch
        {
            BookingRefundDecisionReason.FamilyCancellationWithinGraceWindow =>
                BookingRefundDecisionReason.FamilyCancellationWithinGraceWindow,
            _ => BookingRefundDecisionReason.FamilyCancellationOutsideGraceWindow
        };

        if (RefundDecisionReason is BookingRefundDecisionReason.CaregiverRejectionBeforeAcceptance
            or BookingRefundDecisionReason.CaregiverCancellationAfterAcceptance
            or BookingRefundDecisionReason.FamilyCancellationBeforeAcceptance)
        {
            reason = RefundDecisionReason;
        }

        bool isCaregiverIncident =
            StatusAtAct == BookingStatus.Confirmed
            && ActorSide == BookingCancellationActorSide.Caregiver
            && Action == BookingCancellationAction.Cancel;

        return (entitlement, reason, isCaregiverIncident);
    }

    public override string ToString() =>
        $"policy v{PolicyVersion.Version} {RefundEntitlement} ({RefundDecisionReason}) award=" +
        $"{AwardedMinorUnits} of captured={CapturedMinorUnits} ceiling={RefundableCeilingMinorUnits}";
}
