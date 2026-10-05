using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Application.Inventory;

public enum CareHomeResourceKind { Room = 1, Bed = 2, RoomType = 3 }
public sealed record CareHomeOccupancyInterval(Guid ResourceId, CareHomeResourceKind ResourceKind, DateOnly StartDate, DateOnly EndDate, int Quantity = 1)
{
    public bool Overlaps(DateOnly start, DateOnly end) => StartDate < end && EndDate > start;
}

public interface ICareHomeOccupancyProvider
{
    Task<IReadOnlyList<CareHomeOccupancyInterval>> GetActiveAsync(CareHomeId facilityId, CancellationToken cancellationToken);
}

public sealed class EmptyCareHomeOccupancyProvider : ICareHomeOccupancyProvider
{
    public Task<IReadOnlyList<CareHomeOccupancyInterval>> GetActiveAsync(CareHomeId facilityId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CareHomeOccupancyInterval>>([]);
}

public sealed record CareHomeAvailability(Guid RoomTypeId, int AvailableRooms, int AvailableBeds, int TotalRooms, int TotalBeds);

/// <summary>Pure availability calculation. Holds and stays are supplied by their owning booking slice.</summary>
public static class CareHomeAvailabilityCalculator
{
    public static IReadOnlyList<CareHomeAvailability> Calculate(
        IEnumerable<CareHomeRoomType> roomTypes,
        IEnumerable<CareHomeRoom> rooms,
        IEnumerable<CareHomeBed> beds,
        IEnumerable<CareHomeMaintenanceBlock> maintenance,
        IEnumerable<CareHomeOccupancyInterval> holds,
        IEnumerable<CareHomeOccupancyInterval> stays,
        DateOnly startDate,
        DateOnly endDate)
    {
        if (endDate <= startDate) throw new ArgumentException("Availability end date must be after start date.");
        var occupied = holds.Concat(stays).Where(x => x.Overlaps(startDate, endDate)).ToArray();
        var blockedRooms = maintenance.Where(x => x.Target == CareHomeMaintenanceTarget.Room && x.Overlaps(startDate, endDate)).Select(x => x.TargetId).ToHashSet();
        var blockedBeds = maintenance.Where(x => x.Target == CareHomeMaintenanceTarget.Bed && x.Overlaps(startDate, endDate)).Select(x => x.TargetId).ToHashSet();
        var occupiedRooms = occupied.Where(x => x.ResourceKind == CareHomeResourceKind.Room).Select(x => x.ResourceId).ToHashSet();
        var occupiedBeds = occupied.Where(x => x.ResourceKind == CareHomeResourceKind.Bed).Select(x => x.ResourceId).ToHashSet();
        var occupiedTypes = occupied.Where(x => x.ResourceKind == CareHomeResourceKind.RoomType)
            .GroupBy(x => x.ResourceId).ToDictionary(x => x.Key, x => x.Sum(item => item.Quantity));
        var roomList = rooms.Where(x => !x.IsArchived && !blockedRooms.Contains(x.Id) && !occupiedRooms.Contains(x.Id)).ToArray();
        var bedList = beds.Where(x => !x.IsArchived && !blockedBeds.Contains(x.Id) && !occupiedBeds.Contains(x.Id) && roomList.Any(r => r.Id == x.RoomId)).ToArray();
        return roomTypes.Where(x => !x.IsArchived).Select(type =>
        {
            var typeRooms = rooms.Where(x => x.RoomTypeId == type.Id && !x.IsArchived).ToArray();
            int typeHeld = occupiedTypes.GetValueOrDefault(type.Id);
            var freeRooms = Math.Max(0, roomList.Count(x => x.RoomTypeId == type.Id) -
                (type.AllocationMode is CareHomeAllocationMode.Private or CareHomeAllocationMode.Suite ? typeHeld : 0));
            var freeBeds = type.AllocationMode == CareHomeAllocationMode.Shared
                ? Math.Max(0, bedList.Count(x => typeRooms.Any(room => room.Id == x.RoomId)) - typeHeld)
                : 0;
            if (type.AllocationMode is CareHomeAllocationMode.Private or CareHomeAllocationMode.Suite)
                freeBeds = Math.Max(0, bedList.Count(x => typeRooms.Any(room => room.Id == x.RoomId)) - typeHeld);
            return new CareHomeAvailability(type.Id, freeRooms, freeBeds, typeRooms.Length, beds.Count(x => typeRooms.Any(room => room.Id == x.RoomId) && !x.IsArchived));
        }).ToArray();
    }
}
