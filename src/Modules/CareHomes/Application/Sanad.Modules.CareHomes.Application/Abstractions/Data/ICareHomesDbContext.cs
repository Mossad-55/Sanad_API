using Microsoft.EntityFrameworkCore;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Application.Abstractions.Data;

public interface ICareHomesDbContext
{
    DbSet<CareHomeFacility> Facilities { get; }
    DbSet<CareHomeRoomType> RoomTypes { get; }
    DbSet<CareHomeRoom> Rooms { get; }
    DbSet<CareHomeBed> Beds { get; }
    DbSet<CareHomeMaintenanceBlock> MaintenanceBlocks { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
