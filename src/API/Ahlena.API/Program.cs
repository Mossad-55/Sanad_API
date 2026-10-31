using System.Text.Json;
using Ahlena.API;
using Ahlena.API.Options;
using Ahlena.API.Seeding;
using Microsoft.AspNetCore.DataProtection;
using Ahlena.Modules.Caregivers.Infrastructure.Persistence;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;
using Ahlena.Modules.Chat.Infrastructure.Persistence;
using Ahlena.Modules.Cms.Infrastructure.Persistence;
using Ahlena.Modules.Community.Infrastructure.Persistence;
using Ahlena.Modules.Families.Infrastructure;
using Ahlena.Modules.Families.Infrastructure.Persistence;
using Ahlena.Modules.Finance.Infrastructure;
using Ahlena.Modules.Identity.Infrastructure;
using Ahlena.Modules.Identity.Infrastructure.Persistence;
using Ahlena.Modules.Notifications.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

if (Hc034FamilyFixtureProvisioner.IsRequested(args))
{
    Hc034FamilyFixtureProvisioner.ValidateTarget(builder.Configuration, builder.Environment);
    builder.Services.AddIdentityInfrastructure(builder.Configuration);
    builder.Services.AddFamiliesInfrastructure(builder.Configuration);
    using WebApplication provisionerHost = builder.Build();
    using IServiceScope provisionerScope = provisionerHost.Services.CreateScope();
    await Hc034FamilyFixtureProvisioner.ProvisionAsync(provisionerScope.ServiceProvider, builder.Configuration);
    return;
}

// L1 read-only pre-write report: verifies the effective target of all nine
// module contexts, pending migration counts, fixture record counts, storage
// root and non-live integrations, then prints a credential-free JSON report.
// Usage-gate failures throw with safe messages (non-zero exit); a failed
// report exits 1. Never starts Kestrel, never applies a migration, never
// writes, and never logs a connection string or credential: console logging
// is removed so the only stdout output is the JSON report.
if (args.Contains("--verify-local-fixture-target", StringComparer.Ordinal))
{
    builder.Logging.ClearProviders();

    TestUserSeedTargetGuard.RequirePersistentLocalFixtureConfiguration(
        builder.Configuration,
        builder.Environment);

    // Fixture-seed runs only (guard-enforced Development + persistent-local
    // target). Ephemeral keys are acceptable here and never used on a
    // publicly reachable host.
    builder.Services.AddDataProtection()
        .UseEphemeralDataProtectionProvider();

    builder.Services.AddAhlenaApi(builder.Configuration);

    using WebApplication verificationHost = builder.Build();
    using IServiceScope verificationScope = verificationHost.Services.CreateScope();
    IServiceProvider verificationServices = verificationScope.ServiceProvider;

    LocalFixtureTargetReport report =
        await TestUserSeedTargetGuard.VerifyPersistentLocalRuntimeAsync(
            builder.Configuration,
            builder.Environment,
            [
                new LocalFixtureModuleContext("Identity", verificationServices.GetRequiredService<IdentityDbContext>()),
                new LocalFixtureModuleContext("Cms", verificationServices.GetRequiredService<CmsDbContext>()),
                new LocalFixtureModuleContext("Caregivers", verificationServices.GetRequiredService<CaregiversDbContext>()),
                new LocalFixtureModuleContext("Families", verificationServices.GetRequiredService<FamiliesDbContext>()),
                new LocalFixtureModuleContext("Notifications", verificationServices.GetRequiredService<NotificationsDbContext>()),
                new LocalFixtureModuleContext("CareHomes", verificationServices.GetRequiredService<CareHomesDbContext>()),
                new LocalFixtureModuleContext("Finance", verificationServices.GetRequiredService<FinanceDbContext>()),
                new LocalFixtureModuleContext("Community", verificationServices.GetRequiredService<CommunityDbContext>()),
                new LocalFixtureModuleContext("Chat", verificationServices.GetRequiredService<ChatDbContext>())
            ],
            verificationServices.GetRequiredService<IdentityDbContext>());

    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    if (!report.Passed)
    {
        Environment.ExitCode = 1;
    }

    return;
}

// L1 Owner-authorized persistent-local schema step: validates the approved
// target through the same guard chain as the fixture seed (usage gate -> pin ->
// runtime target check), then applies the pending migrations of the nine module
// contexts without starting Kestrel or any background service. It fails closed
// and applies nothing at all unless every module still targets the approved
// local AhlenaDb and its exact pending migration ids match the expected set
// passed on the command line with --expect ("Module=id;id;id,...", where an
// empty id list means the module has no pending migrations). It never creates a
// database, never resets or deletes data, and prints only a credential-free
// JSON report of the migration ids it applied.
if (args.Contains("--apply-local-fixtures-migrations", StringComparer.Ordinal))
{
    builder.Logging.ClearProviders();

    TestUserSeedTargetGuard.RequirePersistentLocalFixtureConfiguration(
        builder.Configuration,
        builder.Environment);
    TestUserSeedTargetGuard.PinToApprovedDatabase(
        builder.Configuration,
        builder.Environment);
    TestUserSeedTargetGuard.EnsureSafeTarget(
        builder.Configuration,
        builder.Environment);

    string? expectedArgument = null;

    for (int index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], "--expect", StringComparison.Ordinal))
        {
            expectedArgument = args[index + 1];
            break;
        }
    }

    LocalFixtureMigrationApplyReport migrationReport;

    if (!TestUserSeedTargetGuard.TryParseExpectedPendingIds(
        expectedArgument,
        out Dictionary<string, IReadOnlyList<string>> expectedPendingIds,
        out string expectationProblem))
    {
        migrationReport = new LocalFixtureMigrationApplyReport(
            Passed: false,
            TargetDatabase: "AhlenaDb",
            AppliedAnyMigrations: false,
            Modules: [],
            FailedChecks: [expectationProblem]);
    }
    else
    {
        builder.Services.AddDataProtection()
            .UseEphemeralDataProtectionProvider();

        builder.Services.AddAhlenaApi(builder.Configuration);

        using WebApplication migrationHost = builder.Build();
        using IServiceScope migrationScope = migrationHost.Services.CreateScope();
        IServiceProvider migrationServices = migrationScope.ServiceProvider;

        migrationReport =
            await TestUserSeedTargetGuard.ApplyPersistentLocalMigrationsAsync(
                [
                    new LocalFixtureModuleContext("Identity", migrationServices.GetRequiredService<IdentityDbContext>()),
                    new LocalFixtureModuleContext("Cms", migrationServices.GetRequiredService<CmsDbContext>()),
                    new LocalFixtureModuleContext("Caregivers", migrationServices.GetRequiredService<CaregiversDbContext>()),
                    new LocalFixtureModuleContext("Families", migrationServices.GetRequiredService<FamiliesDbContext>()),
                    new LocalFixtureModuleContext("Notifications", migrationServices.GetRequiredService<NotificationsDbContext>()),
                    new LocalFixtureModuleContext("CareHomes", migrationServices.GetRequiredService<CareHomesDbContext>()),
                    new LocalFixtureModuleContext("Finance", migrationServices.GetRequiredService<FinanceDbContext>()),
                    new LocalFixtureModuleContext("Community", migrationServices.GetRequiredService<CommunityDbContext>()),
                    new LocalFixtureModuleContext("Chat", migrationServices.GetRequiredService<ChatDbContext>())
                ],
                expectedPendingIds);
    }

    Console.WriteLine(JsonSerializer.Serialize(
        migrationReport,
        new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    if (!migrationReport.Passed)
    {
        Environment.ExitCode = 1;
    }

    return;
}

// L1 one-shot persistent-local fixture seed: validates the approved target
// through the same guard chain as normal startup (usage gate -> pin ->
// runtime target check), builds the service container without Kestrel and
// without any migration step, runs TestUserDataSeeder directly (Scope
// selects Full/Minimal) and prints a credential-free JSON outcome.
if (args.Contains("--seed-local-fixtures", StringComparer.Ordinal))
{
    builder.Logging.ClearProviders();

    TestUserSeedTargetGuard.RequirePersistentLocalFixtureConfiguration(
        builder.Configuration,
        builder.Environment);
    TestUserSeedTargetGuard.PinToApprovedDatabase(
        builder.Configuration,
        builder.Environment);
    TestUserSeedTargetGuard.EnsureSafeTarget(
        builder.Configuration,
        builder.Environment);

    builder.Services.AddDataProtection()
        .UseEphemeralDataProtectionProvider();

    builder.Services.AddAhlenaApi(builder.Configuration);

    using WebApplication seedHost = builder.Build();
    using IServiceScope seedScope = seedHost.Services.CreateScope();
    TestUserDataSeeder fixtureSeeder =
        seedScope.ServiceProvider.GetRequiredService<TestUserDataSeeder>();
    await fixtureSeeder.SeedAsync();

    Console.WriteLine(JsonSerializer.Serialize(
        new
        {
            command = "seed-local-fixtures",
            passed = true,
            targetMode = "PersistentLocal"
        },
        new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    return;
}

TestUserSeedTargetGuard.PinToApprovedDatabase(
    builder.Configuration,
    builder.Environment);

if (builder.Configuration.GetValue<bool>($"{TestUserSeedOptions.SectionName}:Enabled"))
{
    // Fixture-seeding runs only (guarded to Development + the disposable
    // local target by TestUserSeedTargetGuard). Ephemeral keys are
    // acceptable here and must never be used on a publicly reachable host.
    builder.Services.AddDataProtection()
        .UseEphemeralDataProtectionProvider();
}
else
{
    // Persistent key ring for every other host, including Staging and
    // Production. Chat sync cursors are DataProtection-protected, so old
    // cursor tokens only survive restarts/redeploys with stable keys and a
    // stable application discriminator. Validation refuses ephemeral or
    // release-folder keys outside Development.
    AhlenaDataProtectionOptions dataProtectionOptions =
        builder.Configuration
            .GetSection(AhlenaDataProtectionOptions.SectionName)
            .Get<AhlenaDataProtectionOptions>()
        ?? new AhlenaDataProtectionOptions();

    dataProtectionOptions.Validate(
        builder.Environment.IsDevelopment(),
        builder.Environment.ContentRootPath);

    IDataProtectionBuilder dataProtectionBuilder =
        builder.Services.AddDataProtection();

    dataProtectionBuilder.SetApplicationName(
        dataProtectionOptions.ApplicationName);

    if (!string.IsNullOrWhiteSpace(dataProtectionOptions.KeyRingPath))
    {
        Directory.CreateDirectory(dataProtectionOptions.KeyRingPath);
        dataProtectionBuilder.PersistKeysToFileSystem(
            new DirectoryInfo(dataProtectionOptions.KeyRingPath));
    }
}

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddAhlenaApi(
    builder.Configuration);

var app = builder.Build();

app.UseAhlenaApi();

app.Run();

public partial class Program;
