using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Application.Bookings;

public sealed record TransferCareHomeBookingCommand(UserId Actor, Guid BookingId, Guid RoomId, Guid? BedId, DateOnly EffectiveDate, DateTime UtcNow) : ICommand<CareHomeBookingResponse>;

public static class CareHomeAssignmentHistoryProjection
{
    public static (Guid? RoomId, Guid? BedId) Resolve(CareHomeBooking booking, IEnumerable<CareHomeBookingAssignmentHistory> history, DateOnly date)
    {
        var effective = history.Where(x => x.EffectiveDate <= date).OrderBy(x => x.EffectiveDate).ThenBy(x => x.OccurredOnUtc).LastOrDefault();
        return effective is null ? (booking.AssignedRoomId, booking.AssignedBedId) : (effective.ToRoomId, effective.ToBedId);
    }

    public static DateOnly CairoToday(DateTime utcNow)
    {
        try { return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"))); }
        catch (TimeZoneNotFoundException) { return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"))); }
    }
}

public sealed class TransferCareHomeBookingHandler(ICareHomesDbContext db, ICareHomeBookingReservationGuard reservationGuard) : ICommandHandler<TransferCareHomeBookingCommand, CareHomeBookingResponse>
{
    public async Task<Result<CareHomeBookingResponse>> Handle(TransferCareHomeBookingCommand r, CancellationToken ct)
    {
        var key = await db.Bookings.AsNoTracking().Where(x => x.Id == r.BookingId).Select(x => new { x.FacilityId, x.RoomTypeId, x.StartDate }).SingleOrDefaultAsync(ct);
        if (key is null) return Failure("CareHomes.Bookings.NotFound", "Booking was not found.");
        try
        {
            return await reservationGuard.ExecuteAsync(key.FacilityId, key.RoomTypeId, key.StartDate, async () =>
            {
                var booking = await db.Bookings.SingleOrDefaultAsync(x => x.Id == r.BookingId, ct);
                var facility = booking is null ? null : await db.Facilities.SingleOrDefaultAsync(x => x.Id == booking.FacilityId && x.OwnerUserId == r.Actor, ct);
                if (booking is null || facility is null) return Failure("CareHomes.Bookings.NotFound", "Booking was not found.");
                if (booking.Status != CareHomeBookingStatus.Accepted || booking.AssignedRoomId is null || booking.ActualCheckOutOnUtc is not null) return Failure("CareHomes.Bookings.InvalidState", "Only an accepted, checked-in or not-yet-checked-in stay can be transferred.");
                DateOnly today = CareHomeAssignmentHistoryProjection.CairoToday(r.UtcNow);
                if (r.EffectiveDate < today || r.EffectiveDate < booking.StartDate || r.EffectiveDate >= booking.EndDate) return Failure("CareHomes.Bookings.InvalidTransferDate", "The effective date must be today or later and within the stay.");
                if (await db.BookingAssignmentHistory.AnyAsync(x => x.BookingId == booking.Id && x.EffectiveDate == r.EffectiveDate && x.FromRoomId != null, ct)) return Failure("CareHomes.Bookings.TransferConflict", "Only one transfer is allowed per booking and local date.");
                var room = await db.Rooms.SingleOrDefaultAsync(x => x.Id == r.RoomId && x.FacilityId == booking.FacilityId && !x.IsArchived && x.RoomTypeId == booking.RoomTypeId, ct);
                var type = await db.RoomTypes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == booking.RoomTypeId, ct);
                if (room is null || type is null) return Failure("CareHomes.Bookings.InvalidAssignment", "The destination room is not an active room of the booking's room type.");
                if (type.AllocationMode == CareHomeAllocationMode.Shared && r.BedId is null || type.AllocationMode != CareHomeAllocationMode.Shared && r.BedId is not null) return Failure("CareHomes.Bookings.InvalidAssignment", "The destination bed shape is invalid for the room type.");
                if (r.BedId is Guid bedId && !await db.Beds.AnyAsync(x => x.Id == bedId && x.RoomId == room.Id && x.FacilityId == booking.FacilityId && !x.IsArchived, ct)) return Failure("CareHomes.Bookings.InvalidAssignment", "The destination bed is not in the selected room.");
                var histories = await db.BookingAssignmentHistory.AsNoTracking().Where(x => x.BookingId == booking.Id).OrderBy(x => x.EffectiveDate).ThenBy(x => x.OccurredOnUtc).ToListAsync(ct);
                var previous = CareHomeAssignmentHistoryProjection.Resolve(booking, histories, r.EffectiveDate);
                var existing = await db.Bookings.AsNoTracking().Where(x => x.Id != booking.Id && x.FacilityId == booking.FacilityId && x.Status == CareHomeBookingStatus.Accepted && x.StartDate < booking.EndDate && x.EndDate > booking.StartDate).ToListAsync(ct);
                var ids = existing.Select(x => x.Id).ToArray(); var existingHistory = await db.BookingAssignmentHistory.AsNoTracking().Where(x => ids.Contains(x.BookingId)).ToListAsync(ct);
                foreach (var other in existing)
                {
                    var segments = existingHistory.Where(x => x.BookingId == other.Id).OrderBy(x => x.EffectiveDate).ThenBy(x => x.OccurredOnUtc).ToList();
                    if (segments.Count == 0 && other.AssignedRoomId is Guid assigned) segments.Add(CareHomeBookingAssignmentHistory.CreateInitial(other.Id, assigned, other.AssignedBedId, other.StartDate, r.Actor, r.UtcNow));
                    for (int i = 0; i < segments.Count; i++)
                    {
                        DateOnly from = segments[i].EffectiveDate < other.StartDate ? other.StartDate : segments[i].EffectiveDate;
                        DateOnly to = i + 1 < segments.Count ? segments[i + 1].EffectiveDate : EffectiveEndDate(other);
                        if (from < to && from < booking.EndDate && to > r.EffectiveDate && ResourceConflict(type.AllocationMode, segments[i], room.Id, r.BedId)) return Failure("CareHomes.Bookings.AssignmentConflict", "The destination physical resource is occupied for the transfer interval.");
                    }
                }
                var maintenance = await db.MaintenanceBlocks.AsNoTracking().Where(x => x.FacilityId == booking.FacilityId && x.CancelledOnUtc == null && x.StartDate < booking.EndDate && x.EndDate > r.EffectiveDate).ToListAsync(ct);
                var bedIds = await db.Beds.Where(x => x.RoomId == room.Id && !x.IsArchived).Select(x => x.Id).ToListAsync(ct);
                if (maintenance.Any(x => x.Overlaps(r.EffectiveDate, EffectiveEndDate(booking)) && ((x.Target == CareHomeMaintenanceTarget.Room && x.TargetId == room.Id) || (x.Target == CareHomeMaintenanceTarget.Bed && r.BedId == x.TargetId) || (x.Target == CareHomeMaintenanceTarget.Room && type.AllocationMode == CareHomeAllocationMode.Shared && bedIds.Contains(r.BedId ?? Guid.Empty))))) return Failure("CareHomes.Bookings.AssignmentConflict", "The destination is under maintenance for the transfer interval.");
                var previousRoom = previous.RoomId ?? booking.AssignedRoomId!.Value; var previousBed = previous.BedId;
                if (r.EffectiveDate <= today) booking.TransferPhysicalResource(room.Id, r.BedId, r.UtcNow);
                var transfer = CareHomeBookingAssignmentHistory.CreateTransfer(booking.Id, previousRoom, previousBed, room.Id, r.BedId, r.EffectiveDate, r.Actor, r.UtcNow);
                db.BookingAssignmentHistory.Add(transfer); db.TransferNotificationOutbox.Add(CareHomeTransferNotificationOutbox.Create(booking.Id, transfer.Id, r.UtcNow));
                try { await db.SaveChangesAsync(ct); return CheckoutHandler.Map(booking); }
                catch (DbUpdateConcurrencyException) { return Failure("CareHomes.Bookings.TransferConflict", "The booking changed; reload and retry the transfer."); }
                catch (DbUpdateException) { return Failure("CareHomes.Bookings.TransferConflict", "The transfer conflicts with another reservation."); }
            }, ct);
        }
        catch (CareHomeCapacityConflictException) { return Failure("CareHomes.Bookings.TransferConflict", "The transfer conflicts with another reservation."); }
    }

    private static bool ResourceConflict(CareHomeAllocationMode mode, CareHomeBookingAssignmentHistory x, Guid roomId, Guid? bedId) => mode == CareHomeAllocationMode.Shared ? x.ToBedId == bedId || x.ToRoomId == roomId && x.ToBedId is null : x.ToRoomId == roomId;
    private static DateOnly EffectiveEndDate(CareHomeBooking b) { if (b.ActualCheckOutOnUtc is not DateTime checkout) return b.EndDate; var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(checkout, CairoZone())); return date < b.EndDate ? date : b.EndDate; }
    private static TimeZoneInfo CairoZone() { try { return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); } catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); } }
    private static Result<CareHomeBookingResponse> Failure(string code, string message) => Result<CareHomeBookingResponse>.Failure(new(code, message));
}
