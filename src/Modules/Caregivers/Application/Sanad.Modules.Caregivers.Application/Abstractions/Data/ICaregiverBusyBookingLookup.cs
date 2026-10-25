using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Application.Abstractions.Data;

/// <summary>
/// Busy windows already held by caregiver bookings on one calendar date,
/// expressed as local day times exactly as the checkout overlap predicate sees
/// them. Implemented in the API host over the Families bookings store.
/// </summary>
public interface ICaregiverBusyBookingLookup
{
    /// <summary>
    /// Returns the stored start/end times of every caregiver booking on
    /// <paramref name="date"/> whose status is
    /// <c>PendingPayment | PendingCaregiverApproval | Confirmed | InProgress</c>.
    /// The date filter is the literal <c>BookingDate == date</c> equality used
    /// by checkout, which is why a booking that started the previous day and
    /// runs past midnight is not reported here either.
    /// </summary>
    Task<IReadOnlyList<CaregiverBusyWindow>> GetBusyWindowsAsync(
        CaregiverId caregiverId,
        DateOnly date,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One booked window as stored on the booking: raw local day times, with
/// <see cref="EndTime"/> earlier than <see cref="StartTime"/> when the booking
/// crosses midnight.
/// </summary>
public sealed record CaregiverBusyWindow(
    TimeOnly StartTime,
    TimeOnly EndTime);
