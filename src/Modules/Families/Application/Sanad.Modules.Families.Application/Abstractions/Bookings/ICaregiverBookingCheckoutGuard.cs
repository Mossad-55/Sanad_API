using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Application.Abstractions.Bookings;

/// <summary>
/// Serializes the slot check and the booking insert for one caregiver and one
/// date inside a serializable transaction plus a PostgreSQL advisory lock, so
/// two concurrent checkouts cannot both pass the conflict check. Implemented
/// in Families Infrastructure.
/// </summary>
public interface ICaregiverBookingCheckoutGuard
{
    Task<T> ExecuteAsync<T>(
        CaregiverId caregiverId,
        DateOnly bookingDate,
        Func<Task<T>> action,
        CancellationToken cancellationToken);
}

/// <summary>
/// Raised when the serializable transaction could not be serialized
/// (<c>40001</c>/<c>40P01</c>); the checkout maps it to
/// <c>Bookings.ScheduleConflict</c> because the slot outcome is unknown, never
/// to the availability code.
/// </summary>
public sealed class CaregiverBookingScheduleConflictException : Exception;
