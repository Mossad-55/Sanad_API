using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sanad.Modules.Finance.Infrastructure;

public sealed class FinanceDbContextFactory : IDesignTimeDbContextFactory<FinanceDbContext>
{
    public FinanceDbContext CreateDbContext(string[] args)
    {
        string? connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__FinanceDatabase")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__IdentityDatabase");

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "ConnectionStrings__FinanceDatabase or ConnectionStrings__IdentityDatabase is required.");

        DbContextOptions<FinanceDbContext> options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
            .Options;

        return new FinanceDbContext(options);
    }
}
