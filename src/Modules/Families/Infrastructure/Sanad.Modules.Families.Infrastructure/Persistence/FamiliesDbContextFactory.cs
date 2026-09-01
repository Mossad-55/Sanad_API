using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sanad.Modules.Families.Infrastructure.Persistence;

public sealed class FamiliesDbContextFactory :
    IDesignTimeDbContextFactory<FamiliesDbContext>
{
    public FamiliesDbContext CreateDbContext(
        string[] args)
    {
        string? connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__FamiliesDatabase")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__IdentityDatabase");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__FamiliesDatabase or " +
                "ConnectionStrings__IdentityDatabase is required.");
        }

        DbContextOptions<FamiliesDbContext> options =
            new DbContextOptionsBuilder<FamiliesDbContext>()
                .UseNpgsql(
                    connectionString,
                    npgsql => npgsql.MigrationsHistoryTable(
                        "__EFMigrationsHistory",
                        FamiliesDbContext.Schema))
                .Options;

        return new FamiliesDbContext(options);
    }
}