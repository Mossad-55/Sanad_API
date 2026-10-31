using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Ahlena.Modules.Community.Application.Abstractions.Data;
using Ahlena.Modules.Community.Infrastructure.Persistence;

namespace Ahlena.Modules.Community.Infrastructure;

public sealed class CommunityDbContextFactory : IDesignTimeDbContextFactory<CommunityDbContext>
{
    public CommunityDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(
                "ConnectionStrings__CommunityDatabase")
            ?? Environment.GetEnvironmentVariable(
                "ConnectionStrings__IdentityDatabase");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:CommunityDatabase or " +
                "ConnectionStrings:IdentityDatabase is required.");
        }

        var options = new DbContextOptionsBuilder<CommunityDbContext>()
            .UseNpgsql(connectionString,
                npgsqlOptions =>
                    npgsqlOptions.MigrationsHistoryTable(
                        "__EFMigrationsHistory",
                        CommunityDbContext.Schema))
            .Options;

        return new CommunityDbContext(options);
    }
}