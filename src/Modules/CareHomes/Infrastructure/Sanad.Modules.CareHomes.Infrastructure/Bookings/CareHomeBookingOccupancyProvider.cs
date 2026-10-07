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
        var history = await db.BookingAssignmentHistory.AsNoTracking().Where(x => rows.Select(r => r.booking.Id).Contains(x.BookingId)).OrderBy(x => x.EffectiveDate).ThenBy(x => x.OccurredOnUtc).ToListAsync(cancellationToken);
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
            var segments = history.Where(x => x.BookingId == booking.Id).ToList();
            if (segments.Count > 0)
            {
                if (segments[0].EffectiveDate > booking.StartDate && segments[0].FromRoomId is Guid priorRoomId)
                {
                    DateOnly priorEnd = segments[0].EffectiveDate < endDate ? segments[0].EffectiveDate : endDate;
                    if (booking.StartDate < priorEnd)
                    {
                        if (row.AllocationMode == CareHomeAllocationMode.Shared && segments[0].FromBedId is Guid priorBedId)
                            result.Add(new CareHomeOccupancyInterval(priorBedId, CareHomeResourceKind.Bed, booking.StartDate, priorEnd));
                        else result.Add(new CareHomeOccupancyInterval(priorRoomId, CareHomeResourceKind.Room, booking.StartDate, priorEnd));
                    }
                }
                for (int i = 0; i < segments.Count; i++)
                {
                    DateOnly from = segments[i].EffectiveDate < booking.StartDate ? booking.StartDate : segments[i].EffectiveDate;
                    DateOnly to = i + 1 < segments.Count ? segments[i + 1].EffectiveDate : endDate;
                    if (from >= to) continue;
                    if (row.AllocationMode == CareHomeAllocationMode.Shared && segments[i].ToBedId is Guid bedId)
                        result.Add(new CareHomeOccupancyInterval(bedId, CareHomeResourceKind.Bed, from, to));
                    else
                        result.Add(new CareHomeOccupancyInterval(segments[i].ToRoomId, CareHomeResourceKind.Room, from, to));
                }
            }
            else if (booking.AssignedRoomId is Guid roomId)
            {
                if (row.AllocationMode == CareHomeAllocationMode.Shared && booking.AssignedBedId is Guid bedId)
                    result.Add(new CareHomeOccupancyInterval(bedId, CareHomeResourceKind.Bed, booking.StartDate, endDate));
                else result.Add(new CareHomeOccupancyInterval(roomId, CareHomeResourceKind.Room, booking.StartDate, endDate));
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
