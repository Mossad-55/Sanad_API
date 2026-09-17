namespace Sanad.Modules.Families.Domain.Bookings;

/// <summary>
/// Immutable answer of <see cref="BookingCancellationPolicy.Decide"/>. It decides nothing about money
/// movement and mutates nothing; it is the input used to record a <see cref="BookingCancellationFact"/>.
/// </summary>
/// <param name="StatusAtCancellation">Booking state the decision was made for.</param>
/// <param name="ActorSide">Family or caregiver.</param>
/// <param name="Action">Cancel, or caregiver rejection.</param>
/// <param name="CancelledOnUtc">Authoritative cancellation time the decision used.</param>
/// <param name="ConfirmedOnUtcUsed">Acceptance time the decision used; null before acceptance.</param>
/// <param name="RefundEntitlement">Policy eligibility, including the captured platform fee when full.</param>
/// <param name="RefundDecisionReason">Why that entitlement was reached.</param>
/// <param name="IsCaregiverIncident">
/// True only for a caregiver cancelling a booking they had accepted. Review signal only: it never
/// means automatic suspension, rating change or payout penalty.
/// </param>
/// <param name="IsReasonFeedbackRequired">True when category and note are mandatory (accepted bookings).</param>
/// <param name="PolicyVersion">Policy revision that produced this decision.</param>
public sealed record BookingCancellationDecision(
    BookingStatus StatusAtCancellation,
    BookingCancellationActorSide ActorSide,
    BookingCancellationAction Action,
    DateTime CancelledOnUtc,
    DateTime? ConfirmedOnUtcUsed,
    BookingRefundEntitlement RefundEntitlement,
    BookingRefundDecisionReason RefundDecisionReason,
    bool IsCaregiverIncident,
    bool IsReasonFeedbackRequired,
    int PolicyVersion);
