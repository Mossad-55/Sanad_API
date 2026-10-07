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

    [Fact]
    public async Task Maintenance_can_be_amended_or_cancelled_only_before_its_future_start()
    {
        await using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = await AddFacility(db, owner);
        CareHomeRoomType type = CareHomeRoomType.Create(facility.Id, "خاص", "Private", 100m, CareHomeAllocationMode.Private, Now);
        CareHomeRoom room = CareHomeRoom.Create(facility.Id, type.Id, "401", Now);
        db.RoomTypes.Add(type); db.Rooms.Add(room); await db.SaveChangesAsync();
        var today = new DateOnly(2026, 10, 6);
        var now = CairoNoonUtc(today);
        var create = new CreateMaintenanceBlockCommandHandler(db, new FixedOccupancy());
        var future = await create.Handle(new(owner, facility.Version, CareHomeMaintenanceTarget.Room, room.Id, today.AddDays(10), today.AddDays(12), "planned work"), default);
        Assert.True(future.IsSuccess);

        var amendedStart = today.AddDays(11);
        var amended = await new AmendMaintenanceBlockCommandHandler(db, new FixedOccupancy()).Handle(
            new(owner, future.Value.Version, future.Value.Maintenance.Single().Id, amendedStart, today.AddDays(14), "updated plan", now), default);
        Assert.True(amended.IsSuccess);
        var block = amended.Value.Maintenance.Single();
        Assert.Equal(amendedStart, block.StartDate);
        Assert.Equal(today.AddDays(14), block.EndDate);
        Assert.Equal("updated plan", block.Reason);

        var cancelled = await new CancelMaintenanceBlockCommandHandler(db).Handle(
            new(owner, amended.Value.Version, block.Id, now), default);
        Assert.True(cancelled.IsSuccess);
        var retained = cancelled.Value.Maintenance.Single();
        Assert.True(retained.IsCancelled);
        Assert.NotNull(retained.CancelledOnUtc);
        Assert.Equal(owner.Value, retained.CancelledBy);

        var startsToday = await create.Handle(new(owner, cancelled.Value.Version, CareHomeMaintenanceTarget.Room, room.Id, today, today.AddDays(2), "active today"), default);
        Assert.True(startsToday.IsSuccess);
        var immutableAmend = await new AmendMaintenanceBlockCommandHandler(db, new FixedOccupancy()).Handle(
            new(owner, startsToday.Value.Version, startsToday.Value.Maintenance.Single(x => !x.IsCancelled).Id, today, today.AddDays(3), "no", now), default);
        Assert.False(immutableAmend.IsSuccess);
        Assert.Equal("CareHomes.Inventory.InvalidState", immutableAmend.Error.Code);
        var immutableCancel = await new CancelMaintenanceBlockCommandHandler(db).Handle(
            new(owner, startsToday.Value.Version, startsToday.Value.Maintenance.Single(x => !x.IsCancelled).Id, now), default);
        Assert.False(immutableCancel.IsSuccess);
        Assert.Equal("CareHomes.Inventory.InvalidState", immutableCancel.Error.Code);
    }

    [Fact]
    public async Task Maintenance_amend_rechecks_version_active_occupancy_and_room_overlap()
    {
        await using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = await AddFacility(db, owner);
        CareHomeRoomType type = CareHomeRoomType.Create(facility.Id, "خاص", "Private", 100m, CareHomeAllocationMode.Private, Now);
        CareHomeRoom firstRoom = CareHomeRoom.Create(facility.Id, type.Id, "501", Now);
        db.RoomTypes.Add(type); db.Rooms.Add(firstRoom); await db.SaveChangesAsync();
        var today = new DateOnly(2026, 10, 6);
        var now = CairoNoonUtc(today);
        var create = new CreateMaintenanceBlockCommandHandler(db, new FixedOccupancy());
        var first = await create.Handle(new(owner, facility.Version, CareHomeMaintenanceTarget.Room, firstRoom.Id, today.AddDays(10), today.AddDays(12), "first"), default);
        Assert.True(first.IsSuccess);
        var second = await create.Handle(new(owner, first.Value.Version, CareHomeMaintenanceTarget.Room, firstRoom.Id, today.AddDays(20), today.AddDays(22), "second"), default);
        Assert.True(second.IsSuccess);
        var blockId = second.Value.Maintenance.Single(x => x.TargetId == firstRoom.Id && x.StartDate == today.AddDays(20)).Id;
        var amend = new AmendMaintenanceBlockCommandHandler(db, new FixedOccupancy());

        var stale = await amend.Handle(new(owner, first.Value.Version, blockId, today.AddDays(23), today.AddDays(25), "stale", now), default);
        Assert.False(stale.IsSuccess);
        Assert.Equal("CareHomes.Inventory.Conflict", stale.Error.Code);

        var overlaps = await amend.Handle(new(owner, second.Value.Version, blockId, today.AddDays(11), today.AddDays(13), "overlap", now), default);
        Assert.False(overlaps.IsSuccess);
        Assert.Equal("CareHomes.Inventory.Overlap", overlaps.Error.Code);

        var occupied = await new AmendMaintenanceBlockCommandHandler(db,
            new FixedOccupancy(new Sanad.Modules.CareHomes.Application.Inventory.CareHomeOccupancyInterval(firstRoom.Id, CareHomeResourceKind.Room, today.AddDays(30), today.AddDays(32))))
            .Handle(new(owner, second.Value.Version, blockId, today.AddDays(30), today.AddDays(32), "occupied", now), default);
        Assert.False(occupied.IsSuccess);
        Assert.Equal("CareHomes.Inventory.ActiveOccupancy", occupied.Error.Code);
    }

    [Fact]
    public async Task Maintenance_amend_and_cancel_use_the_Cairo_local_date_at_midnight_boundary()
    {
        await using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = await AddFacility(db, owner);
        CareHomeRoomType type = CareHomeRoomType.Create(facility.Id, "Ø®Ø§Øµ", "Private", 100m, CareHomeAllocationMode.Private, Now);
        CareHomeRoom room = CareHomeRoom.Create(facility.Id, type.Id, "601", Now);
        db.RoomTypes.Add(type); db.Rooms.Add(room); await db.SaveChangesAsync();

        var today = new DateOnly(2026, 10, 6);
        var tomorrow = today.AddDays(1);
        var create = new CreateMaintenanceBlockCommandHandler(db, new FixedOccupancy());
        var created = await create.Handle(new(owner, facility.Version, CareHomeMaintenanceTarget.Room, room.Id, tomorrow, tomorrow.AddDays(2), "planned"), default);
        Assert.True(created.IsSuccess);
        var blockId = created.Value.Maintenance.Single().Id;
        var amend = new AmendMaintenanceBlockCommandHandler(db, new FixedOccupancy());
        var beforeMidnight = await amend.Handle(new(owner, created.Value.Version, blockId, tomorrow, tomorrow.AddDays(3), "still future", CairoUtcAt(today, 23, 59, 59)), default);
        Assert.True(beforeMidnight.IsSuccess);

        var atMidnight = CairoUtcAt(tomorrow, 0, 0, 0);
        var amendOnStartDate = await amend.Handle(new(owner, beforeMidnight.Value.Version, blockId, tomorrow, tomorrow.AddDays(4), "too late", atMidnight), default);
        Assert.False(amendOnStartDate.IsSuccess);
        Assert.Equal("CareHomes.Inventory.InvalidState", amendOnStartDate.Error.Code);

        var cancelOnStartDate = await new CancelMaintenanceBlockCommandHandler(db).Handle(new(owner, beforeMidnight.Value.Version, blockId, atMidnight), default);
        Assert.False(cancelOnStartDate.IsSuccess);
        Assert.Equal("CareHomes.Inventory.InvalidState", cancelOnStartDate.Error.Code);
    }

    private static async Task<CareHomeFacility> AddFacility(CareHomesDbContext db, UserId owner)
    {
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, Now);
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        return facility;
    }

    private static CareHomesDbContext CreateDb() => new(new DbContextOptionsBuilder<CareHomesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DateTime CairoNoonUtc(DateOnly date) => CairoUtcAt(date, 12, 0, 0);

    private static DateTime CairoUtcAt(DateOnly date, int hour, int minute, int second)
    {
        TimeZoneInfo cairo;
        try { cairo = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); }
        catch (TimeZoneNotFoundException) { cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); }
        DateTime local = date.ToDateTime(new TimeOnly(hour, minute, second), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, cairo);
    }

    private sealed class FixedOccupancy(params CareHomeOccupancyInterval[] intervals) : ICareHomeOccupancyProvider
    {
        public Task<IReadOnlyList<CareHomeOccupancyInterval>> GetActiveAsync(CareHomeId facilityId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CareHomeOccupancyInterval>>(intervals);
    }
}
