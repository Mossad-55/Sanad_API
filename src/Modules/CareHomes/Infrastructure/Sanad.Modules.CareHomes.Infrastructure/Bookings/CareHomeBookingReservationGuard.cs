using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sanad.Modules.CareHomes.Application.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;

namespace Sanad.Modules.CareHomes.Infrastructure.Bookings;

public sealed class CareHomeBookingReservationGuard(CareHomesDbContext db) : ICareHomeBookingReservationGuard
{
    public async Task<T> ExecuteAsync<T>(CareHomeId facilityId, Guid roomTypeId, DateOnly startDate, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        // Serialize the whole room type for the facility. The request dates can
        // overlap even when their starts differ, so the date is deliberately not
        // part of this lock key.
        string key = $"{facilityId.Value:N}:{roomTypeId:N}";
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
            T result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (PostgresException ex) when (ex.SqlState is "40001" or "40P01")
        {
            throw new CareHomeCapacityConflictException();
        }
    }
}
