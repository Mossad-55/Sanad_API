using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Bookings;

namespace Sanad.Modules.CareHomes.Application.Bookings;

public static class CareHomeNotificationEvents
{
    public static void Enqueue(ICareHomesDbContext db, CareHomeNotificationEvent eventType,
        Guid bookingId, DateTime utcNow, Guid? disputeId = null, string? eventKey = null)
    {
        string key = eventKey ?? $"care-home-booking:{bookingId:N}:{eventType}:{Guid.CreateVersion7():N}";
        db.BookingNotificationOutbox.Add(CareHomeNotificationOutbox.Create(key, eventType, bookingId, disputeId, utcNow));
    }

    public static string TransitionKey(Guid bookingId, CareHomeNotificationEvent eventType) =>
        $"care-home-booking:{bookingId:N}:{eventType}";
}
