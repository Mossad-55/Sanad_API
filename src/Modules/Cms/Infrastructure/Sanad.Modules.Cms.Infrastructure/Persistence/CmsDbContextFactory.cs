using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sanad.Modules.Cms.Infrastructure.Persistence;

public sealed class CmsDbContextFactory :
    IDesignTimeDbContextFactory<CmsDbContext>
{
    public CmsDbContext CreateDbContext(
        string[] args)
    {
        string? connectionString =
            Environment.GetEnvironmentVariable(
                "ConnectionStrings__CmsDatabase")
            ?? Environment.GetEnvironmentVariable(
                "ConnectionStrings__IdentityDatabase");

        if (string.IsNullOrWhiteSpace(
            connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__CmsDatabase or " +
                "ConnectionStrings__IdentityDatabase is required.");
        }

        DbContextOptions<CmsDbContext> options =
            new DbContextOptionsBuilder<
                CmsDbContext>()
                .UseNpgsql(
                    connectionString,
                    npgsqlOptions =>
                        npgsqlOptions.MigrationsHistoryTable(
                            "__EFMigrationsHistory",
                            CmsDbContext.Schema))
                .Options;

        return new CmsDbContext(
            options);
    }
}