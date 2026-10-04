using Microsoft.EntityFrameworkCore;
using Sanad.Modules.CareHomes.Application.Inventory;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;

namespace Sanad.Modules.CareHomes.Infrastructure.Bookings;

public sealed class CareHomeBookingOccupancyProvider(CareHomesDbContext db) : ICareHomeOccupancyProvider
{
    public async Task<IReadOnlyList<CareHomeOccupancyInterval>> GetActiveAsync(CareHomeId facilityId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var rows = await db.Bookings.AsNoTracking().Where(x => x.FacilityId == facilityId && ((x.Status == CareHomeBookingStatus.PendingPayment && x.CheckoutHoldUntilUtc > now) || (x.Status == CareHomeBookingStatus.PaidAwaitingDecision && x.DecisionHoldUntilUtc > now) || x.Status == CareHomeBookingStatus.Accepted)).Join(db.RoomTypes.AsNoTracking(), booking => booking.RoomTypeId, type => type.Id, (booking, type) => new { booking, type.AllocationMode }).ToListAsync(cancellationToken);
        var result = new List<CareHomeOccupancyInterval>(rows.Count * 2);
        foreach (var row in rows)
        {
            var booking = row.booking;
            DateOnly endDate = booking.EndDate;
            if (booking.ActualCheckOutOnUtc is DateTime checkout)
            {
                TimeZoneInfo cairo;
                try { cairo = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); }
                catch (TimeZoneNotFoundException) { cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); }
                endDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(checkout, cairo));
                if (endDate > booking.EndDate) endDate = booking.EndDate;
            }
            if (endDate <= booking.StartDate) continue;
            if (booking.AssignedRoomId is Guid roomId)
            {
                if (row.AllocationMode == CareHomeAllocationMode.Shared && booking.AssignedBedId is Guid bedId)
                    result.Add(new CareHomeOccupancyInterval(bedId, CareHomeResourceKind.Bed, booking.StartDate, endDate));
                else
                    result.Add(new CareHomeOccupancyInterval(roomId, CareHomeResourceKind.Room, booking.StartDate, endDate));
            }
            else
            {
                // Before physical assignment, the booking hold is represented at
                // room-type level. Once assigned, the physical resource is the
                // source of truth; emitting both would double-count Shared beds.
                result.Add(new CareHomeOccupancyInterval(booking.RoomTypeId, CareHomeResourceKind.RoomType, booking.StartDate, endDate));
            }
        }
        return result;
    }
}
