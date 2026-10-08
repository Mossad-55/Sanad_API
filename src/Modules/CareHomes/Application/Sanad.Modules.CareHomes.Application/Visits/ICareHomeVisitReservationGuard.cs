using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Application.Visits;

public sealed class CareHomeVisitCapacityConflictException : Exception { }

public interface ICareHomeVisitReservationGuard
{
    Task<T> ExecuteAsync<T>(CareHomeId facilityId, Func<Task<T>> action, CancellationToken cancellationToken);
}
