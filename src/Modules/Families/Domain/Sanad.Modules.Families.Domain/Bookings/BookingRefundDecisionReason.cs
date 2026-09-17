namespace Sanad.Modules.Families.Domain.Bookings;

/// <summary>
/// Why the policy reached its <see cref="BookingRefundEntitlement"/>. Recorded with the cancellation
/// fact so a later read model can tell "no money was ever captured" apart from "the policy denied a
/// refund", and never present <see cref="BookingRefundEntitlement.NoRefundDue"/> as a failed refund.
/// </summary>
public enum BookingRefundDecisionReason
{
    /// <summary>Booking cancelled before payment was captured: there is nothing to refund.</summary>
    NothingCaptured = 1,

    /// <summary>Policy would grant a refund, but no captured payment fact exists to refund.</summary>
    NoCapturedPaymentRecorded = 2,

    /// <summary>Family cancelled while the booking was still awaiting caregiver approval.</summary>
    FamilyCancellationBeforeAcceptance = 3,

    /// <summary>Assigned caregiver rejected a booking that was still awaiting approval.</summary>
    CaregiverRejectionBeforeAcceptance = 4,

    /// <summary>Assigned caregiver cancelled a booking they had accepted.</summary>
    CaregiverCancellationAfterAcceptance = 5,

    /// <summary>Family cancelled inside the 60-minute window measured from the acceptance time.</summary>
    FamilyCancellationWithinGraceWindow = 6,

    /// <summary>Family cancelled at or after 60 minutes from the acceptance time.</summary>
    FamilyCancellationOutsideGraceWindow = 7
}
