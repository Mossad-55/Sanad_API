using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Application.Inventory;

public sealed record CareHomeRoomTypeDto(Guid Id, Guid FacilityId, string ArabicName, string EnglishName, decimal MonthlyPriceEgp, CareHomeAllocationMode AllocationMode, bool IsArchived);
public sealed record CareHomeRoomDto(Guid Id, Guid FacilityId, Guid RoomTypeId, string RoomNumber, bool IsArchived);
public sealed record CareHomeBedDto(Guid Id, Guid FacilityId, Guid RoomId, string Label, bool IsArchived);
public sealed record CareHomeMaintenanceDto(Guid Id, Guid FacilityId, CareHomeMaintenanceTarget Target, Guid TargetId, DateOnly StartDate, DateOnly EndDate, string? Reason);
public sealed record CareHomeInventoryDto(Guid FacilityId, int Version, IReadOnlyList<CareHomeRoomTypeDto> RoomTypes, IReadOnlyList<CareHomeRoomDto> Rooms, IReadOnlyList<CareHomeBedDto> Beds, IReadOnlyList<CareHomeMaintenanceDto> Maintenance);

public sealed record CreateRoomTypeCommand(UserId Actor, int ExpectedVersion, string ArabicName, string EnglishName, decimal MonthlyPriceEgp, CareHomeAllocationMode AllocationMode, string? ArabicDescription, string? EnglishDescription) : ICommand<CareHomeInventoryDto>;
public sealed record UpdateRoomTypeCommand(UserId Actor, int ExpectedVersion, Guid RoomTypeId, string ArabicName, string EnglishName, decimal MonthlyPriceEgp, CareHomeAllocationMode AllocationMode, string? ArabicDescription, string? EnglishDescription) : ICommand<CareHomeInventoryDto>;
public sealed record ArchiveRoomTypeCommand(UserId Actor, int ExpectedVersion, Guid RoomTypeId, bool Restore) : ICommand<CareHomeInventoryDto>;
public sealed record CreateRoomCommand(UserId Actor, int ExpectedVersion, Guid RoomTypeId, string RoomNumber) : ICommand<CareHomeInventoryDto>;
public sealed record RenameRoomCommand(UserId Actor, int ExpectedVersion, Guid RoomId, string RoomNumber) : ICommand<CareHomeInventoryDto>;
public sealed record ArchiveRoomCommand(UserId Actor, int ExpectedVersion, Guid RoomId, bool Restore) : ICommand<CareHomeInventoryDto>;
public sealed record CreateBedCommand(UserId Actor, int ExpectedVersion, Guid RoomId, string Label) : ICommand<CareHomeInventoryDto>;
public sealed record RenameBedCommand(UserId Actor, int ExpectedVersion, Guid BedId, string Label) : ICommand<CareHomeInventoryDto>;
public sealed record ArchiveBedCommand(UserId Actor, int ExpectedVersion, Guid BedId, bool Restore) : ICommand<CareHomeInventoryDto>;
public sealed record CreateMaintenanceBlockCommand(UserId Actor, int ExpectedVersion, CareHomeMaintenanceTarget Target, Guid TargetId, DateOnly StartDate, DateOnly EndDate, string? Reason) : ICommand<CareHomeInventoryDto>;
public sealed record GetMyInventoryQuery(UserId Actor) : IQuery<CareHomeInventoryDto>;
public sealed record GetAdminInventoryQuery(Guid FacilityId) : IQuery<CareHomeInventoryDto>;
public sealed record GetAvailabilityQuery(Guid FacilityId, DateOnly StartDate, DateOnly EndDate) : IQuery<IReadOnlyList<CareHomeAvailability>>;
public sealed record GetMyAvailabilityQuery(UserId Actor, DateOnly StartDate, DateOnly EndDate) : IQuery<IReadOnlyList<CareHomeAvailability>>;

internal static class InventoryHandlerSupport
{
    internal static async Task<CareHomeFacility?> Facility(ICareHomesDbContext db, UserId actor, CancellationToken ct) => await db.Facilities.SingleOrDefaultAsync(x => x.OwnerUserId == actor, ct);
    internal static async Task<CareHomeInventoryDto> Map(ICareHomesDbContext db, CareHomeId facilityId, CancellationToken ct)
    {
        var types = await db.RoomTypes.Where(x => x.FacilityId == facilityId).AsNoTracking().ToListAsync(ct);
        var rooms = await db.Rooms.Where(x => x.FacilityId == facilityId).AsNoTracking().ToListAsync(ct);
        var beds = await db.Beds.Where(x => x.FacilityId == facilityId).AsNoTracking().ToListAsync(ct);
        var maintenance = await db.MaintenanceBlocks.Where(x => x.FacilityId == facilityId).AsNoTracking().ToListAsync(ct);
        var facility = await db.Facilities.AsNoTracking().SingleAsync(x => x.Id == facilityId, ct);
        return new(facilityId.Value, facility.Version, types.Select(x => new CareHomeRoomTypeDto(x.Id, x.FacilityId.Value, x.ArabicName, x.EnglishName, x.MonthlyPriceEgp, x.AllocationMode, x.IsArchived)).ToArray(), rooms.Select(x => new CareHomeRoomDto(x.Id, x.FacilityId.Value, x.RoomTypeId, x.RoomNumber, x.IsArchived)).ToArray(), beds.Select(x => new CareHomeBedDto(x.Id, x.FacilityId.Value, x.RoomId, x.Label, x.IsArchived)).ToArray(), maintenance.Select(x => new CareHomeMaintenanceDto(x.Id, x.FacilityId.Value, x.Target, x.TargetId, x.StartDate, x.EndDate, x.Reason)).ToArray());
    }
    internal static Error Error(string code, string message) => new(code, message);
}

public sealed class CreateRoomTypeCommandHandler(ICareHomesDbContext db) : ICommandHandler<CreateRoomTypeCommand, CareHomeInventoryDto>
{
    public async Task<Result<CareHomeInventoryDto>> Handle(CreateRoomTypeCommand r, CancellationToken ct) { var (f, fail) = await InventoryMutation.Load(db, r.Actor, r.ExpectedVersion, ct); if (fail is not null) return fail; try { var x = CareHomeRoomType.Create(f!.Id, r.ArabicName, r.EnglishName, r.MonthlyPriceEgp, r.AllocationMode, DateTime.UtcNow); x.Update(r.ArabicName, r.EnglishName, r.MonthlyPriceEgp, r.AllocationMode, r.ArabicDescription, r.EnglishDescription, DateTime.UtcNow); db.RoomTypes.Add(x); return await InventoryMutation.Save(db, f, ct); } catch (ArgumentException ex) { return Result<CareHomeInventoryDto>.Failure(InventoryHandlerSupport.Error("CareHomes.Inventory.Invalid", ex.Message)); } }
}

public sealed class GetMyInventoryQueryHandler(ICareHomesDbContext db) : IQueryHandler<GetMyInventoryQuery, CareHomeInventoryDto>
{ public async Task<Result<CareHomeInventoryDto>> Handle(GetMyInventoryQuery r, CancellationToken ct) { var f = await InventoryHandlerSupport.Facility(db, r.Actor, ct); return f is null ? Result<CareHomeInventoryDto>.Failure(InventoryHandlerSupport.Error("CareHomes.Inventory.NotFound", "Facility was not found.")) : await InventoryHandlerSupport.Map(db, f.Id, ct); } }

public sealed class GetAdminInventoryQueryHandler(ICareHomesDbContext db) : IQueryHandler<GetAdminInventoryQuery, CareHomeInventoryDto>
{ public async Task<Result<CareHomeInventoryDto>> Handle(GetAdminInventoryQuery r, CancellationToken ct) { var exists = await db.Facilities.AnyAsync(x => x.Id == new CareHomeId(r.FacilityId), ct); return !exists ? Result<CareHomeInventoryDto>.Failure(InventoryHandlerSupport.Error("CareHomes.Inventory.NotFound", "Facility was not found.")) : await InventoryHandlerSupport.Map(db, new CareHomeId(r.FacilityId), ct); } }

public sealed class GetAvailabilityQueryHandler(ICareHomesDbContext db, ICareHomeOccupancyProvider occupancy) : IQueryHandler<GetAvailabilityQuery, IReadOnlyList<CareHomeAvailability>>
{ public async Task<Result<IReadOnlyList<CareHomeAvailability>>> Handle(GetAvailabilityQuery r, CancellationToken ct) { if (r.EndDate <= r.StartDate) return Result<IReadOnlyList<CareHomeAvailability>>.Failure(InventoryHandlerSupport.Error("CareHomes.Inventory.InvalidRange", "End date must be after start date.")); var f = new CareHomeId(r.FacilityId); if (!await db.Facilities.AnyAsync(x => x.Id == f, ct)) return Result<IReadOnlyList<CareHomeAvailability>>.Failure(InventoryHandlerSupport.Error("CareHomes.Inventory.NotFound", "Facility was not found.")); var active = await occupancy.GetActiveAsync(f, ct); return Result<IReadOnlyList<CareHomeAvailability>>.Success(CareHomeAvailabilityCalculator.Calculate(await db.RoomTypes.Where(x => x.FacilityId == f).AsNoTracking().ToListAsync(ct), await db.Rooms.Where(x => x.FacilityId == f).AsNoTracking().ToListAsync(ct), await db.Beds.Where(x => x.FacilityId == f).AsNoTracking().ToListAsync(ct), await db.MaintenanceBlocks.Where(x => x.FacilityId == f).AsNoTracking().ToListAsync(ct), active, active, r.StartDate, r.EndDate)); } }
public sealed class GetMyAvailabilityQueryHandler(ICareHomesDbContext db, ICareHomeOccupancyProvider occupancy) : IQueryHandler<GetMyAvailabilityQuery, IReadOnlyList<CareHomeAvailability>>
{ public async Task<Result<IReadOnlyList<CareHomeAvailability>>> Handle(GetMyAvailabilityQuery r, CancellationToken ct) { var f = await InventoryHandlerSupport.Facility(db, r.Actor, ct); if (f is null) return Result<IReadOnlyList<CareHomeAvailability>>.Failure(InventoryHandlerSupport.Error("CareHomes.Inventory.NotFound", "Facility was not found.")); return await new GetAvailabilityQueryHandler(db, occupancy).Handle(new GetAvailabilityQuery(f.Id.Value, r.StartDate, r.EndDate), ct); } }
