using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sanad.Modules.CareHomes.Application.Visits;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;

namespace Sanad.Modules.CareHomes.Infrastructure.Visits;

public sealed class CareHomeVisitReservationGuard(CareHomesDbContext db) : ICareHomeVisitReservationGuard
{
    public async Task<T> ExecuteAsync<T>(CareHomeId facilityId, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        string key = $"{facilityId.Value:N}:visits";
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
            T result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (PostgresException ex) when (ex.SqlState is "40001" or "40P01")
        {
            throw new CareHomeVisitCapacityConflictException();
        }
    }
}
