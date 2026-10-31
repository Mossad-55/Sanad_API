using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Ahlena.API;
using Ahlena.API.Seeding;
using Ahlena.BuildingBlocks.Infrastructure.Storage;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;
using Ahlena.Modules.Caregivers.Infrastructure.Persistence;
using Ahlena.Modules.Chat.Infrastructure.Persistence;
using Ahlena.Modules.Cms.Infrastructure.Persistence;
using Ahlena.Modules.Community.Infrastructure.Persistence;
using Ahlena.Modules.Families.Infrastructure.Persistence;
using Ahlena.Modules.Finance.Infrastructure;
using Ahlena.Modules.Identity.Infrastructure.Persistence;
using Ahlena.Modules.Notifications.Infrastructure.Persistence;

namespace Ahlena.UnitTests.API;

/// <summary>
/// R1 regression: the portal metadata host must be isolated before any
/// scenario runs. These checks assert registrations and configuration only;
/// they open no database connection, read no secret, and start no job.
/// </summary>
public sealed class PortalHostIsolationTests : IClassFixture<PortalApiMetadataHost>
{
    private static readonly string[] ProductionJobNames =
    [
        "CareHomeLicenseExpiryMonitor",
        "CareHomeBookingExpiryHostedService",
        "CareHomeTransferNotificationProcessor",
        "CareHomeBookingNotificationProcessor",
        "CareHomeReviewNotificationProcessor",
        "EmailOutboxProcessor",
        "PushOutboxProcessor",
        "ChatPlusDispatchService",
        "ChatAttachmentCleanupHostedService"
    ];

    private static readonly string[] ConnectionKeys =
    [
        "ConnectionStrings:IdentityDatabase",
        "ConnectionStrings:NotificationsDatabase",
        "ConnectionStrings:CmsDatabase",
        "ConnectionStrings:ChatDatabase",
        "ConnectionStrings:FinanceDatabase",
        "ConnectionStrings:FamiliesDatabase",
        "ConnectionStrings:CareHomesDatabase",
        "ConnectionStrings:CaregiversDatabase",
        "ConnectionStrings:CommunityDatabase"
    ];

    private readonly PortalApiMetadataHost _host;

    public PortalHostIsolationTests(PortalApiMetadataHost host) => _host = host;

    [Fact]
    public void NoProductionBackgroundJobsRegistered()
    {
        List<string> actual = _host.Services
            .GetRequiredService<IEnumerable<IHostedService>>()
            .Select(service => service.GetType().FullName ?? service.GetType().Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.DoesNotContain(
            actual,
            name => ProductionJobNames.Any(job =>
                name.Contains(job, StringComparison.Ordinal)));

        // Only framework host services may remain (the TestServer host
        // itself and DataProtection key management, ephemeral here): every
        // remaining service must live in a framework namespace.
        Assert.All(
            actual,
            name => Assert.True(
                name.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal),
                $"Non-framework background service is registered: {name}"));
    }

    [Fact]
    public void EveryModuleConnectionKey_UsesDummyTarget()
    {
        IConfiguration configuration = _host.Services.GetRequiredService<IConfiguration>();

        foreach (string key in ConnectionKeys)
            Assert.Equal(PortalApiMetadataHost.DummyConnectionString, configuration[key]);
    }

    [Fact]
    public void EveryModuleDbContext_IsIsolatedInMemory()
    {
        // Module registrations capture connection strings eagerly at startup,
        // so the effective guarantee is structural: every concrete module
        // context resolves on an InMemory store and no PostgreSQL connection
        // is possible regardless of inherited configuration.
        using IServiceScope scope = _host.Services.CreateScope();

        AssertInMemory<IdentityDbContext>(scope);
        AssertInMemory<CmsDbContext>(scope);
        AssertInMemory<CaregiversDbContext>(scope);
        AssertInMemory<FamiliesDbContext>(scope);
        AssertInMemory<CareHomesDbContext>(scope);
        AssertInMemory<NotificationsDbContext>(scope);
        AssertInMemory<CommunityDbContext>(scope);
        AssertInMemory<ChatDbContext>(scope);
        AssertInMemory<FinanceDbContext>(scope);
    }

    [Fact]
    public void MigrationPathsAndSeeders_Disabled()
    {
        IConfiguration configuration = _host.Services.GetRequiredService<IConfiguration>();
        IHostEnvironment environment = _host.Services.GetRequiredService<IHostEnvironment>();

        Assert.Equal("Development", environment.EnvironmentName);
        Assert.False(StartupMigrationPolicy.ShouldApplyModuleMigrations(configuration, environment));
        Assert.False(StartupMigrationPolicy.ShouldApplyFinanceMigrations(configuration, environment));
        Assert.False(configuration.GetValue<bool>($"{TestUserSeedOptions.SectionName}:Enabled"));
    }

    [Fact]
    public void Storage_UsesTestTemporaryRoot()
    {
        string root = _host.Services
            .GetRequiredService<IOptions<LocalStorageOptions>>()
            .Value
            .GetEffectiveRootPath();

        Assert.True(
            root.StartsWith(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase),
            $"Storage root is not under the test temporary directory: {root}");
        Assert.False(
            root.Contains("sanad-files", StringComparison.OrdinalIgnoreCase),
            $"Storage root must not be the binary-relative default: {root}");
    }

    [Fact]
    public void DataProtection_IsEphemeralAndFunctional()
    {
        Microsoft.AspNetCore.DataProtection.IDataProtectionProvider provider = _host.Services
            .GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();

        Assert.Equal(
            "Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider",
            provider.GetType().FullName);
        Microsoft.AspNetCore.DataProtection.IDataProtector protector =
            provider.CreateProtector("portal-isolation-test");
        byte[] protectedBytes = protector.Protect(System.Text.Encoding.UTF8.GetBytes("probe"));
        Assert.Equal("probe", System.Text.Encoding.UTF8.GetString(protector.Unprotect(protectedBytes)));
    }

    [Fact]
    public void ConfigurationPrecedence_TestOverridesWinOverInheritedLayer()
    {
        var inherited = new ConfigurationManager();
        inherited.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:IdentityDatabase"] = "Host=adversarial;Database=live;",
            ["ConnectionStrings:CareHomesDatabase"] = "Host=adversarial;Database=live;",
            ["ConnectionStrings:FinanceDatabase"] = "Host=adversarial;Database=live;",
            ["Database:ApplyMigrationsOnStartup"] = "true",
            ["App:TestUserSeed:Enabled"] = "true"
        });
        inherited.AddInMemoryCollection(PortalApiMetadataHost.TestOverrides);

        foreach (string key in ConnectionKeys)
            Assert.Equal(PortalApiMetadataHost.DummyConnectionString, inherited[key]);
        Assert.False(inherited.GetValue<bool>("Database:ApplyMigrationsOnStartup"));
        Assert.False(inherited.GetValue<bool>("App:TestUserSeed:Enabled"));
        Assert.False(inherited.GetValue<bool>("FinanceMigrations:ApplyOnStartup"));
        Assert.Equal(
            PortalApiMetadataHost.DummyConnectionString,
            inherited["ConnectionStrings:CommunityDatabase"]);
    }

    private static void AssertInMemory<TContext>(IServiceScope scope)
        where TContext : DbContext
    {
        TContext context = scope.ServiceProvider.GetRequiredService<TContext>();
        Assert.Equal(
            "Microsoft.EntityFrameworkCore.InMemory",
            context.Database.ProviderName);
    }
}
