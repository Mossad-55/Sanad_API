using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Inventory;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.UnitTests.CareHomes;

public sealed class CareHomeInventoryTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void InventoryAssets_KeepStableIds_AndArchiveWithoutDestroyingThem()
    {
        CareHomeId facility = CareHomeId.New();
        CareHomeRoomType type = CareHomeRoomType.Create(facility, "مشترك", "Shared", 12000m, CareHomeAllocationMode.Shared, Now);
        CareHomeRoom room = CareHomeRoom.Create(facility, type.Id, "101", Now);
        CareHomeBed bed = CareHomeBed.Create(facility, room.Id, "A", Now);

        Guid typeId = type.Id;
        Guid roomId = room.Id;
        Guid bedId = bed.Id;
        type.Archive(Now.AddMinutes(1));
        room.Archive(Now.AddMinutes(1));
        bed.Archive(Now.AddMinutes(1));

        Assert.Equal(typeId, type.Id);
        Assert.Equal(roomId, room.Id);
        Assert.Equal(bedId, bed.Id);
        Assert.True(type.IsArchived && room.IsArchived && bed.IsArchived);
        type.Restore(Now.AddMinutes(2));
        room.Restore(Now.AddMinutes(2));
        bed.Restore(Now.AddMinutes(2));
        Assert.False(type.IsArchived || room.IsArchived || bed.IsArchived);
    }

    [Theory]
    [InlineData(CareHomeAllocationMode.Shared)]
    [InlineData(CareHomeAllocationMode.Private)]
    [InlineData(CareHomeAllocationMode.Suite)]
    public void RoomType_SupportsAllApprovedAllocationModes(CareHomeAllocationMode mode)
    {
        CareHomeRoomType type = CareHomeRoomType.Create(CareHomeId.New(), "غرفة", "Room", 100m, mode, Now);
        Assert.Equal(mode, type.AllocationMode);
    }

    [Fact]
    public void RoomRename_RequiresNoActiveHoldOrStay_AndBedRenameIsIndependent()
    {
        CareHomeRoom room = CareHomeRoom.Create(CareHomeId.New(), Guid.NewGuid(), "101", Now);
        Assert.Throws<InvalidOperationException>(() => room.Rename("102", true, Now.AddMinutes(1)));
        room.Rename("102", false, Now.AddMinutes(1));
        Assert.Equal("102", room.RoomNumber);

        CareHomeBed bed = CareHomeBed.Create(CareHomeId.New(), room.Id, "A", Now);
        bed.Rename("B", Now.AddMinutes(1));
        Assert.Equal("B", bed.Label);
    }

    [Fact]
    public void Maintenance_UsesStartInclusiveEndExclusiveBoundaries()
    {
        CareHomeMaintenanceBlock block = CareHomeMaintenanceBlock.Create(
            CareHomeId.New(), CareHomeMaintenanceTarget.Room, Guid.NewGuid(),
            new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12), "repair", Now);

        Assert.True(block.Overlaps(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 11)));
        Assert.True(block.Overlaps(new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 13)));
        Assert.False(block.Overlaps(new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 14)));
        Assert.False(block.Overlaps(new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 10)));
        Assert.Throws<ArgumentException>(() => CareHomeMaintenanceBlock.Create(
            CareHomeId.New(), CareHomeMaintenanceTarget.Bed, Guid.NewGuid(),
            new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 12), null, Now));
    }

    [Fact]
    public void Availability_ConsumesRoomAndBedOccupancyAndMaintenanceByAllocationMode()
    {
        CareHomeId facility = CareHomeId.New();
        CareHomeRoomType shared = CareHomeRoomType.Create(facility, "مشترك", "Shared", 100m, CareHomeAllocationMode.Shared, Now);
        CareHomeRoomType privateRoom = CareHomeRoomType.Create(facility, "خاص", "Private", 200m, CareHomeAllocationMode.Private, Now);
        CareHomeRoom sharedRoom = CareHomeRoom.Create(facility, shared.Id, "101", Now);
        CareHomeRoom privateRoomAsset = CareHomeRoom.Create(facility, privateRoom.Id, "201", Now);
        CareHomeBed sharedBedA = CareHomeBed.Create(facility, sharedRoom.Id, "A", Now);
        CareHomeBed sharedBedB = CareHomeBed.Create(facility, sharedRoom.Id, "B", Now);
        CareHomeBed privateBed = CareHomeBed.Create(facility, privateRoomAsset.Id, "A", Now);
        DateOnly start = new(2026, 10, 10);
        DateOnly end = new(2026, 10, 12);

        IReadOnlyList<CareHomeAvailability> result = CareHomeAvailabilityCalculator.Calculate(
            [shared, privateRoom], [sharedRoom, privateRoomAsset], [sharedBedA, sharedBedB, privateBed],
            [CareHomeMaintenanceBlock.Create(facility, CareHomeMaintenanceTarget.Bed, sharedBedB.Id, start, end, null, Now)],
            [new CareHomeOccupancyInterval(privateRoomAsset.Id, CareHomeResourceKind.Room, start, end),
             new CareHomeOccupancyInterval(sharedBedA.Id, CareHomeResourceKind.Bed, start, end)],
            [], start, end);

        CareHomeAvailability sharedAvailability = result.Single(x => x.RoomTypeId == shared.Id);
        CareHomeAvailability privateAvailability = result.Single(x => x.RoomTypeId == privateRoom.Id);
        Assert.Equal(1, sharedAvailability.AvailableRooms);
        Assert.Equal(0, sharedAvailability.AvailableBeds);
        Assert.Equal(0, privateAvailability.AvailableRooms);
        Assert.Equal(0, privateAvailability.AvailableBeds);

        IReadOnlyList<CareHomeAvailability> adjacent = CareHomeAvailabilityCalculator.Calculate(
            [shared], [sharedRoom], [sharedBedA, sharedBedB], [], [], [], end, new DateOnly(2026, 10, 14));
        Assert.Equal(1, adjacent.Single().AvailableRooms);
        Assert.Equal(2, adjacent.Single().AvailableBeds);
    }

    [Fact]
    public void Availability_RejectsInvalidDateRange()
    {
        Assert.Throws<ArgumentException>(() => CareHomeAvailabilityCalculator.Calculate(
            [], [], [], [], [], [], new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 12)));
    }
}
