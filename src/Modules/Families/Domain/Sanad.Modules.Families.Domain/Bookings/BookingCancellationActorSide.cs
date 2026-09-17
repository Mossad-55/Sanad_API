namespace Sanad.Modules.Families.Domain.Bookings;

/// <summary>
/// Side of the marketplace that ended a booking. Persisted on <see cref="BookingCancellationFact"/>
/// together with the acting user id, which is always supplied by the calling application
/// (an authorized handler), never trusted from client claims.
/// </summary>
public enum BookingCancellationActorSide
{
    Family = 1,
    Caregiver = 2
}
