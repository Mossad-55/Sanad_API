using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Ahlena.API.Seeding;

namespace Ahlena.UnitTests.API;

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

    [Theory]
    [InlineData("sk_live-test-marker")]
    [InlineData("egy_sk_live_test-marker")]
    public void EnabledSeed_WithLivePaymobKey_IsRejectedBeforeDatabaseAccess(string liveSecretKey)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:TestUserSeed:Enabled"] = "true",
                ["ConnectionStrings:IdentityDatabase"] = "Host=localhost;Port=5432;Database=SanadBrunoTestDb;Username=test;Password=test",
                ["Paymob:SecretKey"] = liveSecretKey
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

    // ---------------- Persistent-local fixture mode (L1) ----------------

    [Fact]
    public void PersistentLocalMode_AcceptsApprovedAhlenaDbTargetForAllModuleFallbacks()
    {
        IConfiguration configuration = Configuration(
            enabled: true,
            targetMode: "PersistentLocal",
            identityDatabase: "AhlenaDb");

        TestUserSeedTargetGuard.EnsureSafeTarget(
            configuration,
            new TestHostEnvironment(Environments.Development));
    }

    [Fact]
    public void PersistentLocalMode_RejectsBrunoTestTarget_ItIsNeverThePersistentTarget()
    {
        IConfiguration configuration = Configuration(
            enabled: true,
            targetMode: "PersistentLocal",
            identityDatabase: "SanadBrunoTestDb");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TestUserSeedTargetGuard.EnsureSafeTarget(
                configuration,
                new TestHostEnvironment(Environments.Development)));

        Assert.Contains("localhost:5432/AhlenaDb", exception.Message);
        Assert.DoesNotContain("test-password", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Username", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistentLocalMode_RejectsAnyConfiguredModuleDatabaseOutsideAhlenaDb()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:TestUserSeed:Enabled"] = "true",
                ["App:TestUserSeed:TargetMode"] = "PersistentLocal",
                ["ConnectionStrings:IdentityDatabase"] = "Host=localhost;Port=5432;Database=AhlenaDb;Username=test;Password=test",
                ["ConnectionStrings:FinanceDatabase"] = "Host=localhost;Port=5432;Database=SanadIntegrationDb;Username=test;Password=test"
            })
            .Build();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TestUserSeedTargetGuard.EnsureSafeTarget(
                configuration,
                new TestHostEnvironment(Environments.Development)));

        Assert.Contains("localhost:5432/AhlenaDb", exception.Message);
    }

    [Fact]
    public void PersistentLocalMode_PinValidatesOnly_AndNeverMutatesConfiguration()
    {
        const string approved = "Host=localhost;Port=5432;Database=AhlenaDb;Username=test-user;Password=test-password";
        ConfigurationManager configuration = new();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["App:TestUserSeed:Enabled"] = "true",
            ["App:TestUserSeed:TargetMode"] = "PersistentLocal",
            ["ConnectionStrings:IdentityDatabase"] = approved
        });

        TestUserSeedTargetGuard.PinToApprovedDatabase(
            configuration,
            new TestHostEnvironment(Environments.Development));

        Assert.Equal(approved, configuration.GetConnectionString("IdentityDatabase"));
    }

    [Fact]
    public void UnknownTargetMode_FailsClosed_EvenWhenTargetWouldBeApproved()
    {
        IConfiguration configuration = Configuration(
            enabled: true,
            targetMode: "SomeOtherMode",
            identityDatabase: "AhlenaDb");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TestUserSeedTargetGuard.EnsureSafeTarget(
                configuration,
                new TestHostEnvironment(Environments.Development)));

        Assert.Contains("must be 'BrunoTest' or 'PersistentLocal'", exception.Message);
    }

    [Fact]
    public void DisabledSeed_NeverInspectsUnknownTargetModeOrScope()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:TestUserSeed:Enabled"] = "false",
                ["App:TestUserSeed:TargetMode"] = "SomeOtherMode",
                ["App:TestUserSeed:Scope"] = "SomeOtherScope"
            })
            .Build();

        TestUserSeedTargetGuard.EnsureSafeTarget(
            configuration,
            new TestHostEnvironment(Environments.Development));

        Assert.False(TestUserSeedTargetGuard.IsPersistentLocalFixtureRun(configuration));
    }

    [Fact]
    public void IsPersistentLocalFixtureRun_TrueOnlyForEnabledPersistentLocalMode()
    {
        IConfiguration persistentLocal = Configuration(
            enabled: true,
            targetMode: "PersistentLocal",
            identityDatabase: "AhlenaDb");
        IConfiguration brunoTest = Configuration(enabled: true);
        IConfiguration disabled = Configuration(
            enabled: false,
            targetMode: "PersistentLocal",
            identityDatabase: "AhlenaDb");

        Assert.True(TestUserSeedTargetGuard.IsPersistentLocalFixtureRun(persistentLocal));
        Assert.False(TestUserSeedTargetGuard.IsPersistentLocalFixtureRun(brunoTest));
        Assert.False(TestUserSeedTargetGuard.IsPersistentLocalFixtureRun(disabled));
    }

    [Fact]
    public void RequirePersistentLocalFixtureConfiguration_OutsideDevelopment_IsRejected()
    {
        IConfiguration configuration = Configuration(
            enabled: true,
            targetMode: "PersistentLocal",
            identityDatabase: "AhlenaDb");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TestUserSeedTargetGuard.RequirePersistentLocalFixtureConfiguration(
                configuration,
                new TestHostEnvironment(Environments.Production)));

        Assert.Contains("Development", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequirePersistentLocalFixtureConfiguration_BrunoTestMode_IsRejected()
    {
        IConfiguration configuration = Configuration(
            enabled: true,
            targetMode: "BrunoTest",
            identityDatabase: "SanadBrunoTestDb");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TestUserSeedTargetGuard.RequirePersistentLocalFixtureConfiguration(
                configuration,
                new TestHostEnvironment(Environments.Development)));

        Assert.Contains("TargetMode=PersistentLocal", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequirePersistentLocalFixtureConfiguration_DisabledSeed_RequestsExplicitEnable()
    {
        IConfiguration configuration = Configuration(enabled: false);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TestUserSeedTargetGuard.RequirePersistentLocalFixtureConfiguration(
                configuration,
                new TestHostEnvironment(Environments.Development)));

        Assert.Contains("App:TestUserSeed:Enabled=true", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequirePersistentLocalFixtureConfiguration_ApprovedSetup_PassesWithoutConnecting()
    {
        IConfiguration configuration = Configuration(
            enabled: true,
            targetMode: "PersistentLocal",
            identityDatabase: "AhlenaDb");

        TestUserSeedTargetGuard.RequirePersistentLocalFixtureConfiguration(
            configuration,
            new TestHostEnvironment(Environments.Development));
    }

    [Fact]
    public void RequirePersistentLocalFixtureConfiguration_WrongModuleTarget_FailsWithSafeMessage()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:TestUserSeed:Enabled"] = "true",
                ["App:TestUserSeed:TargetMode"] = "PersistentLocal",
                ["ConnectionStrings:IdentityDatabase"] = "Host=localhost;Port=5432;Database=AhlenaDb;Username=test;Password=test",
                ["ConnectionStrings:ChatDatabase"] = "Host=remote.example.test;Port=5432;Database=AhlenaDb;Username=test;Password=test"
            })
            .Build();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TestUserSeedTargetGuard.RequirePersistentLocalFixtureConfiguration(
                configuration,
                new TestHostEnvironment(Environments.Development)));

        Assert.Contains("localhost:5432/AhlenaDb", exception.Message);
        Assert.DoesNotContain("remote.example.test", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", exception.Message, StringComparison.Ordinal);
    }

    private static IConfiguration Configuration(
        bool enabled,
        string targetMode = "BrunoTest",
        string identityHost = "localhost",
        string identityDatabase = "SanadBrunoTestDb",
        int identityPort = 5432)
    {
        Dictionary<string, string?> values = new()
        {
            ["App:TestUserSeed:Enabled"] = enabled.ToString(),
            ["App:TestUserSeed:TargetMode"] = targetMode,
            ["ConnectionStrings:IdentityDatabase"] = $"Host={identityHost};Port={identityPort};Database={identityDatabase};Username=test;Password=test"
        };

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Ahlena.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
