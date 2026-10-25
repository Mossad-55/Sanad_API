using Microsoft.EntityFrameworkCore;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Idempotency;

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence;

public sealed class CareHomesDbContext(DbContextOptions<CareHomesDbContext> options)
    : DbContext(options), ICareHomesDbContext
{
    public const string Schema = "care_homes";

    public DbSet<CareHomeFacility> Facilities => Set<CareHomeFacility>();
    public DbSet<CareHomeRoomType> RoomTypes => Set<CareHomeRoomType>();
    public DbSet<CareHomeRoomTypeMedia> RoomTypeMedia => Set<CareHomeRoomTypeMedia>();
    public DbSet<CareHomeRoom> Rooms => Set<CareHomeRoom>();
    public DbSet<CareHomeRoomMedia> RoomMedia => Set<CareHomeRoomMedia>();
    public DbSet<CareHomeBed> Beds => Set<CareHomeBed>();
    public DbSet<CareHomeMaintenanceBlock> MaintenanceBlocks => Set<CareHomeMaintenanceBlock>();
    public DbSet<CareHomeBooking> Bookings => Set<CareHomeBooking>();
    public DbSet<CareHomeInternalBookingNote> InternalBookingNotes => Set<CareHomeInternalBookingNote>();
    public DbSet<CareHomeRating> Ratings => Set<CareHomeRating>();
    public DbSet<CareHomeRatingModerationAction> RatingModerationActions => Set<CareHomeRatingModerationAction>();
    public DbSet<CareHomeRatingFlag> RatingFlags => Set<CareHomeRatingFlag>();
    public DbSet<CareHomePayout> Payouts => Set<CareHomePayout>();
    public DbSet<CareHomePayoutDebt> PayoutDebts => Set<CareHomePayoutDebt>();
    public DbSet<CareHomeBookingAssignmentHistory> BookingAssignmentHistory => Set<CareHomeBookingAssignmentHistory>();
    public DbSet<CareHomeTransferNotificationOutbox> TransferNotificationOutbox => Set<CareHomeTransferNotificationOutbox>();
    public DbSet<CareHomeNotificationOutbox> BookingNotificationOutbox => Set<CareHomeNotificationOutbox>();
    public DbSet<CareHomeCheckInDispute> CheckInDisputes => Set<CareHomeCheckInDispute>();
    public DbSet<CareHomeProfileMedia> ProfileMedia => Set<CareHomeProfileMedia>();
    public DbSet<CareHomeVisitSettings> VisitSettings => Set<CareHomeVisitSettings>();
    public DbSet<CareHomeVisit> Visits => Set<CareHomeVisit>();
    public DbSet<IdempotencyRequest> IdempotencyRequests => Set<IdempotencyRequest>();

    public void DetachAdded<TEntity>() where TEntity : class
    {
        foreach (var entry in ChangeTracker
            .Entries<TEntity>()
            .Where(entry => entry.State == EntityState.Added)
            .ToArray())
        {
            entry.State = EntityState.Detached;
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CareHomesDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
