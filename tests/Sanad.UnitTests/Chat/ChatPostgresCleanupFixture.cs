using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sanad.Modules.Chat.Infrastructure.Persistence;

namespace Sanad.UnitTests.Chat;

/// <summary>
/// Authorized relational test target for the Chat orphan-sweep race tests.
/// Uses the local PostgreSQL named by
/// <c>ConnectionStrings__IdentityIntegrationDatabase</c> but a SEPARATE
/// database (<c>SanadChatCleanupTestDb</c>) so the Chat sweep tests never
/// interfere with the Identity integration tests. The database is reset
/// (EnsureDeleted/EnsureCreated) per test class run.
/// </summary>
public sealed class ChatPostgresCleanupFixture : IAsyncLifetime
{
    private const string ConnectionStringEnvironmentVariable =
        "ConnectionStrings__IdentityIntegrationDatabase";

    private const string TestDatabaseName = "SanadChatCleanupTestDb";

    public string ConnectionString { get; private set; } = string.Empty;

    public bool IsAvailable { get; private set; }

    public async Task InitializeAsync()
    {
        string? baseConnectionString =
            Environment.GetEnvironmentVariable(
                ConnectionStringEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            IsAvailable = false;
            return;
        }

        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = TestDatabaseName
        };

        ConnectionString = builder.ToString();
        IsAvailable = true;

        await using ChatDbContext db = CreateDb();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task ResetAsync()
    {
        await using ChatDbContext db = CreateDb();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public ChatDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ChatDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);

    public async Task DisposeAsync()
    {
        if (!IsAvailable)
        {
            return;
        }

        try
        {
            await using ChatDbContext db = CreateDb();
            await db.Database.EnsureDeletedAsync();
        }
        catch
        {
            // Best-effort cleanup of the disposable test database.
        }
    }
}
