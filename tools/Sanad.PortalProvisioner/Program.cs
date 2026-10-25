using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Infrastructure.Security;
using Sanad.PortalProvisioner;

// ---------------------------------------------------------------------------
// Sanad.PortalProvisioner — S3a rehearsal fixture provisioning tool.
//
// Modes and their guarantees:
//   plan      pure derivation of the complete write manifest; ZERO database
//             connections, zero writes.
//   verify    read-only target identity assertion (server, port, database,
//             every module migration head) against explicit --expect-* flags;
//             zero writes.
//   provision verify FIRST, then the marker-keyed upsert of the approved
//             data-only groups. Any refusal aborts before the first write.
//
// Fail-closed by construction: Production target kinds are refused, loopback
// targets named SanadDb/SanadIntegrationDb are refused without an explicit
// --tunnel-declared operator declaration (and even then the cluster's
// PostgreSQL system_identifier must match the private expectedSystemIdentifier
// from the credentials file — the local-DB protection), missing expectations
// are refused, unknown or gated groups are refused, every handler the tool
// constructs routes potential refund movements through a REFUSING gateway
// stub, and a run-wide invariant re-proves after every group that no
// fixture-owned row is provider-call armed — the tool cannot reach a provider
// even by accident.
//
// Credentials are read ONLY from the private credentials file passed via
// --credentials-file (format: connectionString=..., fixturePassword=...,
// expectedSystemIdentifier=...); they are never printed, logged or written
// into any manifest.
// ---------------------------------------------------------------------------

if (args.Contains("--help") || args.Contains("-h") || args.Length == 0)
{
    PrintUsage();
    return 0;
}

string? GetOption(string name)
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

string[] GetValues(string name)
{
    List<string> values = [];
    for (int index = 0; index < args.Length; index++)
    {
        if (args[index] == name && index + 1 < args.Length)
        {
            values.Add(args[index + 1]);
        }
    }

    return [.. values];
}

string? mode = GetOption("--mode");
string? runTag = GetOption("--run-tag");
string? instantRaw = GetOption("--instant");
string? workRoot = GetOption("--work-root") ?? Directory.GetCurrentDirectory();
string[] groupArgs =
    [.. GetValues("--groups")
        .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))];

string? revision = Environment.GetEnvironmentVariable("SANAD_REVISION_CORRELATION")
    ?? GetOption("--expect-revision");

if (string.IsNullOrWhiteSpace(mode))
{
    Console.Error.WriteLine("provisioner refused: --mode is required (plan | verify | provision).");
    return 2;
}

if (string.IsNullOrWhiteSpace(runTag))
{
    Console.Error.WriteLine("provisioner refused: --run-tag is required (stable marker namespace per target).");
    return 2;
}

DateTime instant;
if (string.IsNullOrWhiteSpace(instantRaw))
{
    Console.Error.WriteLine(
        "provisioner refused: --instant is required (UTC fixture timestamp; no clock is ever changed).");
    return 2;
}
else if (!DateTime.TryParse(instantRaw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out instant)
    || instant.Kind != DateTimeKind.Utc)
{
    Console.Error.WriteLine("provisioner refused: --instant must parse as a UTC timestamp.");
    return 2;
}

Directory.CreateDirectory(workRoot);
string manifestPath = Path.Combine(workRoot, $"portal-provision-plan-{Clean(runTag)}.json");

try
{
    if (mode == "plan")
    {
        PortalProvisionPlanReport plan = PortalProvisionPlan.Build(
            runTag,
            instant,
            revision ?? "<not provided in plan mode>",
            GetOption("--target-kind") ?? "<plan mode: no target asserted>",
            groupArgs,
            seamDeployed: args.Contains("--seam-deployed"));

        string planJson = PortalProvisionPlan.Serialize(plan);
        File.WriteAllText(manifestPath, planJson);
        Console.WriteLine(planJson);
        Console.Error.WriteLine($"plan written (zero database access): {manifestPath}");
        return 0;
    }

    // verify | provision: the expectations are mandatory before ANY connection.
    // --tunnel-declared: the operator's explicit declaration that the connection
    // string is an SSH port-forward to the rehearsal cluster (its direct
    // PostgreSQL port is not reachable from the workstation). Without it the
    // loopback/protected-name refusal stays absolute.
    bool tunnelDeclared = args.Contains("--tunnel-declared");
    PortalProvisionExpectations expectations = ProvisionerTargetGuard.Parse(
        GetOption("--expect-host"),
        GetOption("--expect-port"),
        GetOption("--expect-database"),
        revision,
        GetOption("--target-kind"),
        tunnelDeclared,
        GetValues("--expect-migration-heads"),
        out IReadOnlyList<string> parseRefusals);

    if (parseRefusals.Count > 0)
    {
        Console.Error.WriteLine("provisioner refused (fail-closed expectations):");
        foreach (string refusal in parseRefusals)
        {
            Console.Error.WriteLine($"  - {refusal}");
        }

        return 3;
    }

    // Credentials: private file only, never echoed.
    string? credentialsFile = GetOption("--credentials-file");
    if (string.IsNullOrWhiteSpace(credentialsFile) || !File.Exists(credentialsFile))
    {
        Console.Error.WriteLine(
            "provisioner refused: --credentials-file must point at an existing private credentials file "
                + "(format: connectionString=<...> and fixturePassword=<...> lines).");
        return 3;
    }

    string? connectionString = null;
    string? fixturePassword = null;
    string? expectedSystemIdentifier = null;
    foreach (string line in File.ReadAllLines(credentialsFile))
    {
        int separator = line.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0)
        {
            continue;
        }

        string key = line[..separator].Trim();
        string value = line[(separator + 1)..].Trim();
        if (key == "connectionString")
        {
            connectionString = value;
        }
        else if (key == "fixturePassword")
        {
            fixturePassword = value;
        }
        else if (key == "expectedSystemIdentifier")
        {
            expectedSystemIdentifier = value;
        }
    }

    if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(fixturePassword))
    {
        Console.Error.WriteLine(
            "provisioner refused: the credentials file must carry non-empty connectionString= and fixturePassword= lines.");
        return 3;
    }

    if (string.IsNullOrWhiteSpace(expectedSystemIdentifier))
    {
        Console.Error.WriteLine(
            "provisioner refused: the credentials file must carry a non-empty expectedSystemIdentifier= line "
                + "(the rehearsal cluster's PostgreSQL system_identifier, captured READ-ONLY over SSH; the value "
                + "is never passed on the command line and never printed).");
        return 3;
    }

    IReadOnlyList<PortalProvisionModuleContext> contexts = ProvisionerTargetGuard.BuildModuleContexts(connectionString);
    try
    {
        PortalProvisionVerificationReport verification =
            await ProvisionerTargetGuard.VerifyAsync(
                expectations,
                contexts,
                expectedSystemIdentifier,
                CancellationToken.None);

        if (!verification.Passed)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new { mode, target = new { expectations.Host, expectations.Port, expectations.Database, expectations.Revision, expectations.TargetKind, tunnelDeclared = expectations.TunnelDeclared }, verificationPassed = false, refusals = verification.Refusals },
                PortalProvisionPlan.JsonOptions));
            Console.Error.WriteLine(
                "provisioner refused: the reached target does not match the explicitly expected identity; ZERO writes were made.");
            return 4;
        }

        if (mode == "verify")
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new { mode, verificationPassed = true, database = verification.DatabaseName, serverAddress = verification.ServerAddress, serverPort = verification.ServerPort, declaredHost = expectations.Host, tunnelDeclared = expectations.TunnelDeclared, clusterIdentityVerified = true, modules = verification.Modules },
                PortalProvisionPlan.JsonOptions));
            return 0;
        }

        // provision: identity verified, now the data-only upsert.
        IServiceCollection services = new ServiceCollection();
        foreach (PortalProvisionModuleContext module in contexts)
        {
            services.AddSingleton(module.Context.GetType(), module.Context);
        }

        services.AddSingleton<IPasswordHasher>(new AspNetPasswordHasher());

        await using (ServiceProvider provider = services.BuildServiceProvider())
        {
            PortalProvisionReport report = await PortalProvisioner.ProvisionAsync(
                provider,
                new PortalProvisionOptions(
                    RunTag: runTag,
                    Groups: groupArgs,
                    InstantUtc: instant,
                    SyntheticPassword: fixturePassword,
                    WorkRoot: workRoot,
                    SeamDeployed: args.Contains("--seam-deployed")),
                CancellationToken.None);

            Console.WriteLine(JsonSerializer.Serialize(
                new
                {
                    mode,
                    verificationPassed = true,
                    database = verification.DatabaseName,
                    serverAddress = verification.ServerAddress,
                    declaredHost = expectations.Host,
                    tunnelDeclared = expectations.TunnelDeclared,
                    revisionCorrelation = expectations.Revision,
                    provisionPassed = report.Passed,
                    runTag = report.RunTag,
                    familyLoginEmail = report.FamilyLoginEmail,
                    reportManifest = report.ManifestPath,
                    groups = report.Groups,
                    unresolved = report.UnresolvedGroups
                },
                PortalProvisionPlan.JsonOptions));

            return report.Passed ? 0 : 5;
        }
    }
    finally
    {
        foreach (PortalProvisionModuleContext module in contexts)
        {
            await module.Context.DisposeAsync();
        }
    }
}
catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
{
    Console.Error.WriteLine($"provisioner refused: {exception.Message.Replace('\r', ' ').Replace('\n', ' ')}");
    return 6;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"provisioner failed (no partial success reported): {exception.GetType().Name}: "
            + exception.Message.Replace('\r', ' ').Replace('\n', ' '));
    return 1;
}

static string Clean(string tag)
{
    var buffer = new System.Text.StringBuilder();
    foreach (char character in tag)
    {
        if (char.IsLetterOrDigit(character))
        {
            buffer.Append(char.ToLowerInvariant(character));
        }
        else
        {
            buffer.Append('-');
        }
    }

    return buffer.ToString();
}

static void PrintUsage()
{
    Console.WriteLine("""
        Sanad.PortalProvisioner — bounded rehearsal fixture provisioning (S3a).

        Modes:
          --mode plan        Print/write the complete planned write manifest. Zero DB access.
          --mode verify      Read-only target identity assertion. Zero writes.
          --mode provision   Verify first, then marker-keyed upsert of approved data-only groups.

        Common:
          --run-tag <tag>                 Stable marker namespace, e.g. r2.
          --instant <UTC timestamp>       Fixture timeline anchor (no clock is ever changed).
          --groups <id,id,...>            Subset of the approved groups (default: all data-only groups).
          --work-root <dir>               Manifest output directory.
          --seam-deployed                 Operator declaration that the deployed rehearsal revision runs the
                                          FixtureRefundSeam refund-outcome allowlist. REQUIRED to plan or provision
                                          the four seam groups (carehome-refund-failed, caregiver-refund-failed,
                                          duplicate-claim-approved-awaiting-execution, duplicate-claim-ambiguous);
                                          refused otherwise. Copy the allowlisted ids/outcomes from the plan into
                                          the deployed appsettings before provisioning those groups. This declaration
                                          must be backed by the deployed app's startup log line
                                          ("Fixture refund seam state: enabled=True ... allowlisted transaction ids=...")
                                          showing EXACTLY the ids this plan prints — if they do not match, do not provision.

        verify/provision only:
          --target-kind rehearsal         The ONLY accepted target kind ('production' is refused).
          --expect-host <host>            Required. Refuses on mismatch.
          --expect-port <port>            Required. Refuses on mismatch.
          --expect-database <name>        Required. Loopback SanadDb/SanadIntegrationDb refused without a tunnel declaration.
          --expect-revision <sha|latest>  Required correlation pin (SSH-verified service revision).
          --expect-migration-heads m=h…   Required, all nine modules: module=head (may repeat).
          --tunnel-declared               Operator declaration that the connection is an SSH port-forward to the
                                          rehearsal cluster. Under it a reached loopback address is accepted, and the
                                          reached address/port plus the declared host are recorded. Without it the
                                          strict address match stays as is.
          --credentials-file <path>       Private file with connectionString=, fixturePassword= and
                                          expectedSystemIdentifier= (the rehearsal cluster's PostgreSQL
                                          system_identifier, captured READ-ONLY over SSH). The identifier is
                                          compared in memory only; it is never printed and never serialized.

        The revision string is recorded in the manifest as the write-authorization
        correlation; the tool cannot read the deployed binary remotely. The cluster
        system identifier is the loopback protection: any target whose identifier is
        unreadable or mismatched is refused, including a local database sharing a
        rehearsal name.
        """);
}
