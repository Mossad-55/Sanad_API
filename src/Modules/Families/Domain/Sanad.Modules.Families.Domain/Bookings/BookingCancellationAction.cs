namespace Sanad.Modules.Families.Domain.Bookings;

/// <summary>
/// Kind of cancellation act that was performed.
/// <see cref="Cancel"/> ends a booking the actor already holds (family booking, or a caregiver-accepted
/// booking). <see cref="Reject"/> is a caregiver declining a booking that is still awaiting approval.
/// A rejection can never stand in for cancelling an accepted booking.
/// </summary>
public enum BookingCancellationAction
{
    Cancel = 1,
    Reject = 2
}
