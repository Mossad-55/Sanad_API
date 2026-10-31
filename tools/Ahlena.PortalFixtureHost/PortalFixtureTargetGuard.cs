using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Ahlena.PortalFixtureHost;

/// <summary>One module database context handed to the read-only target verifier.</summary>
public sealed record PortalFixtureModuleContext(string Module, DbContext Context);

/// <summary>
/// One module verification outcome. Credential-free by construction: only the
/// parsed host/port/database name, the pending migration count and a safe
/// classification are carried.
/// </summary>
public sealed record PortalFixtureModuleVerification(
    string Module,
    string? Host,
    int? Port,
    string? Database,
    int? PendingMigrations,
    bool Passed,
    string? SafeError);

/// <summary>
/// Read-only pre-write verification report for the LOCAL fixture target and for
/// the provider isolation of the host that is about to be built. Deliberately
/// credential-free.
/// <para>
/// Provider isolation is <em>structural</em>, not configuration-derived: the
/// ambient integration observations below are recorded for the operator but
/// never decide pass/fail. A live secret in appsettings or the environment
/// cannot make this host call a provider, because the provider-capable seams
/// are resolved from the DI container and proven to be the scripted doubles.
/// </para>
/// </summary>
public sealed record PortalFixtureTargetReport(
    bool Passed,
    string EnvironmentName,
    IReadOnlyList<string> FailedChecks,
    IReadOnlyList<PortalFixtureModuleVerification> Modules,
    string StorageRootPath,
    bool StorageRootIsLocal,
    bool PaymobSecretKeyObserved,
    bool PaymobLiveKeyObserved,
    bool SmsMisrConfigured,
    bool SmtpEmailConfigured,
    bool PushServiceAccountConfigured);

/// <summary>
/// Fail-closed, read-only guard for the W5 portal fixture host. It proves the
/// exact local target BEFORE any stateful startup: every one of the nine module
/// contexts must target <c>localhost:5432/AhlenaDb</c> and have zero pending
/// migrations, and the environment must be Development. It never writes, never
/// migrates, never creates a database and never emits a connection string or a
/// credential. An unreadable target or an empty/unreadable pending-migration
/// set is a failure, never a silent pass.
/// </summary>
public static class PortalFixtureTargetGuard
{
    /// <summary>The single approved local fixture database for this host.</summary>
    public const string ApprovedDatabase = "AhlenaDb";

    public const int ApprovedPort = 5432;

    public static async Task<PortalFixtureTargetReport> VerifyAsync(
        IConfiguration configuration,
        IHostEnvironment environment,
        IReadOnlyList<PortalFixtureModuleContext> moduleContexts,
        CancellationToken cancellationToken = default)
    {
        List<string> failedChecks = [];
        List<PortalFixtureModuleVerification> modules = [];

        if (!environment.IsDevelopment())
        {
            failedChecks.Add("environment.development");
        }

        foreach (PortalFixtureModuleContext module in moduleContexts)
        {
            PortalFixtureModuleVerification verification = await VerifyModuleAsync(module, cancellationToken);
            modules.Add(verification);

            if (!verification.Passed)
            {
                failedChecks.Add($"module.{module.Module.ToLowerInvariant()}");
            }
        }

        string? paymobSecretKey = configuration["Paymob:SecretKey"];
        bool paymobSecretKeyObserved = !string.IsNullOrWhiteSpace(paymobSecretKey);
        bool paymobLiveKeyObserved = IsLivePaymobKey(paymobSecretKey);

        bool smsMisrConfigured =
            !string.IsNullOrWhiteSpace(configuration["Identity:Sms:SmsMisr:Username"])
            && !string.IsNullOrWhiteSpace(configuration["Identity:Sms:SmsMisr:Password"])
            && !string.IsNullOrWhiteSpace(configuration["Identity:Sms:SmsMisr:Sender"]);

        bool smtpEmailConfigured =
            !string.IsNullOrWhiteSpace(configuration["Identity:Email:Smtp:Host"])
            && !string.IsNullOrWhiteSpace(configuration["Identity:Email:Smtp:FromAddress"]);

        bool pushServiceAccountConfigured =
            !string.IsNullOrWhiteSpace(configuration["Push:Firebase:ServiceAccountJson"]);

        string storageRootPath = configuration["Storage:Local:RootPath"] ?? string.Empty;
        bool storageRootIsLocal =
            !string.IsNullOrWhiteSpace(storageRootPath)
            && Path.IsPathFullyQualified(storageRootPath)
            && !storageRootPath.StartsWith(@"\\", StringComparison.Ordinal)
            && !storageRootPath.Contains("://", StringComparison.Ordinal);
        if (!storageRootIsLocal)
        {
            failedChecks.Add("storage.local-root");
        }

        return new PortalFixtureTargetReport(
            Passed: failedChecks.Count == 0,
            EnvironmentName: environment.EnvironmentName,
            FailedChecks: failedChecks,
            Modules: modules,
            StorageRootPath: storageRootPath,
            StorageRootIsLocal: storageRootIsLocal,
            PaymobSecretKeyObserved: paymobSecretKeyObserved,
            PaymobLiveKeyObserved: paymobLiveKeyObserved,
            SmsMisrConfigured: smsMisrConfigured,
            SmtpEmailConfigured: smtpEmailConfigured,
            PushServiceAccountConfigured: pushServiceAccountConfigured);
    }

    /// <summary>
    /// True only when the effective connection string targets the approved
    /// local fixture database on loopback with the approved port.
    /// </summary>
    public static bool IsApprovedLocalTarget(string? connectionString)
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

        return IsLoopbackHost(parsed.Host)
            && parsed.Port == ApprovedPort
            && string.Equals(parsed.Database, ApprovedDatabase, StringComparison.Ordinal);
    }

    public static bool IsLoopbackHost(string? host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
        || string.Equals(host, "::1", StringComparison.Ordinal);

    /// <summary>
    /// True only for an absolute http(s) URL whose host is loopback. Used to
    /// refuse a listener address that could be reachable off the machine.
    /// </summary>
    public static bool IsLoopbackAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)
            || !Uri.TryCreate(address, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && IsLoopbackHost(uri.Host);
    }

    /// <summary>
    /// Mirrors the production live-key predicate so an observed live key can be
    /// reported. It is an observation only: the host proves isolation by DI
    /// substitution, never by the absence of configuration.
    /// </summary>
    public static bool IsLivePaymobKey(string? secretKey) =>
        secretKey?.StartsWith("sk_live", StringComparison.OrdinalIgnoreCase) == true
        || secretKey?.StartsWith("egy_sk_live_", StringComparison.OrdinalIgnoreCase) == true;

    private static async Task<PortalFixtureModuleVerification> VerifyModuleAsync(
        PortalFixtureModuleContext module,
        CancellationToken cancellationToken)
    {
        string? host = null;
        int? port = null;
        string? database = null;
        int? pendingMigrations = null;
        string? safeError = null;

        try
        {
            string connectionString = module.Context.Database.GetConnectionString()
                ?? throw new InvalidOperationException("No effective connection string.");

            NpgsqlConnectionStringBuilder parsed = new(connectionString);
            host = parsed.Host;
            port = parsed.Port;
            database = parsed.Database;

            pendingMigrations = (await module.Context.Database
                .GetPendingMigrationsAsync(cancellationToken))
                .Count();
        }
        catch (Exception exception)
        {
            safeError = ClassifySafeError(exception);
        }

        bool passed = safeError is null
            && IsLoopbackHost(host)
            && port == ApprovedPort
            && string.Equals(database, ApprovedDatabase, StringComparison.Ordinal)
            // Fail-closed migration evidence: zero pending is required; an
            // unreadable set is a failure (safeError), never an empty pass.
            && pendingMigrations == 0;

        return new PortalFixtureModuleVerification(
            Module: module.Module,
            Host: host,
            Port: port,
            Database: database,
            PendingMigrations: pendingMigrations,
            Passed: passed,
            SafeError: safeError);
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
}

/// <summary>One resolved seam compared against the type this host requires.</summary>
public sealed record PortalFixtureDependencyCheck(
    string Seam,
    string Expected,
    string Actual,
    bool Passed);

/// <summary>
/// Positive DI proof that the container the host is about to serve from has the
/// scripted/inert doubles — not the provider-capable production registrations.
/// It resolves each seam from the REAL container and compares the concrete
/// type, and it lists every remaining <see cref="IHostedService"/> whose
/// implementation is in the application namespace so a background writer that
/// survived removal is visible rather than silent.
/// </summary>
public sealed record PortalFixtureDependencyProof(
    bool Passed,
    IReadOnlyList<PortalFixtureDependencyCheck> Checks,
    IReadOnlyList<string> ApplicationHostedServices)
{
    public static PortalFixtureDependencyProof Prove(
        IServiceCollection descriptors,
        IServiceProvider services)
    {
        List<PortalFixtureDependencyCheck> checks =
        [
            Check<Ahlena.Modules.Families.Application.Abstractions.Payments.IPaymobClient, ScriptedPaymobClient>(services),
            Check<Ahlena.Modules.Finance.Application.ISubscriptionRefundGateway, ScriptedSubscriptionRefundGateway>(services),
            Check<Ahlena.Modules.Identity.Application.Abstractions.Messaging.ISmsSender, PortalFixtureInertSmsSender>(services),
            Check<Ahlena.Modules.Identity.Application.Abstractions.Messaging.IEmailSender, PortalFixtureInertEmailSender>(services),
            Check<Ahlena.Modules.Notifications.Application.Abstractions.Push.IPushSender, PortalFixtureInertPushSender>(services),
            Check<Ahlena.BuildingBlocks.Application.Abstractions.IDateTimeProvider, PortalFixtureClock>(services)
        ];

        // Removed application writers must not survive. Framework hosted
        // services (GenericWebHostService, ConsoleLifetime, options validation)
        // are expected and intentionally kept.
        List<string> applicationHostedServices = descriptors
            .Where(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType?.Namespace?.StartsWith("Ahlena.", StringComparison.Ordinal) == true)
            .Select(descriptor => descriptor.ImplementationType!.FullName ?? descriptor.ImplementationType.Name)
            .ToList();

        return new PortalFixtureDependencyProof(
            Passed: checks.All(check => check.Passed) && applicationHostedServices.Count == 0,
            Checks: checks,
            ApplicationHostedServices: applicationHostedServices);
    }

    private static PortalFixtureDependencyCheck Check<TSeam, TExpected>(IServiceProvider services)
        where TSeam : class
        where TExpected : TSeam
    {
        object? resolved = services.GetService<TSeam>();
        string actual = resolved?.GetType().Name ?? "(none)";
        return new PortalFixtureDependencyCheck(
            Seam: typeof(TSeam).Name,
            Expected: typeof(TExpected).Name,
            Actual: actual,
            Passed: resolved is TExpected);
    }
}
