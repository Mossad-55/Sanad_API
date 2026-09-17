namespace Sanad.Modules.Families.Domain.Bookings;

/// <summary>
/// Why the policy reached its <see cref="BookingRefundEntitlement"/>. Every member is a policy
/// outcome; missing or contradictory capture evidence never produces one of these, it fails the
/// request instead. Recorded with the cancellation fact so a later read model can tell "no money was
/// ever captured" apart from "the policy denied a refund", and never present
/// <see cref="BookingRefundEntitlement.NoRefundDue"/> as a failed refund.
/// </summary>
public enum BookingRefundDecisionReason
{
    /// <summary>Booking cancelled while unpaid: there is no captured money to refund.</summary>
    NothingCaptured = 1,

    /// <summary>Family cancelled while the booking was still awaiting caregiver approval.</summary>
    FamilyCancellationBeforeAcceptance = 2,

    /// <summary>Assigned caregiver rejected a booking that was still awaiting approval.</summary>
    CaregiverRejectionBeforeAcceptance = 3,

    /// <summary>Assigned caregiver cancelled a booking they had accepted.</summary>
    CaregiverCancellationAfterAcceptance = 4,

    /// <summary>Family cancelled inside the 60-minute window measured from the acceptance time.</summary>
    FamilyCancellationWithinGraceWindow = 5,

    /// <summary>
    /// Family cancelled at or after 60 minutes from the acceptance time: a policy denial on a paid
    /// booking, not a missing-capture outcome.
    /// </summary>
    FamilyCancellationOutsideGraceWindow = 6
}
