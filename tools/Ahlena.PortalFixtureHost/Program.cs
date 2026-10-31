using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ahlena.API;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;
using Ahlena.Modules.Caregivers.Infrastructure.Persistence;
using Ahlena.Modules.Chat.Infrastructure.Persistence;
using Ahlena.Modules.Cms.Infrastructure.Persistence;
using Ahlena.Modules.Community.Infrastructure.Persistence;
using Ahlena.Modules.Families.Application.Abstractions.Payments;
using Ahlena.Modules.Families.Infrastructure.Persistence;
using Ahlena.Modules.Finance.Application;
using Ahlena.Modules.Finance.Infrastructure;
using Ahlena.Modules.Identity.Application.Abstractions.Messaging;
using Ahlena.Modules.Identity.Infrastructure.Persistence;
using Ahlena.Modules.Notifications.Application.Abstractions.Push;
using Ahlena.Modules.Notifications.Infrastructure.Persistence;
using Ahlena.PortalFixtureHost;

// Bounded test-only Kestrel host for the W5 portal-report fixture pack.
//
// It composes the REAL Ahlena.API service registrations and pipeline, exposes
// the real controllers/handlers on loopback, and substitutes ONLY the
// provider-capable seams (Families/CareHomes Paymob client, the Finance
// subscription-refund gateway, SMS, SMTP e-mail, push and the clock) with
// explicit scripted/inert local doubles. Background writers, the production
// seeder and every live provider path are disabled and PROVEN disabled by DI
// resolution BEFORE the listener or the provisioner runs. It never applies a
// migration and never contacts a provider, regardless of appsettings or user
// secrets: isolation is structural, not config-derived.
//
// Modes
//   --mode plan        Deterministic, side-effect-free description of the host
//                      contract. No configuration, no credential, no listener.
//   --mode preflight   Builds the container, PROVES the local target and the
//                      DI isolation, prints a JSON report, exits. Never listens.
//   --mode start       preflight, then provisions the bounded fixture and
//                      serves the portal on the loopback address. Blocking.
//   --mode reuse       Probes an already-running listener and reports whether
//                      it is THIS fixture host for the pinned revision.
//   --mode selfcheck   Runs the shared fixture domain sequences in memory and
//                      prints a JSON result. No credential, no configuration, no
//                      database, no listener and no provider call.

var options = PortalFixtureHostOptions.Parse(args);

if (options.ShowHelp)
{
    Console.WriteLine(PortalFixtureHostOptions.Usage);
    return 0;
}

if (options.Mode == PortalFixtureHostMode.Plan)
{
    // No configuration, no credential, no database, no listener.
    Console.WriteLine(JsonSerializer.Serialize(
        new
        {
            host = PortalFixtureHostOptions.IdentityMarker,
            mode = "Plan",
            addressBound = "loopback only (localhost / 127.0.0.1 / ::1)",
            target = $"localhost:{PortalFixtureTargetGuard.ApprovedPort}/{PortalFixtureTargetGuard.ApprovedDatabase}",
            paymentDoubles = new[]
            {
                "Ahlena.Modules.Families.Application.Abstractions.Payments.IPaymobClient -> ScriptedPaymobClient",
                "Ahlena.Modules.Finance.Application.ISubscriptionRefundGateway -> ScriptedSubscriptionRefundGateway"
            },
            inertDoubles = new[]
            {
                "Ahlena.Modules.Identity.Application.Abstractions.Messaging.ISmsSender -> PortalFixtureInertSmsSender",
                "Ahlena.Modules.Identity.Application.Abstractions.Messaging.IEmailSender -> PortalFixtureInertEmailSender",
                "Ahlena.Modules.Notifications.Application.Abstractions.Push.IPushSender -> PortalFixtureInertPushSender",
                "Ahlena.BuildingBlocks.Application.Abstractions.IDateTimeProvider -> PortalFixtureClock (test-host-only)"
            },
            disabled = new[]
            {
                "startup migrations", "production seeder", "all Ahlena background writers",
                "live Paymob", "live SMS (SmsMisr)", "live SMTP email", "live push (FCM)"
            },
            fixtureGroups = PortalFixtureGroups.All,
            modes = new[] { "plan", "preflight", "start", "reuse", "selfcheck" }
        },
        JsonDefaults.Web));
    return 0;
}

if (options.Mode == PortalFixtureHostMode.Reuse)
{
    return await RunReuseProbeAsync(options);
}

if (options.Mode == PortalFixtureHostMode.SelfCheck)
{
    // Side-effect-free in-process proof that the shared domain drivers produce
    // the requested money states. It touches no credential, opens no connection,
    // starts no listener and makes no provider call.
    try
    {
        PortalFixtureDomainChecks.PortalFixtureDomainCheckReport check =
            PortalFixtureDomainChecks.Run(options.InstantUtc ?? DateTime.UtcNow);

        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                host = PortalFixtureHostOptions.IdentityMarker,
                mode = "selfcheck",
                passed = check.Passed,
                checks = check.Checks
            },
            JsonDefaults.Web));

        return check.Passed ? 0 : 1;
    }
    catch (Exception exception)
    {
        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                host = PortalFixtureHostOptions.IdentityMarker,
                mode = "selfcheck",
                passed = false,
                reason = PortalFixtureHostOptions.SafeMessage(exception)
            },
            JsonDefaults.Web));

        return 1;
    }
}

// preflight and start both need the private runtime credential file.
PortalFixtureSecrets secrets;
try
{
    secrets = PortalFixtureSecrets.Load(options.CredentialFile);
}
catch (Exception exception)
{
    Console.Error.WriteLine($"credential file refused: {PortalFixtureHostOptions.SafeMessage(exception)}");
    return 2;
}

string workRoot = options.WorkRoot
    ?? Path.Combine(Path.GetTempPath(), "opencode", "sanad-portal-fixture");

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = [],
    EnvironmentName = "Development",
    ContentRootPath = AppContext.BaseDirectory
});

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();

builder.Configuration.AddInMemoryCollection(BuildLocalConfiguration(secrets, workRoot));

builder.Services.AddAhlenaApi(builder.Configuration);

// Disable every Ahlena background writer (outboxes, expiry/notification
// processors, chat dispatch/cleanup). Framework hosted services
// (GenericWebHostService, ConsoleLifetime, the options startup validator,
// which starts the server and validates options) are preserved.
RemoveAhlenaHostedServices(builder.Services);

// Positive DI substitution of the provider-capable seams with explicit local
// doubles. This is a removal + replacement, never configuration inference: the
// real PaymobClient, SMTP/SMS senders and FCM sender cannot be resolved on this
// host even if their credentials are configured.
builder.Services.RemoveAll<IPaymobClient>();
builder.Services.RemoveAll<ISubscriptionRefundGateway>();
builder.Services.RemoveAll<ISmsSender>();
builder.Services.RemoveAll<IEmailSender>();
builder.Services.RemoveAll<IPushSender>();
builder.Services.RemoveAll<IDateTimeProvider>();

builder.Services.AddSingleton<ScriptedPaymobClient>();
builder.Services.AddSingleton<IPaymobClient>(sp => sp.GetRequiredService<ScriptedPaymobClient>());
builder.Services.AddScoped<ISubscriptionRefundGateway>(sp =>
    new ScriptedSubscriptionRefundGateway(sp.GetRequiredService<ScriptedPaymobClient>()));
builder.Services.AddSingleton<ISmsSender, PortalFixtureInertSmsSender>();
builder.Services.AddSingleton<IEmailSender, PortalFixtureInertEmailSender>();
builder.Services.AddSingleton<IPushSender, PortalFixtureInertPushSender>();

// Test-host-only clock. The instant is explicit, and the guide records the
// honest limit: domain setters that call DateTime.UtcNow directly are not
// controlled by this seam.
DateTime instantUtc = options.InstantUtc ?? DateTime.UtcNow;
builder.Services.AddSingleton<IDateTimeProvider>(new PortalFixtureClock(instantUtc));

// Ephemeral keys are acceptable on this bounded local fixture host only.
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();

if (options.Mode == PortalFixtureHostMode.Start)
{
    builder.WebHost.UseUrls(options.Address);
}

WebApplication app = builder.Build();

PortalFixtureProvisionReport? provisionReport = null;

using (IServiceScope scope = app.Services.CreateScope())
{
    IServiceProvider services = scope.ServiceProvider;

    // 1. Read-only proof of the exact local target, over the EFFECTIVE module
    //    connection strings, BEFORE any stateful startup.
    PortalFixtureTargetReport targetReport = await PortalFixtureTargetGuard.VerifyAsync(
        app.Configuration,
        app.Environment,
        BuildModuleContexts(services));

    // 2. Positive DI proof: the seams the host will serve from resolve to the
    //    explicit doubles and no application background writer survived.
    PortalFixtureDependencyProof dependencyProof =
        PortalFixtureDependencyProof.Prove(builder.Services, services);

    bool ok = targetReport.Passed && dependencyProof.Passed;

    Console.WriteLine(JsonSerializer.Serialize(
        new
        {
            host = PortalFixtureHostOptions.IdentityMarker,
            revision = options.Revision,
            runTag = options.RunTag,
            instantUtc = instantUtc,
            mode = options.Mode.ToString().ToLowerInvariant(),
            address = options.Mode == PortalFixtureHostMode.Start ? options.Address : null,
            target = targetReport,
            dependencyProof
        },
        JsonDefaults.Web));

    if (!ok)
    {
        Console.Error.WriteLine(
            "portal fixture preflight refused: the local target or the DI isolation proof failed; nothing was written and no listener was started.");
        return 1;
    }

    if (options.Mode == PortalFixtureHostMode.Start)
    {
        // 3. Provision the bounded, fixture-owned records through the real
        //    domain flows (with the scripted doubles in place), then serve.
        provisionReport = await PortalFixtureProvisioner.ProvisionAsync(
            services,
            new PortalFixtureProvisionOptions(
                RunTag: options.RunTag,
                Groups: options.ProvisionGroups,
                InstantUtc: instantUtc,
                SyntheticPassword: secrets.FixturePassword,
                WorkRoot: workRoot),
            CancellationToken.None);

        Console.WriteLine(JsonSerializer.Serialize(
            new { host = PortalFixtureHostOptions.IdentityMarker, provision = provisionReport },
            JsonDefaults.Web));

        if (!provisionReport.Passed && !options.AllowPartialProvision)
        {
            Console.Error.WriteLine(
                "portal fixture provisioning refused: at least one requested group could not be provisioned; no listener was started. Pass --allow-partial to serve the groups that succeeded.");
            return 1;
        }
    }
}

if (options.Mode == PortalFixtureHostMode.Preflight)
{
    return 0;
}

app.UseAhlenaApi();

// Read-only identity probe used by the wrapper to distinguish THIS host from
// any other listener on the same loopback address. No secret is ever returned.
app.MapGet("/__portal-fixture/identity", () => Results.Json(new
{
    marker = PortalFixtureHostOptions.IdentityMarker,
    revision = options.Revision,
    runTag = options.RunTag,
    provisionPassed = provisionReport?.Passed,
    manifestPath = provisionReport?.ManifestPath
}));

await app.RunAsync();
return 0;

static Dictionary<string, string?> BuildLocalConfiguration(PortalFixtureSecrets secrets, string workRoot) =>
    new()
    {
        // Every module falls back to IdentityDatabase; the host sets all nine
        // to the same approved local connection string.
        ["ConnectionStrings:IdentityDatabase"] = secrets.ConnectionString,
        ["ConnectionStrings:CmsDatabase"] = secrets.ConnectionString,
        ["ConnectionStrings:CaregiversDatabase"] = secrets.ConnectionString,
        ["ConnectionStrings:FamiliesDatabase"] = secrets.ConnectionString,
        ["ConnectionStrings:NotificationsDatabase"] = secrets.ConnectionString,
        ["ConnectionStrings:CareHomesDatabase"] = secrets.ConnectionString,
        ["ConnectionStrings:FinanceDatabase"] = secrets.ConnectionString,
        ["ConnectionStrings:CommunityDatabase"] = secrets.ConnectionString,
        ["ConnectionStrings:ChatDatabase"] = secrets.ConnectionString,

        ["Identity:Jwt:Issuer"] = "Ahlena.PortalFixtureHost",
        ["Identity:Jwt:Audience"] = "Ahlena.PortalFixtureHost.Local",
        ["Identity:Jwt:SigningKey"] = secrets.JwtSigningKey,

        // No production seeder, no startup migrations, no Finance migrations.
        ["App:TestUserSeed:Enabled"] = "false",
        ["Database:ApplyMigrationsOnStartup"] = "false",
        ["FinanceMigrations:ApplyOnStartup"] = "false",

        ["Storage:Local:RootPath"] = Path.Combine(workRoot, "files"),
        ["DataProtection:KeyRingPath"] = string.Empty,

        ["ElderlyProfile:DefaultTimeZoneId"] = "Africa/Cairo",

        ["Logging:LogLevel:Default"] = "Warning",
        ["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning",
        ["Logging:LogLevel:Microsoft.AspNetCore.HttpsPolicy"] = "Error"
    };

static void RemoveAhlenaHostedServices(IServiceCollection services)
{
    // Remove exactly the application hosted services (namespace Ahlena.*) and
    // keep the framework ones that start the server and validate options.
    List<ServiceDescriptor> applicationHostedServices = services
        .Where(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType?.Namespace?.StartsWith("Ahlena.", StringComparison.Ordinal) == true)
        .ToList();

    foreach (ServiceDescriptor descriptor in applicationHostedServices)
    {
        services.Remove(descriptor);
    }
}

static IReadOnlyList<PortalFixtureModuleContext> BuildModuleContexts(IServiceProvider services) =>
[
    new("Identity", services.GetRequiredService<IdentityDbContext>()),
    new("Cms", services.GetRequiredService<CmsDbContext>()),
    new("Caregivers", services.GetRequiredService<CaregiversDbContext>()),
    new("Families", services.GetRequiredService<FamiliesDbContext>()),
    new("Notifications", services.GetRequiredService<NotificationsDbContext>()),
    new("CareHomes", services.GetRequiredService<CareHomesDbContext>()),
    new("Finance", services.GetRequiredService<FinanceDbContext>()),
    new("Community", services.GetRequiredService<CommunityDbContext>()),
    new("Chat", services.GetRequiredService<ChatDbContext>())
];

static async Task<int> RunReuseProbeAsync(PortalFixtureHostOptions options)
{
    if (string.IsNullOrWhiteSpace(options.Address))
    {
        Console.Error.WriteLine("reuse mode requires --address.");
        return 2;
    }

    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using HttpResponseMessage response = await client.GetAsync(
            $"{options.Address.TrimEnd('/')}/__portal-fixture/identity");

        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;

        string? marker = root.TryGetProperty("marker", out JsonElement markerElement)
            ? markerElement.GetString()
            : null;
        string? revision = root.TryGetProperty("revision", out JsonElement revisionElement)
            ? revisionElement.GetString()
            : null;

        bool isThisHost = string.Equals(marker, PortalFixtureHostOptions.IdentityMarker, StringComparison.Ordinal);
        bool revisionMatches = string.IsNullOrWhiteSpace(options.Revision)
            || string.Equals(revision, options.Revision, StringComparison.Ordinal);

        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                mode = "reuse",
                status = (int)response.StatusCode,
                isPortalFixtureHost = isThisHost,
                revisionMatches,
                passed = isThisHost && revisionMatches
            },
            JsonDefaults.Web));

        return isThisHost && revisionMatches ? 0 : 1;
    }
    catch (Exception exception)
    {
        Console.WriteLine(JsonSerializer.Serialize(
            new { mode = "reuse", passed = false, reason = PortalFixtureHostOptions.SafeMessage(exception) },
            JsonDefaults.Web));
        return 1;
    }
}

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
}

/// <summary>
/// The private runtime credentials read from the credential file that the
/// wrapper creates under the approved OpenCode temp root with a current-user
/// ACL. The values are never printed, never logged and never committed.
/// </summary>
internal sealed record PortalFixtureSecrets(
    string ConnectionString,
    string JwtSigningKey,
    string FixturePassword)
{
    public static PortalFixtureSecrets Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("--credential-file is required.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The credential file does not exist.", path);
        }

        string json = File.ReadAllText(path);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        string connectionString = Require(root, "connectionString");
        string jwtSigningKey = Require(root, "jwtSigningKey");
        string fixturePassword = Require(root, "fixturePassword");

        if (jwtSigningKey.Length < 32)
        {
            throw new InvalidOperationException("jwtSigningKey must be at least 32 characters.");
        }

        if (fixturePassword.Length < 12)
        {
            throw new InvalidOperationException("fixturePassword must be at least 12 characters.");
        }

        if (!PortalFixtureTargetGuard.IsApprovedLocalTarget(connectionString))
        {
            throw new InvalidOperationException(
                $"the connection string must target localhost:{PortalFixtureTargetGuard.ApprovedPort}/{PortalFixtureTargetGuard.ApprovedDatabase}.");
        }

        return new PortalFixtureSecrets(connectionString, jwtSigningKey, fixturePassword);
    }

    private static string Require(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement element)
            || element.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new InvalidOperationException($"the credential file requires a non-empty '{propertyName}'.");
        }

        return element.GetString()!;
    }
}

/// <summary>Parsed command-line surface of the fixture host.</summary>
internal sealed class PortalFixtureHostOptions
{
    public const string IdentityMarker = "sanad-portal-fixture-host";

    public const string Usage =
        "Ahlena.PortalFixtureHost --mode plan|preflight|start|reuse|selfcheck [options]\n" +
        "  --credential-file <path>  private JSON credential file (preflight/start)\n" +
        "  --address <url>           loopback http URL, e.g. http://127.0.0.1:5299\n" +
        "  --work-root <path>        private artifact root (defaults under the OpenCode temp root)\n" +
        "  --run-tag <tag>           ownership tag stamped on every fixture narrative value\n" +
        "  --revision <sha>          pinned repository revision reported by the identity probe\n" +
        "  --instant <utc>           explicit UTC instant the test-host clock returns (ISO-8601)\n" +
        "  --provision-groups <list> comma-separated W5 groups to provision (default: all)\n" +
        "  --allow-partial           serve even when some requested group is not provisioned\n" +
        "  --help";

    public PortalFixtureHostMode Mode { get; private init; } = PortalFixtureHostMode.Plan;

    public string? CredentialFile { get; private init; }

    public string Address { get; private init; } = "http://127.0.0.1:5299";

    public string? WorkRoot { get; private init; }

    public string RunTag { get; private init; } = "w5p";

    public string? Revision { get; private init; }

    public DateTime? InstantUtc { get; private init; }

    public IReadOnlyList<string> ProvisionGroups { get; private init; } = [];

    public bool AllowPartialProvision { get; private init; }

    public bool ShowHelp { get; private init; }

    public static PortalFixtureHostOptions Parse(string[] args)
    {
        PortalFixtureHostMode mode = PortalFixtureHostMode.Plan;
        string? credentialFile = null;
        string address = "http://127.0.0.1:5299";
        string? workRoot = null;
        string runTag = "w5p";
        string? revision = null;
        DateTime? instant = null;
        List<string> groups = [];
        bool allowPartial = false;
        bool help = false;

        for (int index = 0; index < args.Length; index++)
        {
            string current = args[index];
            string? Value()
            {
                if (index + 1 < args.Length)
                {
                    index++;
                    return args[index];
                }

                throw new InvalidOperationException($"'{current}' requires a value.");
            }

            switch (current)
            {
                case "--mode":
                    mode = Value()?.ToLowerInvariant() switch
                    {
                        "plan" => PortalFixtureHostMode.Plan,
                        "preflight" => PortalFixtureHostMode.Preflight,
                        "start" => PortalFixtureHostMode.Start,
                        "reuse" => PortalFixtureHostMode.Reuse,
                        "selfcheck" => PortalFixtureHostMode.SelfCheck,
                        var other => throw new InvalidOperationException($"unknown mode '{other}'.")
                    };
                    break;
                case "--credential-file":
                    credentialFile = Value();
                    break;
                case "--address":
                    address = Value() ?? address;
                    break;
                case "--work-root":
                    workRoot = Value();
                    break;
                case "--run-tag":
                    runTag = Value() ?? runTag;
                    break;
                case "--revision":
                    revision = Value();
                    break;
                case "--instant":
                    string rawInstant = Value() ?? string.Empty;
                    if (!DateTime.TryParse(
                            rawInstant,
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                            out DateTime parsedInstant))
                    {
                        throw new InvalidOperationException("--instant must be an ISO-8601 UTC instant.");
                    }

                    instant = DateTime.SpecifyKind(parsedInstant, DateTimeKind.Utc);
                    break;
                case "--provision-groups":
                    groups = (Value() ?? string.Empty)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
                    break;
                case "--allow-partial":
                    allowPartial = true;
                    break;
                case "--help":
                case "-h":
                    help = true;
                    break;
                default:
                    throw new InvalidOperationException($"unknown argument '{current}'.");
            }
        }

        if (mode == PortalFixtureHostMode.Start && !PortalFixtureTargetGuard.IsLoopbackAddress(address))
        {
            throw new InvalidOperationException("--address must be a loopback http URL (localhost / 127.0.0.1 / ::1).");
        }

        return new PortalFixtureHostOptions
        {
            Mode = mode,
            CredentialFile = credentialFile,
            Address = address,
            WorkRoot = workRoot,
            RunTag = runTag,
            Revision = revision,
            InstantUtc = instant,
            ProvisionGroups = groups,
            AllowPartialProvision = allowPartial,
            ShowHelp = help
        };
    }

    /// <summary>Reduces any exception to a credential-free single line.</summary>
    public static string SafeMessage(Exception exception)
    {
        string message = (exception.Message ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return message.Length > 200 ? message[..200] + "..." : message;
    }
}

internal enum PortalFixtureHostMode
{
    Plan,
    Preflight,
    Start,
    Reuse,
    SelfCheck
}
