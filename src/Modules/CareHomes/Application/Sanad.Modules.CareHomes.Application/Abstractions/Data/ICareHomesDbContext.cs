using Microsoft.EntityFrameworkCore;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Idempotency;

namespace Sanad.Modules.CareHomes.Application.Abstractions.Data;

public interface ICareHomesDbContext
{
    DbSet<CareHomeFacility> Facilities { get; }
    DbSet<CareHomeRoomType> RoomTypes { get; }
    DbSet<CareHomeRoomTypeMedia> RoomTypeMedia { get; }
    DbSet<CareHomeRoom> Rooms { get; }
    DbSet<CareHomeRoomMedia> RoomMedia { get; }
    DbSet<CareHomeBed> Beds { get; }
    DbSet<CareHomeMaintenanceBlock> MaintenanceBlocks { get; }
    DbSet<CareHomeBooking> Bookings { get; }
    DbSet<CareHomeInternalBookingNote> InternalBookingNotes { get; }
    DbSet<CareHomeRating> Ratings { get; }
    DbSet<CareHomeRatingModerationAction> RatingModerationActions { get; }
    DbSet<CareHomeRatingFlag> RatingFlags { get; }
    DbSet<CareHomePayout> Payouts { get; }
    DbSet<CareHomePayoutDebt> PayoutDebts { get; }
    DbSet<CareHomeBookingAssignmentHistory> BookingAssignmentHistory { get; }
    DbSet<CareHomeTransferNotificationOutbox> TransferNotificationOutbox { get; }
    DbSet<CareHomeNotificationOutbox> BookingNotificationOutbox { get; }
    DbSet<CareHomeCheckInDispute> CheckInDisputes { get; }
    DbSet<CareHomeProfileMedia> ProfileMedia { get; }
    DbSet<CareHomeVisitSettings> VisitSettings { get; }
    DbSet<CareHomeVisit> Visits { get; }

    /// <summary>
    /// Accepted idempotent create attempts. The unique
    /// <c>(actor_id, operation, idempotency_key)</c> index makes concurrent
    /// duplicates execute exactly once.
    /// </summary>
    DbSet<IdempotencyRequest> IdempotencyRequests { get; }

    /// <summary>
    /// Detaches every pending <see cref="EntityState.Added"/> entity of
    /// <typeparamref name="TEntity"/> after the loser of an idempotency race.
    /// </summary>
    void DetachAdded<TEntity>() where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
