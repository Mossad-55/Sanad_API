using Microsoft.EntityFrameworkCore;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Domain.Bookings;

namespace Sanad.Modules.CareHomes.Application.Abstractions.Data;

public interface ICareHomesDbContext
{
    DbSet<CareHomeFacility> Facilities { get; }
    DbSet<CareHomeRoomType> RoomTypes { get; }
    DbSet<CareHomeRoom> Rooms { get; }
    DbSet<CareHomeBed> Beds { get; }
    DbSet<CareHomeMaintenanceBlock> MaintenanceBlocks { get; }
    DbSet<CareHomeBooking> Bookings { get; }
    DbSet<CareHomeInternalBookingNote> InternalBookingNotes { get; }
    DbSet<CareHomeRating> Ratings { get; }
    DbSet<CareHomePayout> Payouts { get; }
    DbSet<CareHomePayoutDebt> PayoutDebts { get; }
    DbSet<CareHomeBookingAssignmentHistory> BookingAssignmentHistory { get; }
    DbSet<CareHomeTransferNotificationOutbox> TransferNotificationOutbox { get; }
    DbSet<CareHomeNotificationOutbox> BookingNotificationOutbox { get; }
    DbSet<CareHomeCheckInDispute> CheckInDisputes { get; }
    DbSet<CareHomeProfileMedia> ProfileMedia { get; }
    DbSet<CareHomeVisitSettings> VisitSettings { get; }
    DbSet<CareHomeVisit> Visits { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
