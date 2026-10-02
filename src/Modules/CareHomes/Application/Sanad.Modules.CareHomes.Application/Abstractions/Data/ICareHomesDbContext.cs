using Microsoft.EntityFrameworkCore;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Application.Abstractions.Data;

public interface ICareHomesDbContext
{
    DbSet<CareHomeFacility> Facilities { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
