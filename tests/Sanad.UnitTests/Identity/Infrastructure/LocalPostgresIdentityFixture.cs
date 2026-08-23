using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Identity.Infrastructure.Persistence;

namespace Sanad.UnitTests.Identity.Infrastructure;

public sealed class LocalPostgresIdentityFixture :
    IAsyncLifetime
{
    internal string ConnectionString { get; private set; } = string.Empty;
    private const string ConnectionStringEnvironmentVariable =
        "ConnectionStrings__IdentityIntegrationDatabase";

    public IdentityDbContext DbContext { get; private set; } =
        default!;

    public bool IsAvailable { get; private set; }

    public async Task InitializeAsync()
    {
        string? connectionString =
            Environment.GetEnvironmentVariable(
                ConnectionStringEnvironmentVariable);

        ConnectionString = connectionString!;

        if (string.IsNullOrWhiteSpace(
            connectionString))
        {
            IsAvailable = false;

            return;
        }

        IsAvailable = true;

        DbContextOptions<IdentityDbContext> options =
            new DbContextOptionsBuilder<
                IdentityDbContext>()
                .UseNpgsql(
                    connectionString)
                .Options;

        DbContext = new IdentityDbContext(
            options);

        await DbContext.Database.EnsureDeletedAsync();

        await DbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await DbContext.DisposeAsync();
    }
}