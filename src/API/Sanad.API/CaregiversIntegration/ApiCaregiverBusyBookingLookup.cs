using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.API.CaregiversIntegration;

/// <summary>
/// Busy windows over the Families booking store using exactly the checkout
/// predicate: the four active statuses and <c>BookingDate == date</c>. The
/// date equality (rather than an overlap-window query) is what mirrors the
/// existing cross-midnight gap; this lookup does not fix it.
/// </summary>
public sealed class ApiCaregiverBusyBookingLookup(
    IFamiliesDbContext families) : ICaregiverBusyBookingLookup
{
    public async Task<IReadOnlyList<CaregiverBusyWindow>> GetBusyWindowsAsync(
        CaregiverId caregiverId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var windows = await families.Bookings
            .AsNoTracking()
            .Where(booking =>
                booking.CaregiverId == caregiverId &&
                booking.BookingDate == date &&
                (booking.Status == BookingStatus.PendingPayment ||
                 booking.Status == BookingStatus.PendingCaregiverApproval ||
                 booking.Status == BookingStatus.Confirmed ||
                 booking.Status == BookingStatus.InProgress))
            .Select(booking => new
            {
                booking.StartTime,
                booking.EndTime
            })
            .ToListAsync(cancellationToken);

        return windows
            .Select(window => new CaregiverBusyWindow(window.StartTime, window.EndTime))
            .ToArray();
    }
}
