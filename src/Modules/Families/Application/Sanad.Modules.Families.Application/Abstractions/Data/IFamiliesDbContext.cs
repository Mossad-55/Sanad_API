using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.Modules.Families.Application.Abstractions.Data;

public interface IFamiliesDbContext
{
    DbSet<Family> Families { get; }

    DbSet<Elderly> Elderlies { get; }

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}