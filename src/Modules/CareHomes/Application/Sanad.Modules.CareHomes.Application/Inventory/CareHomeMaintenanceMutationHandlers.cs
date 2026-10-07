using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Application.Inventory;

public sealed class AmendMaintenanceBlockCommandHandler(ICareHomesDbContext db, ICareHomeOccupancyProvider occupancy) : ICommandHandler<AmendMaintenanceBlockCommand, CareHomeInventoryDto>
{
    public async Task<Result<CareHomeInventoryDto>> Handle(AmendMaintenanceBlockCommand r, CancellationToken ct)
    {
        var (facility, failure) = await InventoryMutation.Load(db, r.Actor, r.ExpectedVersion, ct); if (failure is not null) return failure;
        var ownerFacility = facility!;
        var block = await db.MaintenanceBlocks.SingleOrDefaultAsync(x => x.Id == r.MaintenanceId && x.FacilityId == ownerFacility.Id, ct);
        if (block is null) return Result<CareHomeInventoryDto>.Failure(new("CareHomes.Inventory.NotFound", "Maintenance block was not found."));
        DateOnly today = CairoToday(r.UtcNow);
        if (block.IsCancelled || block.StartDate <= today) return Result<CareHomeInventoryDto>.Failure(new("CareHomes.Inventory.InvalidState", "Only future maintenance blocks can be amended."));
        if (r.EndDate <= r.StartDate) return Result<CareHomeInventoryDto>.Failure(new("CareHomes.Inventory.InvalidRange", "End date must be after start date."));
        if (r.StartDate <= today) return Result<CareHomeInventoryDto>.Failure(new("CareHomes.Inventory.InvalidRange", "An amended maintenance block must still start in the future."));
        var active = await occupancy.GetActiveAsync(ownerFacility.Id, ct);
        var targetRoom = block.Target == CareHomeMaintenanceTarget.Room
            ? await db.Rooms.Where(x => x.Id == block.TargetId && x.FacilityId == ownerFacility.Id && !x.IsArchived).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct)
            : await db.Beds.Where(x => x.Id == block.TargetId && x.FacilityId == ownerFacility.Id && !x.IsArchived).Select(x => (Guid?)x.RoomId).SingleOrDefaultAsync(ct);
        if (targetRoom is null) return Result<CareHomeInventoryDto>.Failure(new("CareHomes.Inventory.InvalidTarget", "The maintenance target is no longer active in this facility."));
        var bedIds = await db.Beds.Where(x => x.RoomId == targetRoom.Value && x.FacilityId == ownerFacility.Id && !x.IsArchived).Select(x => x.Id).ToListAsync(ct);
        if (active.Any(x => x.Overlaps(r.StartDate, r.EndDate) &&
            (block.Target == CareHomeMaintenanceTarget.Room
                ? x.ResourceKind == CareHomeResourceKind.Room && x.ResourceId == block.TargetId || x.ResourceKind == CareHomeResourceKind.Bed && bedIds.Contains(x.ResourceId)
                : x.ResourceKind == CareHomeResourceKind.Bed && x.ResourceId == block.TargetId || x.ResourceKind == CareHomeResourceKind.Room && x.ResourceId == targetRoom.Value)))
            return Result<CareHomeInventoryDto>.Failure(new("CareHomes.Inventory.ActiveOccupancy", "Maintenance cannot overlap an active hold or stay."));
        var conflicts = await db.MaintenanceBlocks.Where(x => x.FacilityId == ownerFacility.Id && x.Id != block.Id && x.CancelledOnUtc == null && x.StartDate < r.EndDate && x.EndDate > r.StartDate).ToListAsync(ct);
        if (conflicts.Any(x => x.Target == block.Target && x.TargetId == block.TargetId || block.Target == CareHomeMaintenanceTarget.Room && x.Target == CareHomeMaintenanceTarget.Bed && bedIds.Contains(x.TargetId) || block.Target == CareHomeMaintenanceTarget.Bed && x.Target == CareHomeMaintenanceTarget.Room && x.TargetId == targetRoom)) return Result<CareHomeInventoryDto>.Failure(new("CareHomes.Inventory.Overlap", "Maintenance blocks cannot overlap on a room or its beds."));
        try { block.Amend(r.StartDate, r.EndDate, r.Reason, r.UtcNow); return await InventoryMutation.Save(db, ownerFacility, ct); }
        catch (ArgumentException ex) { return Result<CareHomeInventoryDto>.Failure(new("CareHomes.Inventory.Invalid", ex.Message)); }
    }
    private static DateOnly CairoToday(DateTime utcNow) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), CairoZone()));
    private static TimeZoneInfo CairoZone() { try { return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); } catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); } }
}

public sealed class CancelMaintenanceBlockCommandHandler(ICareHomesDbContext db) : ICommandHandler<CancelMaintenanceBlockCommand, CareHomeInventoryDto>
{
    public async Task<Result<CareHomeInventoryDto>> Handle(CancelMaintenanceBlockCommand r, CancellationToken ct)
    {
        var (facility, failure) = await InventoryMutation.Load(db, r.Actor, r.ExpectedVersion, ct); if (failure is not null) return failure;
        var ownerFacility = facility!;
        var block = await db.MaintenanceBlocks.SingleOrDefaultAsync(x => x.Id == r.MaintenanceId && x.FacilityId == ownerFacility.Id, ct);
        if (block is null) return Result<CareHomeInventoryDto>.Failure(new("CareHomes.Inventory.NotFound", "Maintenance block was not found."));
        if (block.IsCancelled) return await InventoryHandlerSupport.Map(db, ownerFacility.Id, ct);
        if (block.StartDate <= DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(r.UtcNow, DateTimeKind.Utc), CairoZone()))) return Result<CareHomeInventoryDto>.Failure(new("CareHomes.Inventory.InvalidState", "Only future maintenance blocks can be cancelled."));
        block.Cancel(r.Actor, r.UtcNow); return await InventoryMutation.Save(db, ownerFacility, ct);
    }
    private static TimeZoneInfo CairoZone() { try { return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); } catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); } }
}
