using Microsoft.EntityFrameworkCore;
using Sanad.Modules.CareHomes.Application.Bookings;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Families.Application.Abstractions.Payments;

namespace Sanad.Modules.CareHomes.Infrastructure.Bookings;

public static class CareHomeBookingExpiryService
{
    public static async Task SweepAsync(CareHomesDbContext db, IPaymobClient paymob, CancellationToken stoppingToken)
    {
        var now = DateTime.UtcNow;
        var pending = await db.Bookings.Where(x => (x.Status == CareHomeBookingStatus.PendingPayment && x.CheckoutHoldUntilUtc <= now) || (x.Status == CareHomeBookingStatus.PaidAwaitingDecision && x.DecisionHoldUntilUtc <= now)).ToListAsync(stoppingToken);
        foreach (var booking in pending)
        {
            bool decisionExpired = booking.Status == CareHomeBookingStatus.PaidAwaitingDecision;
            booking.ExpireIfNeeded(now);
            if (decisionExpired && booking.Status == CareHomeBookingStatus.RefundPending)
                CareHomeNotificationEvents.Enqueue(db, CareHomeNotificationEvent.DecisionExpired, booking.Id, now,
                    eventKey: CareHomeNotificationEvents.TransitionKey(booking.Id, CareHomeNotificationEvent.DecisionExpired));
            await db.SaveChangesAsync(stoppingToken);
            if (booking.Status != CareHomeBookingStatus.RefundPending || booking.PaymobTransactionId is null) continue;
            await CareHomeRefundProcessor.InitiateAsync(db, paymob, booking, now, stoppingToken);
        }
    }
}
