using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.Families.Application.Abstractions.Data;

namespace Sanad.Modules.CareHomes.Application.Bookings;

public sealed record AssignCareHomeBookingCommand(UserId Actor, Guid BookingId, Guid RoomId, Guid? BedId, DateTime UtcNow) : ICommand<CareHomeBookingResponse>;
public sealed record RecordCareHomeCheckInCommand(UserId Actor, Guid BookingId, DateTime UtcNow) : ICommand<CareHomeBookingResponse>;
public sealed record RecordCareHomeCheckOutCommand(UserId Actor, Guid BookingId, DateTime UtcNow) : ICommand<CareHomeBookingResponse>;
public sealed record GetOwnerCareHomeBookingOperationalQuery(UserId Actor, Guid BookingId) : IQuery<CareHomeOwnerBookingOperationalResponse>;
public sealed record ConfirmCareHomeCheckInCommand(UserId Actor, FamilyId FamilyId, Guid BookingId, DateTime UtcNow) : ICommand<CareHomeBookingResponse>;
public sealed record ListCareHomeCheckInDisputesQuery() : IQuery<IReadOnlyList<CareHomeCheckInDisputeResponse>>;
public sealed record ResolveCareHomeCheckInDisputeCommand(UserId Actor, Guid CaseId, DateTime EffectiveCheckInOnUtc, string Evidence, string Reason, DateTime UtcNow) : ICommand<CareHomeCheckInDisputeResponse>;
public sealed record SubmitCareHomeCheckInDisputeCommand(UserId Actor, FamilyId FamilyId, Guid BookingId, string Reason, DateTime UtcNow) : ICommand<CareHomeCheckInDisputeResponse>;
public sealed record CareHomeCheckInDisputeResponse(Guid Id, Guid BookingId, Guid FacilityId, DateTime? CheckoutOnUtc, CareHomeCheckInDisputeStatus Status, DateTime OpenedOnUtc, Guid OpenedBy, DateTime? EffectiveCheckInOnUtc, string? Evidence, string? Reason, string? FamilyReason, DateTime? ResolvedOnUtc, Guid? ResolvedBy);
public sealed record CareHomeOwnerBookingOperationalResponse(Guid Id, Guid FacilityId, Guid RoomTypeId, Guid? AssignedRoomId, Guid? AssignedBedId, DateTime? ActualCheckInOnUtc, Guid? ActualCheckInRecordedBy, DateTime? ActualCheckOutOnUtc, Guid? ActualCheckOutRecordedBy, DateTime? FamilyCheckInConfirmedOnUtc, Guid? FamilyCheckInConfirmedBy);

public sealed class AssignCareHomeBookingHandler(ICareHomesDbContext db, ICareHomeBookingReservationGuard reservationGuard) : ICommandHandler<AssignCareHomeBookingCommand, CareHomeBookingResponse>
{
    public async Task<Result<CareHomeBookingResponse>> Handle(AssignCareHomeBookingCommand r, CancellationToken ct)
    {
        try
        {
            var key = await db.Bookings.AsNoTracking().Where(x => x.Id == r.BookingId).Select(x => new { x.FacilityId, x.RoomTypeId, x.StartDate }).SingleOrDefaultAsync(ct);
            if (key is null) return NotFound();
            return await reservationGuard.ExecuteAsync(key.FacilityId, key.RoomTypeId, key.StartDate, async () =>
            {
                var booking = await db.Bookings.SingleOrDefaultAsync(x => x.Id == r.BookingId, ct);
                if (booking is null) return NotFound();
                var facility = await db.Facilities.SingleOrDefaultAsync(x => x.OwnerUserId == r.Actor && x.Id == booking.FacilityId, ct);
                var room = await db.Rooms.SingleOrDefaultAsync(x => x.Id == r.RoomId && x.FacilityId == booking.FacilityId && !x.IsArchived && x.RoomTypeId == booking.RoomTypeId, ct);
                var type = await db.RoomTypes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == booking.RoomTypeId, ct);
                if (facility is null || room is null || type is null) return NotFound();
                if (type.AllocationMode == CareHomeAllocationMode.Shared && r.BedId is null)
                    return Result<CareHomeBookingResponse>.Failure(new("CareHomes.Bookings.InvalidAssignment", "A shared stay requires a bed assignment."));
                if (type.AllocationMode != CareHomeAllocationMode.Shared && r.BedId is not null)
                    return Result<CareHomeBookingResponse>.Failure(new("CareHomes.Bookings.InvalidAssignment", "Private and suite stays reserve the whole room."));
                if (r.BedId is Guid bedId && await db.Beds.AnyAsync(x => x.Id == bedId && x.RoomId == room.Id && x.FacilityId == booking.FacilityId && !x.IsArchived, ct) is false)
                    return Result<CareHomeBookingResponse>.Failure(new("CareHomes.Bookings.InvalidAssignment", "The selected bed is not in the selected room."));
                var candidates = await db.Bookings.AsNoTracking().Where(x => x.Id != booking.Id && x.FacilityId == booking.FacilityId && x.Status == CareHomeBookingStatus.Accepted && x.StartDate < booking.EndDate && x.EndDate > booking.StartDate).ToListAsync(ct);
                var conflicts = candidates.Any(x =>
                {
                    DateOnly existingEnd = EffectiveEndDate(x);
                    if (existingEnd <= booking.StartDate || x.StartDate >= booking.EndDate) return false;
                    return type.AllocationMode == CareHomeAllocationMode.Shared
                        ? x.AssignedBedId == r.BedId || (x.AssignedRoomId == r.RoomId && x.AssignedBedId is null)
                        : x.AssignedRoomId == r.RoomId;
                });
                if (conflicts) return Result<CareHomeBookingResponse>.Failure(new("CareHomes.Bookings.AssignmentConflict", "The selected physical resource is occupied for this stay."));
                try { booking.AssignPhysicalResource(r.RoomId, r.BedId, r.UtcNow); await db.SaveChangesAsync(ct); return CheckoutHandler.Map(booking); }
                catch (InvalidOperationException ex) { return Result<CareHomeBookingResponse>.Failure(new("CareHomes.Bookings.InvalidState", ex.Message)); }
            }, ct);
        }
        catch (CareHomeCapacityConflictException) { return Result<CareHomeBookingResponse>.Failure(new("CareHomes.Bookings.AssignmentConflict", "The selected physical resource is occupied for this stay.")); }
    }
    private static Result<CareHomeBookingResponse> NotFound() => Result<CareHomeBookingResponse>.Failure(new("CareHomes.Bookings.NotFound", "Booking was not found."));
    private static DateOnly EffectiveEndDate(CareHomeBooking booking)
    {
        if (booking.ActualCheckOutOnUtc is not DateTime checkout) return booking.EndDate;
        TimeZoneInfo cairo;
        try { cairo = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); }
        catch (TimeZoneNotFoundException) { cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); }
        DateOnly checkoutDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(checkout, cairo));
        return checkoutDate < booking.EndDate ? checkoutDate : booking.EndDate;
    }
}

public sealed class RecordCareHomeCheckInHandler(ICareHomesDbContext db) : ICommandHandler<RecordCareHomeCheckInCommand, CareHomeBookingResponse>
{
    public async Task<Result<CareHomeBookingResponse>> Handle(RecordCareHomeCheckInCommand r, CancellationToken ct)
    { var b = await Owned(db, r.Actor, r.BookingId, ct); if (b is null) return Missing(); try { b.RecordCheckIn(r.Actor, r.UtcNow); await db.SaveChangesAsync(ct); return CheckoutHandler.Map(b); } catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return Invalid(ex.Message); } }
    internal static async Task<CareHomeBooking?> Owned(ICareHomesDbContext db, UserId actor, Guid id, CancellationToken ct) { var f = await db.Facilities.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == actor, ct); return f is null ? null : await db.Bookings.SingleOrDefaultAsync(x => x.Id == id && x.FacilityId == f.Id, ct); }
    private static Result<CareHomeBookingResponse> Missing() => Result<CareHomeBookingResponse>.Failure(new("CareHomes.Bookings.NotFound", "Booking was not found."));
    private static Result<CareHomeBookingResponse> Invalid(string m) => Result<CareHomeBookingResponse>.Failure(new("CareHomes.Bookings.InvalidState", m));
}

public sealed class OwnerCareHomeBookingOperationalHandler(ICareHomesDbContext db) : IQueryHandler<GetOwnerCareHomeBookingOperationalQuery, CareHomeOwnerBookingOperationalResponse>
{
    public async Task<Result<CareHomeOwnerBookingOperationalResponse>> Handle(GetOwnerCareHomeBookingOperationalQuery r, CancellationToken ct)
    {
        var facility = await db.Facilities.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == r.Actor, ct);
        var booking = facility is null ? null : await db.Bookings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.BookingId && x.FacilityId == facility.Id, ct);
        if (booking is null) return Result<CareHomeOwnerBookingOperationalResponse>.Failure(new("CareHomes.Bookings.NotFound", "Booking was not found."));
        return new CareHomeOwnerBookingOperationalResponse(booking.Id, booking.FacilityId.Value, booking.RoomTypeId, booking.AssignedRoomId, booking.AssignedBedId, booking.ActualCheckInOnUtc, booking.ActualCheckInRecordedBy?.Value, booking.ActualCheckOutOnUtc, booking.ActualCheckOutRecordedBy?.Value, booking.FamilyCheckInConfirmedOnUtc, booking.FamilyCheckInConfirmedBy?.Value);
    }
}

public sealed class RecordCareHomeCheckOutHandler(ICareHomesDbContext db) : ICommandHandler<RecordCareHomeCheckOutCommand, CareHomeBookingResponse>
{
    public async Task<Result<CareHomeBookingResponse>> Handle(RecordCareHomeCheckOutCommand r, CancellationToken ct)
    {
        var b = await RecordCareHomeCheckInHandler.Owned(db, r.Actor, r.BookingId, ct); if (b is null) return Result<CareHomeBookingResponse>.Failure(new("CareHomes.Bookings.NotFound", "Booking was not found."));
        try { b.RecordCheckOut(r.Actor, r.UtcNow); if (b.FamilyCheckInConfirmedOnUtc is null && !await db.CheckInDisputes.AnyAsync(x => x.BookingId == b.Id && x.Status == CareHomeCheckInDisputeStatus.Open, ct)) db.CheckInDisputes.Add(CareHomeCheckInDispute.Open(b.Id, b.FacilityId, r.UtcNow, r.Actor, r.UtcNow)); await db.SaveChangesAsync(ct); return CheckoutHandler.Map(b); }
        catch (DbUpdateException) { return Result<CareHomeBookingResponse>.Failure(new("CareHomes.CheckInDispute.OpenConflict", "A check-in dispute was opened concurrently for this booking.")); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return Result<CareHomeBookingResponse>.Failure(new("CareHomes.Bookings.InvalidState", ex.Message)); }
    }
}

public sealed class ConfirmCareHomeCheckInHandler(ICareHomesDbContext db) : ICommandHandler<ConfirmCareHomeCheckInCommand, CareHomeBookingResponse>
{
    public async Task<Result<CareHomeBookingResponse>> Handle(ConfirmCareHomeCheckInCommand r, CancellationToken ct)
    { var b = await db.Bookings.SingleOrDefaultAsync(x => x.Id == r.BookingId && x.FamilyId == r.FamilyId, ct); if (b is null) return Result<CareHomeBookingResponse>.Failure(new("CareHomes.Bookings.NotFound", "Booking was not found.")); try { b.ConfirmFamilyCheckIn(r.Actor, r.UtcNow); await db.SaveChangesAsync(ct); return CheckoutHandler.Map(b); } catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return Result<CareHomeBookingResponse>.Failure(new("CareHomes.Bookings.InvalidState", ex.Message)); } }
}

public sealed class CheckInDisputeHandlers(ICareHomesDbContext db) : IQueryHandler<ListCareHomeCheckInDisputesQuery, IReadOnlyList<CareHomeCheckInDisputeResponse>>, ICommandHandler<ResolveCareHomeCheckInDisputeCommand, CareHomeCheckInDisputeResponse>, ICommandHandler<SubmitCareHomeCheckInDisputeCommand, CareHomeCheckInDisputeResponse>
{
    public async Task<Result<IReadOnlyList<CareHomeCheckInDisputeResponse>>> Handle(ListCareHomeCheckInDisputesQuery r, CancellationToken ct) => (await db.CheckInDisputes.AsNoTracking().OrderByDescending(x => x.OpenedOnUtc).ToListAsync(ct)).Select(Map).ToArray();
    public async Task<Result<CareHomeCheckInDisputeResponse>> Handle(ResolveCareHomeCheckInDisputeCommand r, CancellationToken ct) { var c = await db.CheckInDisputes.SingleOrDefaultAsync(x => x.Id == r.CaseId, ct); if (c is null) return Result<CareHomeCheckInDisputeResponse>.Failure(new("CareHomes.CheckInDispute.NotFound", "Check-in dispute was not found.")); try { c.Resolve(r.Actor, r.EffectiveCheckInOnUtc, r.Evidence, r.Reason, r.UtcNow); await db.SaveChangesAsync(ct); return Map(c); } catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return Result<CareHomeCheckInDisputeResponse>.Failure(new("CareHomes.CheckInDispute.InvalidState", ex.Message)); } }
    public async Task<Result<CareHomeCheckInDisputeResponse>> Handle(SubmitCareHomeCheckInDisputeCommand r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Trim().Length > 2000)
            return Result<CareHomeCheckInDisputeResponse>.Failure(new("CareHomes.CheckInDispute.InvalidReason", "A dispute reason between 1 and 2000 characters is required."));
        var booking = await db.Bookings.SingleOrDefaultAsync(x => x.Id == r.BookingId && x.FamilyId == r.FamilyId, ct);
        if (booking is null) return Result<CareHomeCheckInDisputeResponse>.Failure(new("CareHomes.CheckInDispute.NotFound", "Check-in dispute was not found."));
        var existing = await db.CheckInDisputes.SingleOrDefaultAsync(x => x.BookingId == r.BookingId && x.Status == CareHomeCheckInDisputeStatus.Open, ct);
        if (existing is not null) return await AttachReasonAndReadAsync(existing, r.Reason, ct);
        if (booking.ActualCheckInOnUtc is null) return Result<CareHomeCheckInDisputeResponse>.Failure(new("CareHomes.CheckInDispute.InvalidState", "A facility-recorded check-in is required."));
        CareHomeCheckInDispute? created = null;
        try
        {
            created = CareHomeCheckInDispute.Open(booking.Id, booking.FacilityId, booking.ActualCheckOutOnUtc, r.Actor, r.UtcNow, r.Reason);
            db.CheckInDisputes.Add(created);
            await db.SaveChangesAsync(ct);
            return Map(created);
        }
        catch (DbUpdateException)
        {
            if (created is not null) db.CheckInDisputes.Remove(created);
            var raced = await db.CheckInDisputes.AsNoTracking().SingleOrDefaultAsync(x => x.BookingId == r.BookingId && x.Status == CareHomeCheckInDisputeStatus.Open, ct);
            if (raced is not null)
            {
                if (raced.FamilyReason is null)
                {
                    db.CheckInDisputes.Attach(raced);
                    return await AttachReasonAndReadAsync(raced, r.Reason, ct);
                }
                return Map(raced);
            }
            throw;
        }
    }
    private async Task<Result<CareHomeCheckInDisputeResponse>> AttachReasonAndReadAsync(CareHomeCheckInDispute dispute, string reason, CancellationToken ct)
    {
        if (dispute.FamilyReason is null) await SaveFamilyReasonAsync(dispute, reason, ct);
        var persisted = await db.CheckInDisputes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == dispute.Id, ct);
        return persisted?.FamilyReason is null
            ? Result<CareHomeCheckInDisputeResponse>.Failure(new("CareHomes.CheckInDispute.OpenConflict", "The dispute reason could not be persisted."))
            : Map(persisted);
    }
    private async Task SaveFamilyReasonAsync(CareHomeCheckInDispute dispute, string reason, CancellationToken ct)
    {
        dispute.AttachFamilyReason(reason);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.CheckInDisputes.Entry(dispute).State = EntityState.Detached;
            var reloaded = await db.CheckInDisputes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == dispute.Id, ct);
            if (reloaded is null || reloaded.FamilyReason is not null) return;
            db.CheckInDisputes.Attach(reloaded);
            reloaded.AttachFamilyReason(reason);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { db.CheckInDisputes.Entry(reloaded).State = EntityState.Detached; }
        }
    }
    private static CareHomeCheckInDisputeResponse Map(CareHomeCheckInDispute x) => new(x.Id, x.BookingId, x.FacilityId.Value, x.CheckoutOnUtc, x.Status, x.OpenedOnUtc, x.OpenedBy.Value, x.EffectiveCheckInOnUtc, x.Evidence, x.Reason, x.FamilyReason, x.ResolvedOnUtc, x.ResolvedBy?.Value);
}
