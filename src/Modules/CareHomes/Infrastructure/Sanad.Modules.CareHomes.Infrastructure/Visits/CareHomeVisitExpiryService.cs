using Microsoft.EntityFrameworkCore;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;

namespace Sanad.Modules.CareHomes.Infrastructure.Visits;

public static class CareHomeVisitExpiryService
{
    public static async Task<int> SweepAsync(CareHomesDbContext db, DateTime utcNow, CancellationToken ct)
    {
        List<CareHomeVisit> expired = await db.Visits.Where(x =>
            (x.Status == CareHomeVisitStatus.Pending || x.Status == CareHomeVisitStatus.ReschedulePending)
            && x.DecisionExpiresOnUtc <= utcNow).ToListAsync(ct);
        foreach (CareHomeVisit visit in expired) visit.Expire(utcNow);
        if (expired.Count != 0) await db.SaveChangesAsync(ct);
        return expired.Count;
    }
}
