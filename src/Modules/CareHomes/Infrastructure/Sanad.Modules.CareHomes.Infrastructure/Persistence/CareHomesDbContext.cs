using Microsoft.EntityFrameworkCore;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence;

public sealed class CareHomesDbContext(DbContextOptions<CareHomesDbContext> options)
    : DbContext(options), ICareHomesDbContext
{
    public const string Schema = "care_homes";

    public DbSet<CareHomeFacility> Facilities => Set<CareHomeFacility>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CareHomesDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
