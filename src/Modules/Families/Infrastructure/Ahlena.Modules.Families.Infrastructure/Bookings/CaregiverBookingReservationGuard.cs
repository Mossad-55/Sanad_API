using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Abstractions.Bookings;
using Ahlena.Modules.Families.Infrastructure.Persistence;

namespace Ahlena.Modules.Families.Infrastructure.Bookings;

/// <summary>
/// Slot-serialization guard for caregiver booking checkout. The lock key is
/// <c>"{caregiverId:N}:{date:yyyy-MM-dd}"</c>: one caregiver's one date is the
/// contended resource, so two overlapping requests on that date serialize while
/// different caregivers/dates stay independent. A serialization failure
/// (<c>40001</c>) or deadlock (<c>40P01</c>) is reported as a schedule
/// conflict; the availability code is reserved for a slot proven outside the
/// declared windows.
/// </summary>
public sealed class CaregiverBookingReservationGuard(FamiliesDbContext db)
    : ICaregiverBookingCheckoutGuard
{
    public async Task<T> ExecuteAsync<T>(
        CaregiverId caregiverId,
        DateOnly bookingDate,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        string key = $"{caregiverId.Value:N}:{bookingDate:yyyy-MM-dd}";

        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))",
                cancellationToken);

            T result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (PostgresException exception) when (
            exception.SqlState is "40001" or "40P01")
        {
            throw new CaregiverBookingScheduleConflictException();
        }
    }
}
