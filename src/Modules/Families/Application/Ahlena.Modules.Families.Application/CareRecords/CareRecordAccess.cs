using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Domain.Bookings;
using Ahlena.Modules.Families.Domain.CareRecords;

namespace Ahlena.Modules.Families.Application.CareRecords;

/// <summary>
/// The locked D1 caregiver window, implemented once and revalidated on every
/// caregiver write and read: the caregiver must hold, for the exact elderly
/// dependent, a booking that is Confirmed or InProgress, or one that
/// Completed within the last 30 days (the grace period, inclusive of the
/// boundary instant). There is no caregiver-dependent assignment table — the
/// booking set is the authorization. Medical and companion caregivers are
/// both covered without distinction; type never widens or narrows scope.
/// </summary>
internal static class CaregiverCareRecordAccess
{
    public const int CompletionGraceDays = 30;

    public static Task<bool> HasAuthorizedDependentAsync(
        IFamiliesDbContext dbContext,
        CaregiverId caregiverId,
        ElderlyId elderlyId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        DateTime graceCutoffUtc = utcNow.AddDays(-CompletionGraceDays);

        return dbContext.Bookings.AnyAsync(
            booking => booking.CaregiverId == caregiverId
                && booking.ElderlyId == elderlyId
                && (booking.Status == BookingStatus.Confirmed
                    || booking.Status == BookingStatus.InProgress
                    || (booking.Status == BookingStatus.Completed
                        && booking.CompletedOnUtc != null
                        && booking.CompletedOnUtc.Value >= graceCutoffUtc)),
            cancellationToken);
    }
}
