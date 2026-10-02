using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Inventory;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;

namespace Sanad.UnitTests.CareHomes;

public sealed class CareHomeInventoryMutationTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Mutations_RejectStaleVersionAndDuplicateRoomAndBedIdentifiers()
    {
        await using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = await AddFacility(db, owner);
        CareHomeRoomType type = CareHomeRoomType.Create(facility.Id, "مشترك", "Shared", 100m, CareHomeAllocationMode.Shared, Now);
        db.RoomTypes.Add(type);
        await db.SaveChangesAsync();

        CreateRoomCommandHandler rooms = new(db);
        int initialVersion = facility.Version;
        var first = await rooms.Handle(new CreateRoomCommand(owner, initialVersion, type.Id, "101"), default);
        Assert.True(first.IsSuccess);
        var stale = await rooms.Handle(new CreateRoomCommand(owner, initialVersion, type.Id, "102"), default);
        Assert.False(stale.IsSuccess);
        Assert.Equal("CareHomes.Inventory.Conflict", stale.Error.Code);
        var duplicateRoom = await rooms.Handle(new CreateRoomCommand(owner, first.Value.Version, type.Id, "101"), default);
        Assert.False(duplicateRoom.IsSuccess);
        Assert.Equal("CareHomes.Inventory.RoomNumberTaken", duplicateRoom.Error.Code);

        CreateBedCommandHandler beds = new(db);
        var firstBed = await beds.Handle(new CreateBedCommand(owner, first.Value.Version, first.Value.Rooms.Single().Id, "A"), default);
        Assert.True(firstBed.IsSuccess);
        var duplicateBed = await beds.Handle(new CreateBedCommand(owner, firstBed.Value.Version, first.Value.Rooms.Single().Id, " A "), default);
        Assert.False(duplicateBed.IsSuccess);
        Assert.Equal("CareHomes.Inventory.LabelTaken", duplicateBed.Error.Code);
    }

    [Fact]
    public async Task ArchiveCommands_RestoreStableAssetsAndMaintenanceRejectsInvalidOrOverlappingTargets()
    {
        await using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = await AddFacility(db, owner);
        CareHomeRoomType type = CareHomeRoomType.Create(facility.Id, "خاص", "Private", 100m, CareHomeAllocationMode.Private, Now);
        CareHomeRoom room = CareHomeRoom.Create(facility.Id, type.Id, "201", Now);
        CareHomeBed bed = CareHomeBed.Create(facility.Id, room.Id, "A", Now);
        db.RoomTypes.Add(type); db.Rooms.Add(room); db.Beds.Add(bed);
        await db.SaveChangesAsync();

        var archived = await new ArchiveBedCommandHandler(db).Handle(new ArchiveBedCommand(owner, facility.Version, bed.Id, false), default);
        Assert.True(archived.IsSuccess);
        var restored = await new ArchiveBedCommandHandler(db).Handle(new ArchiveBedCommand(owner, archived.Value.Version, bed.Id, true), default);
        Assert.True(restored.IsSuccess);
        Assert.False(restored.Value.Beds.Single(x => x.Id == bed.Id).IsArchived);

        FixedOccupancy occupancy = new(new CareHomeOccupancyInterval(bed.Id, CareHomeResourceKind.Bed, new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12)));
        CreateMaintenanceBlockCommandHandler maintenance = new(db, occupancy);
        var invalidRange = await maintenance.Handle(new CreateMaintenanceBlockCommand(owner, restored.Value.Version, CareHomeMaintenanceTarget.Bed, bed.Id, new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 12), null), default);
        Assert.False(invalidRange.IsSuccess);
        Assert.Equal("CareHomes.Inventory.Invalid", invalidRange.Error.Code);
        var invalidTarget = await maintenance.Handle(new CreateMaintenanceBlockCommand(owner, restored.Value.Version, CareHomeMaintenanceTarget.Bed, Guid.NewGuid(), new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12), null), default);
        Assert.False(invalidTarget.IsSuccess);
        Assert.Equal("CareHomes.Inventory.InvalidTarget", invalidTarget.Error.Code);
        var occupied = await maintenance.Handle(new CreateMaintenanceBlockCommand(owner, restored.Value.Version, CareHomeMaintenanceTarget.Bed, bed.Id, new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 13), null), default);
        Assert.False(occupied.IsSuccess);
        Assert.Equal("CareHomes.Inventory.ActiveOccupancy", occupied.Error.Code);

        var first = await new CreateMaintenanceBlockCommandHandler(db, new FixedOccupancy()).Handle(new CreateMaintenanceBlockCommand(owner, restored.Value.Version, CareHomeMaintenanceTarget.Bed, bed.Id, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3), null), default);
        Assert.True(first.IsSuccess);
        var overlap = await new CreateMaintenanceBlockCommandHandler(db, new FixedOccupancy()).Handle(new CreateMaintenanceBlockCommand(owner, first.Value.Version, CareHomeMaintenanceTarget.Bed, bed.Id, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 4), null), default);
        Assert.False(overlap.IsSuccess);
        Assert.Equal("CareHomes.Inventory.Overlap", overlap.Error.Code);
    }

    [Fact]
    public async Task RoomRename_RejectsRoomOrContainedBedOccupancy_AndAllowsAdjacentMaintenanceIntervals()
    {
        await using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = await AddFacility(db, owner);
        CareHomeRoomType type = CareHomeRoomType.Create(facility.Id, "مشترك", "Shared", 100m, CareHomeAllocationMode.Shared, Now);
        CareHomeRoom room = CareHomeRoom.Create(facility.Id, type.Id, "301", Now);
        CareHomeBed bed = CareHomeBed.Create(facility.Id, room.Id, "A", Now);
        db.RoomTypes.Add(type); db.Rooms.Add(room); db.Beds.Add(bed); await db.SaveChangesAsync();

        var roomHeld = await new RenameRoomCommandHandler(db, new FixedOccupancy(new CareHomeOccupancyInterval(room.Id, CareHomeResourceKind.Room, new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12))))
            .Handle(new RenameRoomCommand(owner, facility.Version, room.Id, "302"), default);
        Assert.False(roomHeld.IsSuccess);
        Assert.Equal("CareHomes.Inventory.ActiveOccupancy", roomHeld.Error.Code);
        var bedHeld = await new RenameRoomCommandHandler(db, new FixedOccupancy(new CareHomeOccupancyInterval(bed.Id, CareHomeResourceKind.Bed, new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12))))
            .Handle(new RenameRoomCommand(owner, facility.Version, room.Id, "302"), default);
        Assert.False(bedHeld.IsSuccess);
        Assert.Equal("CareHomes.Inventory.ActiveOccupancy", bedHeld.Error.Code);
    }

    private static async Task<CareHomeFacility> AddFacility(CareHomesDbContext db, UserId owner)
    {
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, Now);
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        return facility;
    }

    private static CareHomesDbContext CreateDb() => new(new DbContextOptionsBuilder<CareHomesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FixedOccupancy(params CareHomeOccupancyInterval[] intervals) : ICareHomeOccupancyProvider
    {
        public Task<IReadOnlyList<CareHomeOccupancyInterval>> GetActiveAsync(CareHomeId facilityId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CareHomeOccupancyInterval>>(intervals);
    }
}
