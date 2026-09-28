using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Sanad.API.Seeding;

public static class TestUserSeedTargetGuard
{
    private const string AllowedDatabase = "SanadBrunoTestDb";
    private const int AllowedPort = 5432;
    private static readonly string[] ConnectionNames =
    [
        "IdentityDatabase",
        "CmsDatabase",
        "CaregiversDatabase",
        "FamiliesDatabase",
        "NotificationsDatabase"
    ];

    public static void PinToApprovedDatabase(
        ConfigurationManager configuration,
        IHostEnvironment environment)
    {
        if (!configuration.GetValue<bool>($"{TestUserSeedOptions.SectionName}:Enabled"))
        {
            return;
        }

        EnsureDevelopmentAndNonLivePayments(configuration, environment);

        string identity = RequiredConnection(configuration, "IdentityDatabase");
        List<KeyValuePair<string, string>> pinned = [];
        foreach (string name in ConnectionNames)
        {
            string? connectionString = configuration.GetConnectionString(name);
            if (connectionString is null && name != "IdentityDatabase")
            {
                continue;
            }

            connectionString ??= identity;
            NpgsqlConnectionStringBuilder parsed = ParseAndValidateLocalServer(connectionString);
            parsed.Database = AllowedDatabase;
            pinned.Add(new KeyValuePair<string, string>($"ConnectionStrings:{name}", parsed.ConnectionString));
        }

        foreach (KeyValuePair<string, string> item in pinned)
        {
            configuration[item.Key] = item.Value;
        }
    }

    public static void EnsureSafeTarget(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!configuration.GetValue<bool>($"{TestUserSeedOptions.SectionName}:Enabled"))
        {
            return;
        }

        EnsureDevelopmentAndNonLivePayments(configuration, environment);

        string identity = RequiredConnection(configuration, "IdentityDatabase");
        string cms = configuration.GetConnectionString("CmsDatabase") ?? identity;
        string caregivers = configuration.GetConnectionString("CaregiversDatabase") ?? identity;
        string families = configuration.GetConnectionString("FamiliesDatabase") ?? identity;
        string notifications = configuration.GetConnectionString("NotificationsDatabase") ?? identity;

        foreach (string connectionString in new[] { identity, cms, caregivers, families, notifications })
        {
            NpgsqlConnectionStringBuilder parsed = ParseAndValidateLocalServer(connectionString);

            if (!string.Equals(parsed.Database, AllowedDatabase, StringComparison.Ordinal))
            {
                throw UnsafeTarget();
            }
        }

        EnsureTargetDatabaseExists(identity);
    }

    private static string RequiredConnection(IConfiguration configuration, string name) =>
        configuration.GetConnectionString(name)
        ?? throw new InvalidOperationException(
            $"ConnectionStrings:{name} is required when App:TestUserSeed is enabled.");

    private static bool IsLocalHost(string? host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
        || string.Equals(host, "::1", StringComparison.Ordinal);

    private static void EnsureDevelopmentAndNonLivePayments(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "App:TestUserSeed may only be enabled in Development.");
        }

        if (configuration["Paymob:SecretKey"]?.StartsWith(
                "sk_live",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new InvalidOperationException(
                "App:TestUserSeed must never be enabled with a LIVE Paymob key.");
        }
    }

    private static NpgsqlConnectionStringBuilder ParseAndValidateLocalServer(
        string connectionString)
    {
        NpgsqlConnectionStringBuilder parsed;
        try
        {
            parsed = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw UnsafeTarget();
        }

        if (!IsLocalHost(parsed.Host) || parsed.Port != AllowedPort)
        {
            throw UnsafeTarget();
        }

        return parsed;
    }

    private static InvalidOperationException UnsafeTarget() =>
        new("Test-user seeding is allowed only when every configured database targets localhost:5432/SanadBrunoTestDb. No connection details were logged.");

    private static void EnsureTargetDatabaseExists(string identityConnectionString)
    {
        NpgsqlConnectionStringBuilder maintenance =
            new(identityConnectionString)
            {
                Database = "postgres"
            };

        try
        {
            using NpgsqlConnection connection = new(maintenance.ConnectionString);
            connection.Open();

            using NpgsqlCommand exists = connection.CreateCommand();
            exists.CommandText = "SELECT 1 FROM pg_database WHERE datname = @database";
            exists.Parameters.AddWithValue("database", AllowedDatabase);
            if (exists.ExecuteScalar() is not null)
            {
                return;
            }

            using NpgsqlCommand create = connection.CreateCommand();
            create.CommandText = "CREATE DATABASE \"SanadBrunoTestDb\"";
            create.ExecuteNonQuery();
        }
        catch (NpgsqlException)
        {
            throw new InvalidOperationException(
                "Could not create or inspect the approved local Bruno test database. Verify that local PostgreSQL is running and the configured test role can connect to postgres and create a database.");
        }
    }
}
