namespace Sanad.Modules.Families.Domain.Bookings;

/// <summary>
/// Immutable answer of <see cref="BookingCancellationPolicy.Decide"/>. It decides nothing about money
/// movement and mutates nothing; it is the only accepted input for
/// <see cref="BookingCancellationFact.Create"/>.
/// <para>
/// Instances can only be produced by the policy: the constructor is private, the factory is internal
/// to the domain assembly, the type is a class rather than a record (so no <c>with</c> clone exists)
/// and every property is get-only with no setter or initializer. A caller therefore cannot forge or
/// alter an entitlement, an incident flag, a policy version, a timestamp or the bound feedback.
/// </para>
/// </summary>
public sealed class BookingCancellationDecision
{
    private BookingCancellationDecision(
        BookingStatus statusAtCancellation,
        BookingCancellationActorSide actorSide,
        BookingCancellationAction action,
        DateTime cancelledOnUtc,
        DateTime? confirmedOnUtcUsed,
        BookingRefundEntitlement refundEntitlement,
        BookingRefundDecisionReason refundDecisionReason,
        bool isCaregiverIncident,
        bool isReasonFeedbackRequired,
        BookingCancellationFeedback? feedback,
        int policyVersion)
    {
        StatusAtCancellation = statusAtCancellation;
        ActorSide = actorSide;
        Action = action;
        CancelledOnUtc = cancelledOnUtc;
        ConfirmedOnUtcUsed = confirmedOnUtcUsed;
        RefundEntitlement = refundEntitlement;
        RefundDecisionReason = refundDecisionReason;
        IsCaregiverIncident = isCaregiverIncident;
        IsReasonFeedbackRequired = isReasonFeedbackRequired;
        Feedback = feedback;
        PolicyVersion = policyVersion;
    }

    /// <summary>Booking state the decision was made for.</summary>
    public BookingStatus StatusAtCancellation { get; }

    /// <summary>Family or caregiver.</summary>
    public BookingCancellationActorSide ActorSide { get; }

    /// <summary>Cancel, or caregiver rejection.</summary>
    public BookingCancellationAction Action { get; }

    /// <summary>Authoritative cancellation time the decision used.</summary>
    public DateTime CancelledOnUtc { get; }

    /// <summary>Acceptance time the decision used; null before acceptance.</summary>
    public DateTime? ConfirmedOnUtcUsed { get; }

    /// <summary>Policy eligibility, including the captured platform fee when full.</summary>
    public BookingRefundEntitlement RefundEntitlement { get; }

    /// <summary>Why that entitlement was reached.</summary>
    public BookingRefundDecisionReason RefundDecisionReason { get; }

    /// <summary>
    /// True only for a caregiver cancelling a booking they had accepted. Review signal only: it never
    /// means automatic suspension, rating change or payout penalty.
    /// </summary>
    public bool IsCaregiverIncident { get; }

    /// <summary>True when a known category and a non-blank note are mandatory (accepted bookings).</summary>
    public bool IsReasonFeedbackRequired { get; }

    /// <summary>
    /// The exact reason the policy validated for this decision: a category plus note for an accepted
    /// booking, an optional note-only reason before acceptance, or null when nothing was supplied.
    /// Recording reads the reason from here, so it can never be swapped for unrelated feedback.
    /// </summary>
    public BookingCancellationFeedback? Feedback { get; }

    /// <summary>Policy revision that produced this decision.</summary>
    public int PolicyVersion { get; }

    /// <summary>
    /// Sole construction path, reachable only from inside the domain assembly. The policy validates
    /// every value before calling it.
    /// </summary>
    internal static BookingCancellationDecision Create(
        BookingStatus statusAtCancellation,
        BookingCancellationActorSide actorSide,
        BookingCancellationAction action,
        DateTime cancelledOnUtc,
        DateTime? confirmedOnUtcUsed,
        BookingRefundEntitlement refundEntitlement,
        BookingRefundDecisionReason refundDecisionReason,
        bool isCaregiverIncident,
        bool isReasonFeedbackRequired,
        BookingCancellationFeedback? feedback,
        int policyVersion) =>
        new(
            statusAtCancellation,
            actorSide,
            action,
            cancelledOnUtc,
            confirmedOnUtcUsed,
            refundEntitlement,
            refundDecisionReason,
            isCaregiverIncident,
            isReasonFeedbackRequired,
            feedback,
            policyVersion);

    public override bool Equals(object? obj) =>
        obj is BookingCancellationDecision other && Equals(other);

    private bool Equals(BookingCancellationDecision other) =>
        StatusAtCancellation == other.StatusAtCancellation
        && ActorSide == other.ActorSide
        && Action == other.Action
        && CancelledOnUtc.Equals(other.CancelledOnUtc)
        && Nullable.Equals(ConfirmedOnUtcUsed, other.ConfirmedOnUtcUsed)
        && RefundEntitlement == other.RefundEntitlement
        && RefundDecisionReason == other.RefundDecisionReason
        && IsCaregiverIncident == other.IsCaregiverIncident
        && IsReasonFeedbackRequired == other.IsReasonFeedbackRequired
        && Equals(Feedback, other.Feedback)
        && PolicyVersion == other.PolicyVersion;

    public override int GetHashCode() =>
        HashCode.Combine(
            StatusAtCancellation,
            ActorSide,
            Action,
            CancelledOnUtc,
            ConfirmedOnUtcUsed,
            RefundEntitlement,
            RefundDecisionReason,
            HashCode.Combine(
                IsCaregiverIncident,
                IsReasonFeedbackRequired,
                Feedback,
                PolicyVersion));
}
