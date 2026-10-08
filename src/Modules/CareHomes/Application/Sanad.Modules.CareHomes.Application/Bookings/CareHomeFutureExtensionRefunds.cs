using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.Families.Application.Abstractions.Payments;

namespace Sanad.Modules.CareHomes.Application.Bookings;

public static class CareHomeFutureExtensionRefunds
{
    public static async Task<Result> ProcessAsync(
        ICareHomesDbContext db,
        IPaymobClient paymob,
        CareHomeBooking endedSegment,
        DateOnly endedOn,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        Guid rootId = endedSegment.ExtensionRootBookingId ?? endedSegment.Id;
        var future = await db.Bookings
            .Where(x => x.ExtensionRootBookingId == rootId && x.StartDate > endedOn && x.ActualCheckInOnUtc == null)
            .OrderBy(x => x.StartDate)
            .ToListAsync(cancellationToken);

        foreach (var extension in future)
        {
            if (extension.Status is CareHomeBookingStatus.PendingPayment
                or CareHomeBookingStatus.PaidAwaitingDecision
                or CareHomeBookingStatus.Accepted)
                extension.CancelUnusedExtension(endedOn, utcNow);
        }
        if (future.Count > 0) await db.SaveChangesAsync(cancellationToken);

        foreach (var extension in future.Where(x => x.Status == CareHomeBookingStatus.RefundPending
            && x.RefundStatus is CareHomeRefundStatus.Pending or CareHomeRefundStatus.Failed))
        {
            Result result = await CareHomeRefundProcessor.InitiateAsync(db, paymob, extension, utcNow, cancellationToken);
            if (result.IsFailure) return result;
        }
        return Result.Success();
    }

    public static DateOnly CairoDate(DateTime utc)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); }
        catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); }
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));
    }
}
