using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Sanad.API.Seeding;

namespace Sanad.UnitTests.API;

public sealed class TestUserSeedTargetGuardTests
{
    [Fact]
    public void DisabledSeed_DoesNotRequireOrInspectDatabaseTarget()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:TestUserSeed:Enabled"] = "false"
            })
            .Build();

        TestUserSeedTargetGuard.EnsureSafeTarget(
            configuration,
            new TestHostEnvironment(Environments.Production));
    }

    [Fact]
    public void EnabledSeed_OutsideDevelopment_IsRejectedBeforeDatabaseAccess()
    {
        IConfiguration configuration = Configuration(enabled: true);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TestUserSeedTargetGuard.EnsureSafeTarget(
                configuration,
                new TestHostEnvironment(Environments.Production)));

        Assert.Contains("only be enabled in Development", exception.Message);
    }

    [Theory]
    [InlineData("localhost", 5432, "SanadIntegrationDb")]
    [InlineData("db.example.test", 5432, "SanadBrunoTestDb")]
    [InlineData("localhost", 5433, "SanadBrunoTestDb")]
    public void EnabledSeed_RejectsUnsafeTargetBeforeDatabaseAccess(
        string host,
        int port,
        string database)
    {
        IConfiguration configuration = Configuration(
            enabled: true,
            identityHost: host,
            identityDatabase: database,
            identityPort: port);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TestUserSeedTargetGuard.EnsureSafeTarget(
                configuration,
                new TestHostEnvironment(Environments.Development)));

        Assert.Contains("localhost:5432/SanadBrunoTestDb", exception.Message);
    }

    [Fact]
    public void EnabledSeed_RejectsAnyConfiguredModuleDatabaseOutsideApprovedTarget()
    {
        IConfiguration configuration = Configuration(enabled: true);
        Dictionary<string, string?> values = new()
        {
            ["App:TestUserSeed:Enabled"] = "true",
            ["ConnectionStrings:IdentityDatabase"] = "Host=localhost;Port=5432;Database=SanadBrunoTestDb;Username=test;Password=test",
            ["ConnectionStrings:CmsDatabase"] = "Host=localhost;Port=5432;Database=SanadIntegrationDb;Username=test;Password=test"
        };
        configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TestUserSeedTargetGuard.EnsureSafeTarget(
                configuration,
                new TestHostEnvironment(Environments.Development)));

        Assert.Contains("localhost:5432/SanadBrunoTestDb", exception.Message);
    }

    [Fact]
    public void EnabledSeed_WithLivePaymobKey_IsRejectedBeforeDatabaseAccess()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:TestUserSeed:Enabled"] = "true",
                ["ConnectionStrings:IdentityDatabase"] = "Host=localhost;Port=5432;Database=SanadBrunoTestDb;Username=test;Password=test",
                ["Paymob:SecretKey"] = "sk_live-test-marker"
            })
            .Build();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TestUserSeedTargetGuard.EnsureSafeTarget(
                configuration,
                new TestHostEnvironment(Environments.Development)));

        Assert.Contains("LIVE Paymob key", exception.Message);
    }

    [Fact]
    public void PinToApprovedDatabase_ChangesOnlyConfiguredDatabaseOnLocalServer()
    {
        ConfigurationManager configuration = new();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["App:TestUserSeed:Enabled"] = "true",
            ["ConnectionStrings:IdentityDatabase"] = "Host=localhost;Port=5432;Database=SanadIntegrationDb;Username=test-user;Password=test-password"
        });

        TestUserSeedTargetGuard.PinToApprovedDatabase(
            configuration,
            new TestHostEnvironment(Environments.Development));

        NpgsqlConnectionStringBuilder pinned = new(
            configuration.GetConnectionString("IdentityDatabase")!);
        Assert.Equal("localhost", pinned.Host);
        Assert.Equal(5432, pinned.Port);
        Assert.Equal("SanadBrunoTestDb", pinned.Database);
        Assert.Equal("test-user", pinned.Username);
        Assert.Equal("test-password", pinned.Password);
    }

    [Fact]
    public void PinToApprovedDatabase_RejectsNonLocalServerBeforeChangingConfiguration()
    {
        ConfigurationManager configuration = new();
        const string original = "Host=db.example.test;Port=5432;Database=SanadIntegrationDb;Username=test;Password=test";
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["App:TestUserSeed:Enabled"] = "true",
            ["ConnectionStrings:IdentityDatabase"] = original
        });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TestUserSeedTargetGuard.PinToApprovedDatabase(
                configuration,
                new TestHostEnvironment(Environments.Development)));

        Assert.Contains("localhost:5432/SanadBrunoTestDb", exception.Message);
        Assert.Equal(original, configuration.GetConnectionString("IdentityDatabase"));
    }

    private static IConfiguration Configuration(
        bool enabled,
        string identityHost = "localhost",
        string identityDatabase = "SanadBrunoTestDb",
        int identityPort = 5432)
    {
        Dictionary<string, string?> values = new()
        {
            ["App:TestUserSeed:Enabled"] = enabled.ToString(),
            ["ConnectionStrings:IdentityDatabase"] = $"Host={identityHost};Port={identityPort};Database={identityDatabase};Username=test;Password=test"
        };

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Sanad.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
