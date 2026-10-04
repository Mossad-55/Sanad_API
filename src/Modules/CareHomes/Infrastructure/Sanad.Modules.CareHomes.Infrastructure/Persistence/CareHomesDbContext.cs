using Microsoft.EntityFrameworkCore;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Domain.Bookings;

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence;

public sealed class CareHomesDbContext(DbContextOptions<CareHomesDbContext> options)
    : DbContext(options), ICareHomesDbContext
{
    public const string Schema = "care_homes";

    public DbSet<CareHomeFacility> Facilities => Set<CareHomeFacility>();
    public DbSet<CareHomeRoomType> RoomTypes => Set<CareHomeRoomType>();
    public DbSet<CareHomeRoom> Rooms => Set<CareHomeRoom>();
    public DbSet<CareHomeBed> Beds => Set<CareHomeBed>();
    public DbSet<CareHomeMaintenanceBlock> MaintenanceBlocks => Set<CareHomeMaintenanceBlock>();
    public DbSet<CareHomeBooking> Bookings => Set<CareHomeBooking>();
    public DbSet<CareHomeCheckInDispute> CheckInDisputes => Set<CareHomeCheckInDispute>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CareHomesDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
