using Microsoft.EntityFrameworkCore;
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
            booking.ExpireIfNeeded(now); await db.SaveChangesAsync(stoppingToken);
            if (booking.Status != CareHomeBookingStatus.RefundPending || booking.PaymobTransactionId is null || !booking.TryClaimRefund(now)) continue;
            await db.SaveChangesAsync(stoppingToken);
            var refund = await paymob.RefundPaymentAsync(booking.PaymobTransactionId.Value.ToString(), booking.TotalAmount, stoppingToken);
            if (refund.IsSuccess) { booking.MarkRefundInitiated(refund.Value, now); await db.SaveChangesAsync(stoppingToken); }
        }
    }
}
