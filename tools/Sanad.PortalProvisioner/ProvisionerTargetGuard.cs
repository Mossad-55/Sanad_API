using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.Chat.Infrastructure.Persistence;
using Sanad.Modules.Cms.Infrastructure.Persistence;
using Sanad.Modules.Community.Infrastructure.Persistence;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Finance.Infrastructure;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Notifications.Infrastructure.Persistence;

namespace Sanad.PortalProvisioner;

/// <summary>
/// The explicitly passed, fail-closed target expectations. Every field is
/// mandatory; there is no default anywhere. <see cref="TargetKind"/> accepts
/// exactly one value: <c>rehearsal</c>. The revision string is a correlation
/// pin: the tool cannot read the deployed binary remotely, so it is recorded
/// in the manifest and must equal the operator's SSH-verified service revision.
/// <para>
/// <see cref="TunnelDeclared"/> is the operator's explicit declaration that the
/// connection is an SSH port-forward to the rehearsal cluster (the direct VPS
/// PostgreSQL port is not reachable from the workstation). Under that
/// declaration a reached loopback address is accepted (the tunnel terminates
/// on the server's loopback) and BOTH the reached address/port and the
/// operator-declared host are recorded; without it, strict address matching
/// stays exactly as before.
/// </para>
/// <para>The rehearsal cluster identity itself is pinned by its PostgreSQL
/// <c>system_identifier</c> (from <c>pg_control_system()</c>): a non-secret
/// fingerprint the operator supplies through the PRIVATE credentials file
/// (<c>expectedSystemIdentifier=…</c>) — never the command line, never printed,
/// never written into any plan, manifest or report. This is the loopback
/// protection: a loopback target (tunneled or not) whose system identifier is
/// unreadable or mismatched is refused, which separates the rehearsal cluster
/// from the locally protected <c>SanadDb</c>/<c>SanadIntegrationDb</c> even
/// when names collide.</para>
/// </summary>
public sealed record PortalProvisionExpectations(
    string Host,
    int Port,
    string Database,
    string Revision,
    string TargetKind,
    bool TunnelDeclared,
    IReadOnlyDictionary<string, string> MigrationHeads,
    bool AllowLocalSanadDb = false,
    string RunTag = "")
{
    /// <summary>The original S3a rehearsal target kind — the default, unchanged path.</summary>
    public const string RehearsalTargetKind = "rehearsal";

    /// <summary>
    /// The disposable nonce dry-run target kind (D1 only): loopback, and only a
    /// database whose name matches <see cref="DemoNonceDatabasePattern"/>.
    /// </summary>
    public const string DemoNonceTargetKind = "demo-nonce";

    /// <summary>
    /// The single recorded override for the LOCAL <c>SanadDb</c> (D2): accepted
    /// only with <c>--allow-local-sanad-db</c>, run tag <c>demo1</c>, a
    /// cluster-identity check and a printed plan warning. Never applies to the
    /// report-fixture groups.
    /// </summary>
    public const string LocalDemoTargetKind = "local-demo";

    /// <summary>The one run tag the local override is ever accepted for.</summary>
    public const string LocalDemoRunTag = "demo1";

    /// <summary>A generated D1 nonce database name: <c>SanadDemoNonce_&lt;digits&gt;</c>.</summary>
    public const string DemoNonceDatabasePattern = @"^SanadDemoNonce_[0-9]{6,20}$";

    /// <summary>Kept for the rehearsal path's existing message and tests.</summary>
    public const string OnlyAllowedTargetKind = RehearsalTargetKind;

    /// <summary>The locally protected databases that must never be a tool target.</summary>
    public static readonly IReadOnlyList<string> ProtectedLocalDatabases = ["SanadDb", "SanadIntegrationDb"];

    /// <summary>
    /// Every target kind the guard can accept. <c>production</c> is deliberately
    /// absent from this list and refused by name in <c>Parse</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> AllowedTargetKinds =
        [RehearsalTargetKind, DemoNonceTargetKind, LocalDemoTargetKind];

    public bool IsDemoTarget => TargetKind is DemoNonceTargetKind or LocalDemoTargetKind;
}

/// <summary>One module database context handed to the read-only verifier.</summary>
public sealed record PortalProvisionModuleContext(string Module, DbContext Context);

/// <summary>One module's read-only verification outcome. Credential-free.</summary>
public sealed record PortalProvisionModuleVerification(
    string Module,
    bool Passed,
    string? AppliedHead,
    string? ExpectedHead,
    int PendingMigrations,
    string? SafeError);

/// <summary>The combined read-only identity probe result.</summary>
public sealed record PortalProvisionVerificationReport(
    bool Passed,
    string? ServerAddress,
    int? ServerPort,
    string? DatabaseName,
    string? ServerVersion,
    IReadOnlyList<PortalProvisionModuleVerification> Modules,
    IReadOnlyList<string> Refusals);

/// <summary>
/// The fail-closed target-identity guard. Parse validates the operator's
/// expectations (refusing Production and the locally protected databases
/// outright); VerifyAsync runs READ-ONLY probes — the server address/port and
/// database actually reached, and each module's applied migration head versus
/// the expected head — and refuses on any mismatch. Nothing here writes.
/// <para>
/// The identity check is driven by the explicitly passed expectations only.
/// The rehearsal database and the local proof database share the NAME
/// <c>SanadDb</c>, so the database name is never sufficient: host, port,
/// per-module heads and the operator-declared revision correlation must all
/// match, and any mismatch aborts with zero writes.
/// </para>
/// </summary>
public static class ProvisionerTargetGuard
{
    public static PortalProvisionExpectations Parse(
        string? host,
        string? port,
        string? database,
        string? revision,
        string? targetKind,
        bool tunnelDeclared,
        IEnumerable<string>? migrationHeadPairs,
        out IReadOnlyList<string> refusals,
        bool allowLocalSanadDb = false,
        string? runTag = null)
    {
        var problems = new List<string>();

        string target = (targetKind ?? string.Empty).Trim();
        bool targetKindAccepted = string.Equals(
            target, PortalProvisionExpectations.RehearsalTargetKind, StringComparison.Ordinal)
            || string.Equals(target, PortalProvisionExpectations.DemoNonceTargetKind, StringComparison.Ordinal)
            || string.Equals(target, PortalProvisionExpectations.LocalDemoTargetKind, StringComparison.Ordinal);

        if (!targetKindAccepted)
        {
            problems.Add(
                $"--target-kind must be one of {string.Join(", ", PortalProvisionExpectations.AllowedTargetKinds.Select(
                    kind => $"'{kind}'"))} (got "
                    + (target.Length == 0 ? "<missing>" : $"'{target}'") + "); Production is refused outright.");
        }

        bool demoNonce = string.Equals(
            target, PortalProvisionExpectations.DemoNonceTargetKind, StringComparison.Ordinal);
        bool localDemo = string.Equals(
            target, PortalProvisionExpectations.LocalDemoTargetKind, StringComparison.Ordinal);

        if (allowLocalSanadDb && !localDemo)
        {
            problems.Add(
                "--allow-local-sanad-db is the single recorded local override and is accepted only with "
                    + $"--target-kind {PortalProvisionExpectations.LocalDemoTargetKind} (got "
                    + $"'{(target.Length == 0 ? "<missing>" : target)}').");
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            problems.Add("--expect-host is required (fail-closed: no default target).");
        }

        if (!int.TryParse(port, NumberStyles.Integer, CultureInfo.InvariantCulture, out int portValue)
            || portValue is < 1 or > 65535)
        {
            problems.Add("--expect-port is required and must be a valid TCP port (fail-closed).");
        }

        if (string.IsNullOrWhiteSpace(database))
        {
            problems.Add("--expect-database is required (fail-closed: no default target).");
        }

        if (string.IsNullOrWhiteSpace(revision))
        {
            problems.Add(
                "--expect-revision is required: the deployed service revision the operator verified over SSH; "
                    + "the manifest records it as the write-authorization correlation.");
        }

        string hostValue = (host ?? string.Empty).Trim();
        string databaseValue = (database ?? string.Empty).Trim();
        bool loopback = hostValue is "localhost" or "127.0.0.1" or "::1";

        // The local proof boundary: never touch the protected local databases
        // on a direct loopback connection. A rehearsal reached over an SSH
        // tunnel may legitimately share BOTH the loopback path and the name
        // SanadDb, so with --tunnel-declared the parse allows the shape and
        // the RUNTIME guard refuses instead: the reached loopback cluster must
        // present the expected PostgreSQL system identifier, which the local
        // protected databases cannot have unless explicitly pinned.
        // The local-demo kind is deliberately NOT handled in this branch (a
        // protected-name condition can never constrain the boundary of a
        // target declared under a different name); its full boundary is
        // enforced unconditionally further below. Protected names never match
        // the generated nonce pattern, so a plain protected-name refusal for
        // every non-tunneled direct shape (demo kinds included — the nonce
        // rule below refuses those by its own generation requirement) is
        // exactly the previous behavior.
        if (loopback
            && PortalProvisionExpectations.ProtectedLocalDatabases.Contains(
                databaseValue, StringComparer.OrdinalIgnoreCase)
            && !localDemo
            && !tunnelDeclared)
        {
            problems.Add(
                $"refusing target '{databaseValue}' on a loopback host: that name is the protected local "
                    + "database. A rehearsal cluster genuinely reached over an SSH port-forward must be "
                    + "declared with --tunnel-declared and will then be proven by the cluster system "
                    + "identifier (expectedSystemIdentifier in the private credentials file) at verify time.");
        }

        if (demoNonce)
        {
            if (!loopback)
            {
                problems.Add(
                    $"--target-kind {PortalProvisionExpectations.DemoNonceTargetKind} is a disposable LOCAL dry-run target: "
                        + $"--expect-host must be loopback (got '{hostValue}').");
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(
                    databaseValue, PortalProvisionExpectations.DemoNonceDatabasePattern))
            {
                problems.Add(
                    $"--target-kind {PortalProvisionExpectations.DemoNonceTargetKind} requires a generated nonce database name "
                        + $"'SanadDemoNonce_<digits>' (got '{databaseValue}').");
            }

            if (tunnelDeclared)
            {
                problems.Add(
                    $"--target-kind {PortalProvisionExpectations.DemoNonceTargetKind} is a DIRECT local dry run: "
                        + "--tunnel-declared is refused so a nonce target can never be pointed at the rehearsal cluster.");
            }
        }

        // The exact local-demo boundary, UNCONDITIONAL (takeover finding #5):
        // every constraint applies to EVERY declared database name — it must
        // not depend on the name already being in the protected list, or any
        // other name would bypass the override, the run tag and the tunnel
        // refusal. The override accepts exactly one shape: --allow-local-
        // sanad-db, the exact database 'SanadDb' (Ordinal), --run-tag 'demo1',
        // a loopback host, and no tunnel declaration.
        if (localDemo)
        {
            if (!loopback)
            {
                problems.Add(
                    $"--target-kind {PortalProvisionExpectations.LocalDemoTargetKind} is a DIRECT local connection: "
                        + $"--expect-host must be loopback (got '{hostValue}').");
            }

            if (!allowLocalSanadDb)
            {
                problems.Add(
                    $"refusing target '{databaseValue}' for --target-kind {PortalProvisionExpectations.LocalDemoTargetKind} without "
                        + "--allow-local-sanad-db: the local override is never implied and must be declared explicitly.");
            }

            if (!string.Equals(databaseValue, "SanadDb", StringComparison.Ordinal))
            {
                problems.Add(
                    $"--target-kind {PortalProvisionExpectations.LocalDemoTargetKind} applies only to the local database 'SanadDb' "
                        + $"(got '{databaseValue}'); SanadIntegrationDb and every other name stay refused.");
            }

            string runTagValue = (runTag ?? string.Empty).Trim();
            if (!string.Equals(runTagValue, PortalProvisionExpectations.LocalDemoRunTag, StringComparison.Ordinal))
            {
                problems.Add(
                    string.IsNullOrWhiteSpace(runTagValue)
                        ? $"--target-kind {PortalProvisionExpectations.LocalDemoTargetKind} requires --run-tag "
                            + $"'{PortalProvisionExpectations.LocalDemoRunTag}'."
                        : $"--target-kind {PortalProvisionExpectations.LocalDemoTargetKind} is accepted only with --run-tag "
                            + $"'{PortalProvisionExpectations.LocalDemoRunTag}' (got '{runTagValue}').");
            }

            if (tunnelDeclared)
            {
                problems.Add(
                    $"--target-kind {PortalProvisionExpectations.LocalDemoTargetKind} is a DIRECT local connection; "
                        + "--tunnel-declared is refused so the override can never be used against the rehearsal cluster.");
            }
        }

        var heads = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in migrationHeadPairs ?? [])
        {
            string trimmed = pair.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            int separator = trimmed.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0 || separator == trimmed.Length - 1)
            {
                problems.Add($"--expect-migration-heads entry '{trimmed}' must be module=head.");
                continue;
            }

            string module = trimmed[..separator].Trim();
            string head = trimmed[(separator + 1)..].Trim();
            if (heads.ContainsKey(module))
            {
                problems.Add($"--expect-migration-heads module '{module}' passed twice.");
                continue;
            }

            heads[module] = head;
        }

        foreach (string required in RequiredModules)
        {
            if (!heads.ContainsKey(required))
            {
                problems.Add(
                    $"--expect-migration-heads is missing the required module '{required}' (all nine module heads are mandatory).");
            }
        }

        refusals = problems;
        return new PortalProvisionExpectations(
            hostValue,
            portValue,
            databaseValue,
            (revision ?? string.Empty).Trim(),
            target,
            tunnelDeclared,
            heads,
            allowLocalSanadDb,
            (runTag ?? string.Empty).Trim());
    }

    /// <summary>The modules whose migration heads are asserted (all nine).</summary>
    public static readonly IReadOnlyList<string> RequiredModules =
    [
        "care_homes", "caregivers", "chat", "cms", "community",
        "families", "finance", "identity", "notifications"
    ];

    public static IReadOnlyList<PortalProvisionModuleContext> BuildModuleContexts(
        string connectionString)
    {
        string shared = connectionString;
        return
        [
            new("care_homes", BuildContext<CareHomesDbContext>(shared)),
            new("caregivers", BuildContext<CaregiversDbContext>(shared)),
            new("chat", BuildContext<ChatDbContext>(shared)),
            new("cms", BuildContext<CmsDbContext>(shared)),
            new("community", BuildContext<CommunityDbContext>(shared)),
            new("families", BuildContext<FamiliesDbContext>(shared)),
            new("finance", BuildContext<FinanceDbContext>(shared)),
            new("identity", BuildContext<IdentityDbContext>(shared)),
            new("notifications", BuildContext<NotificationsDbContext>(shared))
        ];
    }

    private static T BuildContext<T>(string connectionString)
        where T : DbContext
    {
        // Every module scopes its migrations history to its own schema —
        // replicating the application's registrations verbatim is what makes
        // the head assertion read the module's OWN history, not another's.
        string schema = (string)(typeof(T).GetField("Schema")
            ?? throw new InvalidOperationException($"context {typeof(T).Name} carries no Schema constant."))
            .GetValue(null)!;

        var builder = new DbContextOptionsBuilder<T>();
        builder.UseNpgsql(
            connectionString,
            npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(T).Assembly.GetName().Name);
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema);
            });
        return (T)Activator.CreateInstance(typeof(T), builder.Options)!;
    }

    /// <summary>
    /// The reached target identity, materialized for the pure evaluator.
    /// <see cref="SystemIdentifier"/> is the PostgreSQL cluster fingerprint;
    /// it is compared in-memory only and NEVER serialized anywhere.
    /// </summary>
    public sealed record PortalReachedIdentity(
        string? Database,
        string? ServerAddress,
        int? ServerPort,
        string? SystemIdentifier,
        bool SystemIdentifierReadable,
        string? SystemIdentifierError);

    /// <summary>
    /// Strictly read-only verification of the reached server against the
    /// passed expectations. Any refusal means "do not write"; the caller
    /// aborts. No schema or data is touched.
    /// <para>
    /// <paramref name="expectedSystemIdentifier"/> is the rehearsal cluster's
    /// PostgreSQL <c>system_identifier</c> read from the PRIVATE credentials
    /// file (mandatory). It is compared in memory against
    /// <c>pg_control_system()</c> and is never printed, logged, serialized or
    /// written into any plan, manifest or report.
    /// </para>
    /// </summary>
    public static async Task<PortalProvisionVerificationReport> VerifyAsync(
        PortalProvisionExpectations expectations,
        IReadOnlyList<PortalProvisionModuleContext> contexts,
        string expectedSystemIdentifier,
        CancellationToken cancellationToken)
    {
        var refusals = new List<string>();
        var moduleResults = new List<PortalProvisionModuleVerification>();

        string? serverAddress = null;
        int? serverPort = null;
        string? databaseName = null;
        string? serverVersion = null;
        string? reachedSystemIdentifier = null;
        bool systemIdentifierReadable = false;
        string? systemIdentifierError = null;

        DbContext probeContext = contexts[0].Context;

        Npgsql.NpgsqlConnection? probeConnection = null;
        try
        {
            probeConnection = (Npgsql.NpgsqlConnection)probeContext.Database.GetDbConnection();
            await probeConnection.OpenAsync(cancellationToken);
            await using (var command = probeConnection.CreateCommand())
            {
                command.CommandText = "select current_database(), inet_server_addr()::text, inet_server_port(), version()";
                await using System.Data.Common.DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    databaseName = reader.IsDBNull(0) ? null : reader.GetString(0);
                    serverAddress = reader.IsDBNull(1) ? null : reader.GetString(1);
                    serverPort = reader.IsDBNull(2) ? null : reader.GetInt32(2);
                    serverVersion = reader.IsDBNull(3) ? null : reader.GetString(3)[..Math.Min(60, reader.GetString(3).Length)];
                }
            }

            // Cluster identity probe (read-only, separate so a privilege
            // failure does not mask the database/address results): the value
            // stays in memory; only its reachability and a value-free error
            // summary ever leave this scope.
            try
            {
                await using var identifierCommand = probeConnection.CreateCommand();
                identifierCommand.CommandText = "select system_identifier::text from pg_control_system()";
                object? scalar = await identifierCommand.ExecuteScalarAsync(cancellationToken);
                if (scalar is string identifierText && identifierText.Trim().Length > 0)
                {
                    reachedSystemIdentifier = identifierText.Trim();
                    systemIdentifierReadable = true;
                }
                else
                {
                    systemIdentifierError = "pg_control_system() returned no system identifier.";
                }
            }
            catch (Exception exception)
            {
                systemIdentifierError = Safe(exception);
            }
        }
        catch (Exception exception)
        {
            refusals.Add($"target probe failed: {Safe(exception)}");
        }
        finally
        {
            if (probeConnection is not null)
            {
                await probeConnection.CloseAsync();
            }
        }

        refusals.AddRange(EvaluateReachedIdentity(
            expectations,
            expectedSystemIdentifier,
            new PortalReachedIdentity(
                databaseName,
                serverAddress,
                serverPort,
                reachedSystemIdentifier,
                systemIdentifierReadable,
                systemIdentifierError)));

        foreach (PortalProvisionModuleContext module in contexts)
        {
            try
            {
                List<string> applied = [.. await module.Context.Database.GetAppliedMigrationsAsync(cancellationToken)];
                List<string> pending = [.. await module.Context.Database.GetPendingMigrationsAsync(cancellationToken)];
                string appliedHead = applied.Count > 0 ? applied[^1] : "<none>";
                bool headOk = expectations.MigrationHeads.TryGetValue(module.Module, out string? expectedHead)
                    && string.Equals(appliedHead, expectedHead, StringComparison.Ordinal);
                bool pendingOk = pending.Count == 0;

                if (!headOk)
                {
                    refusals.Add(
                        $"migration head mismatch for {module.Module}: reached '{appliedHead}', expected "
                            + $"'{expectedHead ?? "<not asserted>"}'.");
                }

                if (!pendingOk)
                {
                    refusals.Add(
                        $"module {module.Module} has {pending.Count} pending migration(s); a target must be fully migrated before provisioning.");
                }

                moduleResults.Add(new PortalProvisionModuleVerification(
                    module.Module, headOk && pendingOk, appliedHead, expectedHead, pending.Count, null));
            }
            catch (Exception exception)
            {
                refusals.Add($"module {module.Module} verification failed: {Safe(exception)}");
                moduleResults.Add(new PortalProvisionModuleVerification(
                    module.Module, false, null, expectations.MigrationHeads.GetValueOrDefault(module.Module), 0, Safe(exception)));
            }
            finally
            {
                await module.Context.Database.CloseConnectionAsync();
            }
        }

        return new PortalProvisionVerificationReport(
            refusals.Count == 0,
            serverAddress,
            serverPort,
            databaseName,
            serverVersion,
            moduleResults,
            refusals);
    }

    /// <summary>
    /// The pure identity decision shared by <see cref="VerifyAsync"/> and the
    /// focused test suite: database name, reached address/port (strict unless
    /// the operator declared an SSH tunnel AND the reached address is the
    /// tunnel's loopback terminus), and the MANDATORY PostgreSQL cluster
    /// system identifier. Refusal messages never contain the identifier value.
    /// </summary>
    public static IReadOnlyList<string> EvaluateReachedIdentity(
        PortalProvisionExpectations expectations,
        string? expectedSystemIdentifier,
        PortalReachedIdentity reached)
    {
        var refusals = new List<string>();

        if (reached.Database is not null
            && !string.Equals(reached.Database, expectations.Database, StringComparison.Ordinal))
        {
            refusals.Add(
                $"database mismatch: reached '{reached.Database}', expected '{expectations.Database}'.");
        }

        bool tunneledLoopback = expectations.TunnelDeclared
            && IsLoopbackAddress(reached.ServerAddress);
        if (reached.ServerAddress is not null
            && !tunneledLoopback
            && !MatchesHost(reached.ServerAddress, expectations.Host))
        {
            refusals.Add(
                $"server address mismatch: reached '{reached.ServerAddress}', expected '{expectations.Host}'. "
                    + "The identity check is driven by explicit expectations only; refuse by default."
                    + (expectations.TunnelDeclared
                        ? " Under --tunnel-declared only the tunnel's loopback terminus is accepted."
                        : string.Empty));
        }

        if (reached.ServerPort is not null && reached.ServerPort.Value != expectations.Port)
        {
            refusals.Add($"server port mismatch: reached {reached.ServerPort.Value}, expected {expectations.Port}.");
        }

        // Both demo target kinds are LOCAL dry-run targets: whatever the
        // declared host resolves to, the reached address must itself be
        // loopback. Without this a name that resolves off-box could carry the
        // demo path to a remote cluster.
        if (expectations.IsDemoTarget
            && reached.ServerAddress is not null
            && !IsLoopbackAddress(reached.ServerAddress))
        {
            refusals.Add(
                $"server address mismatch: --target-kind '{expectations.TargetKind}' is a local dry-run target and must be "
                    + $"reached on loopback (reached '{reached.ServerAddress}').");
        }

        // Cluster identity (mandatory for rehearsal, direct or tunneled):
        // this is what separates the rehearsal cluster from a LOCAL database
        // that happens to share its name, and it is the loopback protection.
        if (string.IsNullOrWhiteSpace(expectedSystemIdentifier))
        {
            refusals.Add(
                "the cluster system identifier is mandatory for every target kind: add an expectedSystemIdentifier= "
                    + "line to the private credentials file, captured READ-ONLY from the target cluster. The value "
                    + "is never passed on the command line and never printed.");
        }
        else if (!reached.SystemIdentifierReadable)
        {
            refusals.Add(
                "the connected role cannot read pg_control_system(), so the cluster identity cannot be proven: "
                    + "grant the connection role permission to read control data (for example membership in "
                    + "pg_read_control_data, or another role permitted to read pg_control_system) and retry. "
                    + "Refusing fail-closed; no value is printed."
                    + (reached.SystemIdentifierError is null
                        ? string.Empty
                        : $" (probe outcome: {reached.SystemIdentifierError})"));
        }
        else if (!string.Equals(reached.SystemIdentifier, expectedSystemIdentifier, StringComparison.Ordinal))
        {
            refusals.Add(
                "cluster system identifier mismatch: the reached cluster is NOT the rehearsal cluster pinned by "
                    + "the private credentials file. Refusing; the identifier value is never printed.");
        }

        return refusals;
    }

    /// <summary>Loopback test that tolerates a CIDR-suffixed inet value.</summary>
    private static bool IsLoopbackAddress(string? reachedAddress)
    {
        if (string.IsNullOrWhiteSpace(reachedAddress))
        {
            return false;
        }

        int slash = reachedAddress.IndexOf('/', StringComparison.Ordinal);
        string candidate = slash > 0 ? reachedAddress[..slash] : reachedAddress;
        return System.Net.IPAddress.TryParse(candidate, out System.Net.IPAddress? address)
            && System.Net.IPAddress.IsLoopback(address);
    }

    /// <summary>
    /// Host identity comparison: a literal-IP expectation must equal the
    /// reached address; a hostname expectation accepts any address the name
    /// resolves to (localhost legitimately reaches ::1 or 127.0.0.1).
    /// </summary>
    private static bool MatchesHost(string reachedAddress, string expectedHost)
    {
        if (string.Equals(reachedAddress, expectedHost, StringComparison.Ordinal))
        {
            return true;
        }

        // Strip a CIDR suffix if the server reports one.
        int slash = reachedAddress.IndexOf('/', StringComparison.Ordinal);
        if (slash > 0)
        {
            reachedAddress = reachedAddress[..slash];
        }

        if (System.Net.IPAddress.TryParse(expectedHost, out System.Net.IPAddress? expectedIp))
        {
            return System.Net.IPAddress.TryParse(reachedAddress, out System.Net.IPAddress? reachedIp)
                && expectedIp.Equals(reachedIp);
        }

        try
        {
            return System.Net.Dns.GetHostAddresses(expectedHost)
                .Any(address => address.Equals(System.Net.IPAddress.Parse(reachedAddress)));
        }
        catch (System.Net.Sockets.SocketException)
        {
            return false;
        }
    }

    private static string Safe(Exception exception)
    {
        string message = (exception.Message ?? string.Empty)
            .Replace('\r', ' ').Replace('\n', ' ').Trim();
        return message.Length > 200 ? message[..200] + "..." : message;
    }
}
