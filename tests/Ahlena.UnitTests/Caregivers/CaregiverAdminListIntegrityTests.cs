using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ahlena.Modules.Caregivers.Infrastructure;
using Ahlena.Modules.Caregivers.Infrastructure.Persistence;

namespace Ahlena.UnitTests.Caregivers;

// PORTAL-04: diagnostic classifications (same CASE shape as
// diagnostics/caregiver-identity.sql Section 3) plus configuration-wiring
// key/fallback behavior. Classification runs on the isolated SQLite
// attached-schema fixture: semantics proof only, not PostgreSQL proof.
// Configuration tests use dummy connection strings and never connect.
public sealed class CaregiverAdminListIntegrityTests
{
    [Fact]
    public async Task Classification_ShouldDistinguishMatchedMissingAndWrongRole()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeeded();
        using (seeded.Connection)
        {
            // One extra caregiver reusing an already-matched user_id: the
            // duplicate-reference control (user_id has an index only, no
            // unique constraint and no cross-module FK).
            CaregiverIntegrityFixture.InsertCaregiver(
                seeded.Connection,
                CaregiverIntegrityFixture.DuplicateMedicalCaregiverId,
                CaregiverIntegrityFixture.MatchedMedicalUserId,
                type: 1,
                status: 1,
                new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc));

            Dictionary<string, string> classifications = await RunClassificationAsync(seeded.Connection);

            Assert.Equal(6, classifications.Count);
            Assert.Equal("matched", classifications["a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1"]);
            Assert.Equal("missing_user", classifications["b2b2b2b2-b2b2-b2b2-b2b2-b2b2b2b2b2b2"]);
            Assert.Equal(
                "present_user_missing_expected_account",
                classifications["c3c3c3c3-c3c3-c3c3-c3c3-c3c3c3c3c3c3"]);
            Assert.Equal("missing_user", classifications["d4d4d4d4-d4d4-d4d4-d4d4-d4d4d4d4d4d4"]);
            Assert.Equal("matched", classifications["e5e5e5e5-e5e5-e5e5-e5e5-e5e5e5e5e5e5"]);
            Assert.Equal("matched", classifications["f6f6f6f6-f6f6-f6f6-f6f6-f6f6f6f6f6f6"]);

            // Reconciliation: matched + missing_user (+ wrong-role present
            // users) must equal the caregiver total, never hide rows.
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);
            int total = await db.CountAdminCaregiversAsync(null, null);
            Assert.Equal(6, total);
            Assert.Equal(
                total,
                classifications.Values.Count(v => v == "matched")
                + classifications.Values.Count(v => v == "missing_user")
                + classifications.Values.Count(v => v == "present_user_missing_expected_account"));
        }
    }

    [Fact]
    public async Task MismatchDetail_ShouldExposeActualAccountCodes_ForPresentUsers()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeeded();
        using (seeded.Connection)
        {
            List<(string CaregiverId, int ActualAccount)> rows = await RunMismatchDetailAsync(seeded.Connection);

            // Only the Family-only user behind the Companion caregiver.
            var single = Assert.Single(rows);
            Assert.Equal("c3c3c3c3-c3c3-c3c3-c3c3-c3c3c3c3c3c3", single.CaregiverId);
            Assert.Equal(1, single.ActualAccount);
        }
    }

    [Fact]
    public async Task DuplicateUserReferences_ShouldBeDetectable_ByUserIdGrouping()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeeded();
        using (seeded.Connection)
        {
            CaregiverIntegrityFixture.InsertCaregiver(
                seeded.Connection,
                CaregiverIntegrityFixture.DuplicateMedicalCaregiverId,
                CaregiverIntegrityFixture.MatchedMedicalUserId,
                type: 1,
                status: 1,
                new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc));

            List<(string UserId, int Rows)> groups = await RunDuplicateGroupsAsync(seeded.Connection);

            var duplicate = Assert.Single(groups);
            Assert.Equal("11111111-1111-1111-1111-111111111111", duplicate.UserId);
            Assert.Equal(2, duplicate.Rows);
        }
    }

    [Fact]
    public void CaregiversInfrastructure_ShouldFallBackToIdentityDatabase_WhenCaregiversKeyMissing()
    {
        const string identityCs = "Host=portal04-test;Database=identity;Username=test;Password=dummy";

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:IdentityDatabase"] = identityCs,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddCaregiversInfrastructure(config);

        Assert.Equal(identityCs, ResolvedConnectionString(services));
    }

    [Fact]
    public void CaregiversInfrastructure_ShouldPreferCaregiversDatabase_WhenBothKeysSet()
    {
        const string caregiversCs = "Host=portal04-test;Database=caregivers;Username=test;Password=dummy";

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:IdentityDatabase"] = "Host=portal04-test;Database=identity;Username=test;Password=dummy",
                ["ConnectionStrings:CaregiversDatabase"] = caregiversCs,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddCaregiversInfrastructure(config);

        Assert.Equal(caregiversCs, ResolvedConnectionString(services));
    }

    [Fact]
    public void CaregiversInfrastructure_ShouldThrow_WhenBothKeysMissing()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => services.AddCaregiversInfrastructure(config));
    }

    [Fact]
    public void CaregiversInfrastructure_ShouldCaptureConnectionStringEagerly_AtRegistration()
    {
        // The final IConfiguration snapshot alone is not proof of the
        // already-captured DI options: the connection string is read during
        // registration (UseNpgsql at startup), so a later config change has
        // no effect on the resolved DbContext options.
        const string registeredCs = "Host=portal04-test;Database=caregivers;Username=test;Password=dummy";

        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CaregiversDatabase"] = registeredCs,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddCaregiversInfrastructure(config);

        config["ConnectionStrings:CaregiversDatabase"] =
            "Host=portal04-test;Database=changed;Username=test;Password=dummy";

        Assert.Equal(registeredCs, ResolvedConnectionString(services));
    }

    private static string? ResolvedConnectionString(ServiceCollection services)
    {
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        CaregiversDbContext db = scope.ServiceProvider.GetRequiredService<CaregiversDbContext>();

        return db.Database.GetConnectionString();
    }

    // Same classification CASE as diagnostics/caregiver-identity.sql Section 3a.
    private static async Task<Dictionary<string, string>> RunClassificationAsync(SqliteConnection connection)
    {
        const string sql =
            """
            SELECT
                c.id AS caregiver_id,
                CASE
                    WHEN u.id IS NULL THEN 'missing_user'
                    WHEN NOT EXISTS (
                        SELECT 1 FROM identity.user_accounts a
                        WHERE a.user_id = c.user_id
                          AND a.account_type = CASE WHEN c.type = 1 THEN 2 WHEN c.type = 2 THEN 3 END
                    ) THEN 'present_user_missing_expected_account'
                    ELSE 'matched'
                END AS classification
            FROM caregivers.caregivers c
            LEFT JOIN identity.users u ON u.id = c.user_id
            ORDER BY c.id
            LIMIT $limit;
            """;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$limit", 100);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }

        return result;
    }

    private static async Task<List<(string CaregiverId, int ActualAccount)>> RunMismatchDetailAsync(
        SqliteConnection connection)
    {
        const string sql =
            """
            SELECT c.id, a.account_type
            FROM caregivers.caregivers c
            INNER JOIN identity.users u ON u.id = c.user_id
            LEFT JOIN identity.user_accounts a ON a.user_id = c.user_id
            WHERE NOT EXISTS (
                    SELECT 1 FROM identity.user_accounts a2
                    WHERE a2.user_id = c.user_id
                      AND a2.account_type = CASE WHEN c.type = 1 THEN 2 WHEN c.type = 2 THEN 3 END)
            ORDER BY c.id, a.account_type
            LIMIT $limit;
            """;

        var rows = new List<(string CaregiverId, int ActualAccount)>();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$limit", 100);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetInt32(1)));
        }

        return rows;
    }

    private static async Task<List<(string UserId, int Rows)>> RunDuplicateGroupsAsync(
        SqliteConnection connection)
    {
        const string sql =
            """
            SELECT c.user_id, COUNT(*) AS caregiver_rows
            FROM caregivers.caregivers c
            GROUP BY c.user_id
            HAVING COUNT(*) > 1
            ORDER BY caregiver_rows DESC, c.user_id
            LIMIT $limit;
            """;

        var groups = new List<(string UserId, int Rows)>();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$limit", 100);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            groups.Add((reader.GetString(0), reader.GetInt32(1)));
        }

        return groups;
    }
}
