using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sanad.Modules.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContextFactory :
    IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(
        string[] args)
    {
        string? connectionString =
            Environment.GetEnvironmentVariable(
                "ConnectionStrings__IdentityDatabase");

        if (string.IsNullOrWhiteSpace(
            connectionString))
        {
            throw new InvalidOperationException(
                "The ConnectionStrings__IdentityDatabase " +
                "environment variable is required to create " +
                "IdentityDbContext migrations.");
        }

        DbContextOptions<IdentityDbContext> options =
            new DbContextOptionsBuilder<
                IdentityDbContext>()
                .UseNpgsql(
                    connectionString,
                    npgsqlOptions =>
                        npgsqlOptions.MigrationsHistoryTable(
                            "__EFMigrationsHistory",
                            IdentityDbContext.Schema))
                .Options;

        return new IdentityDbContext(
            options);
    }
}