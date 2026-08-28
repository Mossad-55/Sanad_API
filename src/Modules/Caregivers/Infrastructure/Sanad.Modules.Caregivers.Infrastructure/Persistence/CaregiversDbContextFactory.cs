using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence;

public sealed class CaregiversDbContextFactory :
    IDesignTimeDbContextFactory<CaregiversDbContext>
{
    public CaregiversDbContext CreateDbContext(
        string[] args)
    {
        string? connectionString =
            Environment.GetEnvironmentVariable(
                "ConnectionStrings__CaregiversDatabase")
            ?? Environment.GetEnvironmentVariable(
                "ConnectionStrings__IdentityDatabase");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__CaregiversDatabase or " +
                "ConnectionStrings__IdentityDatabase is required.");
        }

        DbContextOptions<CaregiversDbContext> options =
            new DbContextOptionsBuilder<CaregiversDbContext>()
                .UseNpgsql(
                    connectionString,
                    npgsqlOptions =>
                        npgsqlOptions.MigrationsHistoryTable(
                            "__EFMigrationsHistory",
                            CaregiversDbContext.Schema))
                .Options;

        return new CaregiversDbContext(options);
    }
}