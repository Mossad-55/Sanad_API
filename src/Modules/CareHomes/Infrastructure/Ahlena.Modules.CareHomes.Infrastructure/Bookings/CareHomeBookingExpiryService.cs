using Microsoft.EntityFrameworkCore;
using Ahlena.Modules.CareHomes.Application.Bookings;
using Ahlena.Modules.CareHomes.Domain.Bookings;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;
using Ahlena.Modules.Families.Application.Abstractions.Payments;

namespace Ahlena.Modules.CareHomes.Infrastructure.Bookings;

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
