using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Infrastructure.Storage;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;

namespace Sanad.API.Seeding;

/// <summary>Database target mode for opt-in fixture seeding.</summary>
public enum TestUserSeedTargetMode
{
    /// <summary>Existing automated-test guard: localhost:5432/SanadBrunoTestDb.</summary>
    BrunoTest,

    /// <summary>Persistent-local fixtures: localhost:5432/SanadDb, validate-only.</summary>
    PersistentLocal
}

/// <summary>One module database context handed to the read-only target verifier.</summary>
public sealed record LocalFixtureModuleContext(string Module, DbContext Context);

/// <summary>
/// Read-only verification result for one module context. Only safe fields are
/// carried: host, port, database name, the pending EF migration ids, a failure
/// classification that never embeds an exception message, and pass/fail.
/// Migration ids are the repository's own migration names; they carry no
/// credentials.
/// </summary>
public sealed record LocalFixtureModuleVerification(
    string Module,
    string? Host,
    int? Port,
    string? Database,
    int? PendingMigrations,
    IReadOnlyList<string> PendingMigrationIds,
    bool Passed,
    string? SafeError);

/// <summary>
/// One module's migration-application outcome. Reports the pending ids read
/// immediately before the apply, whether the apply ran, and a failure
/// classification that never embeds an exception message.
/// </summary>
public sealed record LocalFixtureMigrationApplication(
    string Module,
    int PendingBefore,
    bool Applied,
    IReadOnlyList<string> AppliedMigrationIds,
    string? SafeError);

/// <summary>
/// Credential-free outcome of the Owner-authorized persistent-local migration
/// application. <see cref="Passed"/> is false when the observed pending set did
/// not match the expectation, in which case nothing was applied at all.
/// </summary>
public sealed record LocalFixtureMigrationApplyReport(
    bool Passed,
    string TargetDatabase,
    bool AppliedAnyMigrations,
    IReadOnlyList<LocalFixtureMigrationApplication> Modules,
    IReadOnlyList<string> FailedChecks);

/// <summary>
/// Read-only pre-write verification report for the persistent-local fixture
/// target. The shape is deliberately credential-free: no usernames,
/// passwords or connection strings can be produced by the verifier.
/// </summary>
public sealed record LocalFixtureTargetReport(
    bool Passed,
    string EnvironmentName,
    string TargetMode,
    IReadOnlyList<string> FailedChecks,
    IReadOnlyList<LocalFixtureModuleVerification> Modules,
    string StorageRootPath,
    bool StorageRootExists,
    bool PaymobLiveKey,
    string? PaymobBaseUrlHost,
    bool SmsMisrConfigured,
    bool SmtpEmailConfigured,
    bool PushServiceAccountConfigured,
    string PushSenderMode,
    int IdentityUserCount,
    int FixtureIdentityPresentCount,
    int ActiveFixtureSessionCount);

public static class TestUserSeedTargetGuard
{
    private const string BrunoTestDatabase = "SanadBrunoTestDb";
    private const string PersistentLocalDatabase = "SanadDb";
    private const int AllowedPort = 5432;

    // Every module falls back to IdentityDatabase when its own connection
    // string is absent, so both modes validate all nine module contexts
    // instead of inferring targets from names.
    private static readonly string[] ModuleConnectionNames =
    [
        "IdentityDatabase",
        "CmsDatabase",
        "CaregiversDatabase",
        "FamiliesDatabase",
        "NotificationsDatabase",
        "CareHomesDatabase",
        "FinanceDatabase",
        "CommunityDatabase",
        "ChatDatabase"
    ];

    /// <summary>
    /// First guard entry point (program start). Disabled seeds return without
    /// inspecting any configuration. In <see cref="TestUserSeedTargetMode.BrunoTest"/>
    /// mode this keeps the existing behavior: every configured module
    /// connection is rewritten to the disposable Bruno test database, only
    /// after all of them are proven to be local. In
    /// <see cref="TestUserSeedTargetMode.PersistentLocal"/> mode this only
    /// validates the approved persistent-local target and never mutates
    /// configuration or touches a database.
    /// </summary>
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
        TestUserSeedTargetMode mode = ResolveTargetMode(configuration);

        if (mode == TestUserSeedTargetMode.PersistentLocal)
        {
            AssertPersistentLocalTargets(configuration, identity);
            return;
        }

        List<KeyValuePair<string, string>> pinned = [];
        foreach (string name in ModuleConnectionNames)
        {
            string? connectionString = configuration.GetConnectionString(name);
            if (connectionString is null && name != "IdentityDatabase")
            {
                continue;
            }

            connectionString ??= identity;
            NpgsqlConnectionStringBuilder parsed = ParseAndValidateLocalServer(connectionString);
            parsed.Database = BrunoTestDatabase;
            pinned.Add(new KeyValuePair<string, string>($"ConnectionStrings:{name}", parsed.ConnectionString));
        }

        foreach (KeyValuePair<string, string> item in pinned)
        {
            configuration[item.Key] = item.Value;
        }
    }

    /// <summary>
    /// Second guard entry point (application build). The Bruno path is the
    /// existing automated-test guard, extended to check every module context
    /// against the same allowed database and to create only that disposable
    /// database. The persistent-local path is validate-only: no configuration
    /// mutation, no database creation, no connection of any kind.
    /// </summary>
    public static void EnsureSafeTarget(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!configuration.GetValue<bool>($"{TestUserSeedOptions.SectionName}:Enabled"))
        {
            return;
        }

        EnsureDevelopmentAndNonLivePayments(configuration, environment);

        TestUserSeedTargetMode mode = ResolveTargetMode(configuration);
        string identity = RequiredConnection(configuration, "IdentityDatabase");

        if (mode == TestUserSeedTargetMode.PersistentLocal)
        {
            // Validate-only: correct host/port/database per module or the
            // persistent-local violation message. Never creates a database,
            // never mutates configuration.
            AssertPersistentLocalTargets(configuration, identity);
            return;
        }

        foreach (string name in ModuleConnectionNames)
        {
            string? connectionString = configuration.GetConnectionString(name);
            if (connectionString is null && name != "IdentityDatabase")
            {
                connectionString = identity;
            }

            NpgsqlConnectionStringBuilder parsed = ParseAndValidateLocalServer(connectionString!);

            if (!string.Equals(parsed.Database, BrunoTestDatabase, StringComparison.Ordinal))
            {
                throw UnsafeTarget();
            }
        }

        EnsureTargetDatabaseExists(identity);
    }

    /// <summary>
    /// True only for an enabled persistent-local fixture run; a disabled seed
    /// never inspects the mode, and unknown modes fail closed here as well.
    /// </summary>
    public static bool IsPersistentLocalFixtureRun(IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>($"{TestUserSeedOptions.SectionName}:Enabled"))
        {
            return false;
        }

        return ResolveTargetMode(configuration) == TestUserSeedTargetMode.PersistentLocal;
    }

    /// <summary>
    /// Usage gate for the read-only verification and one-shot seed commands:
    /// the fixture seed must be explicitly enabled in Development with the
    /// persistent-local target mode, and the configuration-level nine-module
    /// target assertion must pass before any database connection is opened.
    /// </summary>
    public static void RequirePersistentLocalFixtureConfiguration(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!configuration.GetValue<bool>($"{TestUserSeedOptions.SectionName}:Enabled"))
        {
            throw new InvalidOperationException(
                "Local fixture verification requires App:TestUserSeed:Enabled=true.");
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "App:TestUserSeed may only be enabled in Development.");
        }

        if (ResolveTargetMode(configuration) != TestUserSeedTargetMode.PersistentLocal)
        {
            throw new InvalidOperationException(
                "Local fixture verification requires App:TestUserSeed:TargetMode=PersistentLocal.");
        }

        string identity = RequiredConnection(configuration, "IdentityDatabase");
        AssertPersistentLocalTargets(configuration, identity);
    }

    /// <summary>
    /// Read-only verification of the persistent-local fixture target. For each
    /// module context it parses the effective runtime connection (never the
    /// name), confirms localhost:5432/SanadDb, and reads the EF migration
    /// history to count pending migrations. It also reports storage root,
    /// non-live integration state and current fixture record counts. It never
    /// applies a migration, never writes, and never emits credentials:
    /// failures are classified without exception messages because database
    /// exceptions can embed connection details.
    /// </summary>
    public static async Task<LocalFixtureTargetReport> VerifyPersistentLocalRuntimeAsync(
        IConfiguration configuration,
        IHostEnvironment environment,
        IReadOnlyList<LocalFixtureModuleContext> moduleContexts,
        IdentityDbContext identityDbContext,
        CancellationToken cancellationToken = default)
    {
        List<string> failedChecks = [];
        List<LocalFixtureModuleVerification> modules = [];

        if (!configuration.GetValue<bool>($"{TestUserSeedOptions.SectionName}:Enabled"))
        {
            failedChecks.Add("config.fixture-seed-enabled");
        }

        if (!string.Equals(
                configuration[$"{TestUserSeedOptions.SectionName}:TargetMode"],
                TestUserSeedOptions.TargetModePersistentLocal,
                StringComparison.OrdinalIgnoreCase))
        {
            failedChecks.Add("config.persistent-local-target-mode");
        }

        if (!environment.IsDevelopment())
        {
            failedChecks.Add("environment.development");
        }

        try
        {
            string identityForTargets = RequiredConnection(configuration, "IdentityDatabase");
            AssertPersistentLocalTargets(configuration, identityForTargets);
        }
        catch (InvalidOperationException)
        {
            failedChecks.Add("config.nine-module-targets");
        }

        foreach (LocalFixtureModuleContext module in moduleContexts)
        {
            LocalFixtureModuleVerification verification = await VerifyModuleAsync(
                module,
                cancellationToken);
            modules.Add(verification);

            if (!verification.Passed)
            {
                failedChecks.Add($"module.{module.Module.ToLowerInvariant()}");
            }
        }

        string? paymobSecretKey = configuration["Paymob:SecretKey"];
        bool paymobLiveKey = paymobSecretKey?.StartsWith("sk_live", StringComparison.OrdinalIgnoreCase) == true;
        if (paymobLiveKey)
        {
            failedChecks.Add("integrations.paymob-live-key");
        }

        string? paymobBaseUrl = configuration["Paymob:BaseUrl"];
        string? paymobBaseUrlHost = null;
        if (!string.IsNullOrWhiteSpace(paymobBaseUrl))
        {
            paymobBaseUrlHost = Uri.TryCreate(paymobBaseUrl, UriKind.Absolute, out Uri? uri)
                ? uri.Host
                : "(unparseable)";
        }

        // Mirror DependencyInjection: SMS Misr or SMTP configuration means a
        // live provider could be contacted, so the verification process must
        // run with those cleared (the workflow script clears them).
        bool smsMisrConfigured =
            !string.IsNullOrWhiteSpace(configuration["Identity:Sms:SmsMisr:Username"])
            && !string.IsNullOrWhiteSpace(configuration["Identity:Sms:SmsMisr:Password"])
            && !string.IsNullOrWhiteSpace(configuration["Identity:Sms:SmsMisr:Sender"]);
        if (smsMisrConfigured)
        {
            failedChecks.Add("integrations.sms-misr-not-cleared");
        }

        bool smtpEmailConfigured =
            !string.IsNullOrWhiteSpace(configuration["Identity:Email:Smtp:Host"])
            && !string.IsNullOrWhiteSpace(configuration["Identity:Email:Smtp:FromAddress"]);
        if (smtpEmailConfigured)
        {
            failedChecks.Add("integrations.smtp-email-not-cleared");
        }

        bool pushServiceAccountConfigured =
            !string.IsNullOrWhiteSpace(configuration["Push:Firebase:ServiceAccountJson"]);

        // DependencyInjection selects DevelopmentNoOpPushSender for every
        // Development host, so push is structurally non-live in this mode.
        string pushSenderMode = environment.IsDevelopment() ? "DevelopmentNoOp" : "Fcm";

        LocalStorageOptions storageOptions =
            configuration.GetSection(LocalStorageOptions.SectionName).Get<LocalStorageOptions>()
            ?? new LocalStorageOptions();
        string storageRootPath = storageOptions.GetEffectiveRootPath();
        bool storageRootIsLocal =
            Path.IsPathFullyQualified(storageRootPath)
            && !storageRootPath.StartsWith(@"\\", StringComparison.Ordinal)
            && !storageRootPath.Contains("://", StringComparison.Ordinal);
        if (!storageRootIsLocal)
        {
            failedChecks.Add("storage.local-root");
        }

        bool storageRootExists = Directory.Exists(storageRootPath);

        int identityUserCount;
        int fixtureIdentityPresentCount;
        int activeFixtureSessionCount;
        try
        {
            List<User> users = await identityDbContext.Users
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            identityUserCount = users.Count;

            IReadOnlyList<string> fixtureEmails = TestUserDataSeeder.MinimalFixtureIdentityEmails;
            List<UserId> fixtureUserIds = users
                .Where(user => user.Email is not null && fixtureEmails.Contains(user.Email.Value))
                .Select(user => user.Id)
                .ToList();
            fixtureIdentityPresentCount = fixtureUserIds.Count;

            DateTime utcNow = DateTime.UtcNow;
            activeFixtureSessionCount = fixtureUserIds.Count == 0
                ? 0
                : await identityDbContext.DeviceSessions
                    .CountAsync(
                        session => fixtureUserIds.Contains(session.UserId)
                            && session.RevokedOnUtc == null
                            && session.ExpiresOnUtc > utcNow,
                        cancellationToken);
        }
        catch (Exception exception)
        {
            failedChecks.Add("records.identity-read");
            identityUserCount = -1;
            fixtureIdentityPresentCount = -1;
            activeFixtureSessionCount = -1;
            _ = exception; // classified without its message: messages may embed connection details.
        }

        return new LocalFixtureTargetReport(
            Passed: failedChecks.Count == 0,
            EnvironmentName: environment.EnvironmentName,
            TargetMode: configuration[$"{TestUserSeedOptions.SectionName}:TargetMode"] ?? string.Empty,
            FailedChecks: failedChecks,
            Modules: modules,
            StorageRootPath: storageRootPath,
            StorageRootExists: storageRootExists,
            PaymobLiveKey: paymobLiveKey,
            PaymobBaseUrlHost: paymobBaseUrlHost,
            SmsMisrConfigured: smsMisrConfigured,
            SmtpEmailConfigured: smtpEmailConfigured,
            PushServiceAccountConfigured: pushServiceAccountConfigured,
            PushSenderMode: pushSenderMode,
            IdentityUserCount: identityUserCount,
            FixtureIdentityPresentCount: fixtureIdentityPresentCount,
            ActiveFixtureSessionCount: activeFixtureSessionCount);
    }

    private static async Task<LocalFixtureModuleVerification> VerifyModuleAsync(
        LocalFixtureModuleContext module,
        CancellationToken cancellationToken)
    {
        string? host = null;
        int? port = null;
        string? database = null;
        int? pendingMigrations = null;
        IReadOnlyList<string> pendingMigrationIds = [];
        string? safeError = null;

        try
        {
            string connectionString = module.Context.Database.GetConnectionString()
                ?? throw new InvalidOperationException("No effective connection string.");
            NpgsqlConnectionStringBuilder parsed = new(connectionString);
            host = parsed.Host;
            port = parsed.Port;
            database = parsed.Database;

            string[] pending = (await module.Context.Database
                .GetPendingMigrationsAsync(cancellationToken))
                .ToArray();

            pendingMigrations = pending.Length;
            pendingMigrationIds = pending;
        }
        catch (Exception exception)
        {
            safeError = ClassifySafeError(exception);
        }

        bool passed = safeError is null
            && host is not null
            && port is not null
            && IsLocalHost(host)
            && port == AllowedPort
            && string.Equals(database, PersistentLocalDatabase, StringComparison.Ordinal)
            && pendingMigrations == 0;

        return new LocalFixtureModuleVerification(
            Module: module.Module,
            Host: host,
            Port: port,
            Database: database,
            PendingMigrations: pendingMigrations,
            PendingMigrationIds: pendingMigrationIds,
            Passed: passed,
            SafeError: safeError);
    }

    /// <summary>
    /// True only when the effective connection string targets the approved
    /// persistent-local server, port and database. Used by both the read-only
    /// report and the migration application so an unexpected target can never
    /// be migrated, and never reports connection details.
    /// </summary>
    public static bool IsApprovedPersistentLocalTarget(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        NpgsqlConnectionStringBuilder parsed;
        try
        {
            parsed = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            return false;
        }

        return IsLocalHost(parsed.Host)
            && parsed.Port == AllowedPort
            && string.Equals(parsed.Database, PersistentLocalDatabase, StringComparison.Ordinal);
    }

    /// <summary>
    /// True only when a token can be an EF migration id: non-empty, no
    /// whitespace, and only ASCII letters, digits or underscores. Rejects a
    /// malformed expectation before it can ever be compared with a real set.
    /// </summary>
    public static bool IsValidMigrationId(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    /// <summary>
    /// Parses the <c>--expect</c> argument into an exact expected pending set:
    /// comma-separated <c>Module=id;id;id</c> entries, where <c>Module=</c>
    /// means the module is expected to have no pending migrations. Returns false
    /// with a safe, credential-free classification when the argument is missing,
    /// an entry has no module name, a module repeats, a comma entry is empty
    /// (leading, trailing or repeated commas), or an id is malformed or
    /// duplicated. Never returns a partial expectation.
    /// </summary>
    public static bool TryParseExpectedPendingIds(
        string? argument,
        out Dictionary<string, IReadOnlyList<string>> expectedPendingIds,
        out string safeProblem)
    {
        expectedPendingIds = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        safeProblem = "expectation.not-provided";

        if (string.IsNullOrWhiteSpace(argument))
        {
            return false;
        }

        Dictionary<string, IReadOnlyList<string>> parsed = new(StringComparer.Ordinal);

        // Empty entries are kept, not dropped: a stray leading, trailing or
        // repeated comma is a malformed expectation and must fail closed rather
        // than be silently accepted as if it were absent.
        foreach (string entry in argument.Split(',', StringSplitOptions.TrimEntries))
        {
            if (entry.Length == 0)
            {
                safeProblem = "expectation.malformed-entry";
                return false;
            }

            int separator = entry.IndexOf('=');
            if (separator <= 0)
            {
                safeProblem = "expectation.malformed-entry";
                return false;
            }

            string module = entry[..separator].Trim();
            if (module.Length == 0)
            {
                safeProblem = "expectation.malformed-entry";
                return false;
            }

            List<string> ids = [];

            string idList = entry[(separator + 1)..].Trim();
            if (idList.Length > 0)
            {
                foreach (string id in idList.Split(';', StringSplitOptions.TrimEntries))
                {
                    if (!IsValidMigrationId(id))
                    {
                        safeProblem = "expectation.malformed-migration-id";
                        return false;
                    }

                    if (ids.Contains(id, StringComparer.Ordinal))
                    {
                        safeProblem = "expectation.duplicate-migration-id";
                        return false;
                    }

                    ids.Add(id);
                }
            }

            if (!parsed.TryAdd(module, ids))
            {
                safeProblem = "expectation.duplicate-module";
                return false;
            }
        }

        if (parsed.Count == 0)
        {
            return false;
        }

        expectedPendingIds = parsed;
        safeProblem = string.Empty;
        return true;
    }

    /// <summary>
    /// Validates the shape of an expected pending set without any database
    /// access: it must be non-empty, name exactly the supplied modules, and
    /// contain no duplicate id. Returns a safe classification, or null when the
    /// expectation is complete and well formed.
    /// </summary>
    public static string? DescribeExpectationShape(
        IReadOnlyDictionary<string, IReadOnlyList<string>> expectedPendingIds,
        IReadOnlyList<string> moduleNames)
    {
        if (expectedPendingIds.Count == 0)
        {
            return "expectation.not-provided";
        }

        if (expectedPendingIds.Count != moduleNames.Count)
        {
            return "expectation.module-set-mismatch";
        }

        foreach (string moduleName in moduleNames)
        {
            if (!expectedPendingIds.ContainsKey(moduleName))
            {
                return "expectation.module-set-mismatch";
            }
        }

        foreach (IReadOnlyList<string> ids in expectedPendingIds.Values)
        {
            if (ids.Count != ids.Distinct(StringComparer.Ordinal).Count())
            {
                return "expectation.duplicate-migration-id";
            }
        }

        return null;
    }

    /// <summary>
    /// Comparison of one module's observed pending migration ids against the
    /// Owner-authorized expectation. The ids are compared as an exact set:
    /// identical membership for the same cardinality, so a missing, extra or
    /// substituted id fails even when the count is unchanged, and a duplicated
    /// observed id fails too. Returns a safe classification, or null on an exact
    /// match.
    /// </summary>
    public static string? DescribePendingSetMismatch(
        string module,
        IReadOnlyList<string> pendingMigrationIds,
        IReadOnlyDictionary<string, IReadOnlyList<string>> expectedPendingIds)
    {
        if (!expectedPendingIds.TryGetValue(module, out IReadOnlyList<string>? expected))
        {
            return "unexpected-module";
        }

        if (pendingMigrationIds.Count != pendingMigrationIds.Distinct(StringComparer.Ordinal).Count())
        {
            return "pending-set-mismatch";
        }

        if (pendingMigrationIds.Count != expected.Count)
        {
            return "pending-set-mismatch";
        }

        foreach (string id in expected)
        {
            if (!pendingMigrationIds.Contains(id, StringComparer.Ordinal))
            {
                return "pending-set-mismatch";
            }
        }

        return null;
    }

    /// <summary>
    /// Owner-authorized, expectation-gated application of the persistent-local
    /// pending migrations. Two phases: the expectation shape is validated
    /// offline, then every module is re-validated as the approved
    /// `localhost:5432/SanadDb` target and its pending migration ids are read and
    /// compared with <paramref name="expectedPendingIds"/> as an exact set; only
    /// when the whole observed set matches does the second phase call
    /// <c>MigrateAsync</c>. Any mismatch (including a same-count substitution),
    /// unexpected target, unreadable module or unknown module name leaves every
    /// database untouched and fails closed. No migration is ever reverted and no
    /// data is deleted by this method; it only runs the repository's authored
    /// migrations.
    /// </summary>
    public static async Task<LocalFixtureMigrationApplyReport> ApplyPersistentLocalMigrationsAsync(
        IReadOnlyList<LocalFixtureModuleContext> moduleContexts,
        IReadOnlyDictionary<string, IReadOnlyList<string>> expectedPendingIds,
        CancellationToken cancellationToken = default)
    {
        List<string> failedChecks = [];
        List<LocalFixtureMigrationApplication> results = [];
        List<(LocalFixtureModuleContext Module, string[] Pending)> ready = [];

        // An expectation that is empty, malformed or does not name exactly the
        // supplied module set cannot be trusted to gate the apply; refuse
        // before any connection.
        string? expectationProblem = DescribeExpectationShape(
            expectedPendingIds,
            moduleContexts.Select(module => module.Module).ToArray());

        if (expectationProblem is not null)
        {
            return new LocalFixtureMigrationApplyReport(
                Passed: false,
                TargetDatabase: PersistentLocalDatabase,
                AppliedAnyMigrations: false,
                Modules: [],
                FailedChecks: [expectationProblem]);
        }

        foreach (LocalFixtureModuleContext module in moduleContexts)
        {
            string? connectionString = null;
            string[]? pending = null;
            string? safeError = null;

            try
            {
                connectionString = module.Context.Database.GetConnectionString();

                if (!IsApprovedPersistentLocalTarget(connectionString))
                {
                    safeError = "unexpected-target";
                }
                else
                {
                    pending = (await module.Context.Database
                        .GetPendingMigrationsAsync(cancellationToken))
                        .ToArray();
                }
            }
            catch (Exception exception)
            {
                safeError = ClassifySafeError(exception);
            }

            if (safeError is null && pending is not null)
            {
                safeError = DescribePendingSetMismatch(
                    module.Module,
                    pending,
                    expectedPendingIds);
            }

            if (safeError is not null)
            {
                failedChecks.Add($"module.{module.Module.ToLowerInvariant()}");
                results.Add(new LocalFixtureMigrationApplication(
                    Module: module.Module,
                    PendingBefore: pending?.Length ?? -1,
                    Applied: false,
                    AppliedMigrationIds: [],
                    SafeError: safeError));
                continue;
            }

            ready.Add((module, pending!));
        }

        if (failedChecks.Count > 0)
        {
            return new LocalFixtureMigrationApplyReport(
                Passed: false,
                TargetDatabase: PersistentLocalDatabase,
                AppliedAnyMigrations: false,
                Modules: results,
                FailedChecks: failedChecks);
        }

        foreach ((LocalFixtureModuleContext module, string[] pending) in ready)
        {
            try
            {
                await module.Context.Database.MigrateAsync(cancellationToken);

                results.Add(new LocalFixtureMigrationApplication(
                    Module: module.Module,
                    PendingBefore: pending.Length,
                    Applied: true,
                    AppliedMigrationIds: pending,
                    SafeError: null));
            }
            catch (Exception exception)
            {
                failedChecks.Add($"module.{module.Module.ToLowerInvariant()}");
                results.Add(new LocalFixtureMigrationApplication(
                    Module: module.Module,
                    PendingBefore: pending.Length,
                    Applied: false,
                    AppliedMigrationIds: [],
                    SafeError: ClassifySafeError(exception)));
            }
        }

        return new LocalFixtureMigrationApplyReport(
            Passed: failedChecks.Count == 0,
            TargetDatabase: PersistentLocalDatabase,
            AppliedAnyMigrations: results.Any(result => result.Applied),
            Modules: results,
            FailedChecks: failedChecks);
    }

    /// <summary>
    /// Failure classes only — never exception messages, which can embed
    /// usernames, hosts or connection details.
    /// </summary>
    private static string ClassifySafeError(Exception exception) =>
        exception switch
        {
            PostgresException postgres => $"postgres-{postgres.SqlState}",
            NpgsqlException => "postgres-connection-failure",
            InvalidOperationException => "verification-invalid-state",
            _ => "verification-failed"
        };

    private static void AssertPersistentLocalTargets(IConfiguration configuration, string identity)
    {
        foreach (string name in ModuleConnectionNames)
        {
            string? connectionString = configuration.GetConnectionString(name);
            if (connectionString is null && name != "IdentityDatabase")
            {
                connectionString = identity;
            }

            // Any deviation (host, port or database) is the persistent-local
            // violation it is - never the BrunoTest message, which would
            // misidentify the approved target in this mode.
            if (!IsApprovedPersistentLocalTarget(connectionString))
            {
                throw UnsafePersistentLocalTarget();
            }
        }
    }

    private static TestUserSeedTargetMode ResolveTargetMode(IConfiguration configuration)
    {
        string? raw = configuration[$"{TestUserSeedOptions.SectionName}:TargetMode"];

        if (string.IsNullOrWhiteSpace(raw)
            || string.Equals(raw, TestUserSeedOptions.TargetModeBrunoTest, StringComparison.OrdinalIgnoreCase))
        {
            return TestUserSeedTargetMode.BrunoTest;
        }

        if (string.Equals(raw, TestUserSeedOptions.TargetModePersistentLocal, StringComparison.OrdinalIgnoreCase))
        {
            return TestUserSeedTargetMode.PersistentLocal;
        }

        throw new InvalidOperationException(
            "App:TestUserSeed:TargetMode must be 'BrunoTest' or 'PersistentLocal'.");
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

    private static InvalidOperationException UnsafePersistentLocalTarget() =>
        new("Persistent-local fixture seeding is allowed only when every configured module database targets localhost:5432/SanadDb. No connection details were logged.");

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
            exists.Parameters.AddWithValue("database", BrunoTestDatabase);
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
