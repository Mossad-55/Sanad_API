using System.Text.Json;

namespace Sanad.UnitTests.PortalReportFixtures;

/// <summary>
/// Offline verification for tools/Invoke-PortalReportFixturePack.ps1. The tool
/// is dot-sourced with -LibraryOnly inside a PowerShell process and only the
/// pure helpers are exercised: no host process, no HTTP request, no database
/// connection, no provisioning and no credential use ever happens here.
/// <para>
/// The wrapper is a safe lifecycle driver only. These tests pin the two
/// properties that keep it safe: (1) every mode combination fails closed
/// without the explicit confirm switch it requires, and (2) there is no delete
/// surface at all — no cleanup-SQL function and no DELETE statement anywhere in
/// the tool text.
/// </para>
/// </summary>
public sealed class PortalReportFixturePackTests
{
    private static readonly Lazy<OfflineHarnessResult> Harness = new(RunOfflineHarness);

    private static readonly string[] ExpectedGroupIds =
    [
        "caregiver-refund-ambiguity",
        "caregiver-refund-failure",
        "caregiver-completed-booking",
        "carehome-paid-awaiting-decision",
        "carehome-refund-ambiguity",
        "carehome-refund-failure",
        "carehome-evidenced-external-refund",
        "carehome-booking-extension",
        "carehome-checkout-dispute",
        "subscription-and-duplicate-claim",
        "complaint-received",
        "complaint-investigating",
    ];

    [Fact]
    public void LoopbackHostAcceptsLoopbackAddressesOnly()
    {
        Assert.True(GetFlag("loopbackOk"));
        Assert.True(GetFlag("loopbackReject"));
    }

    [Fact]
    public void BaseUrlResolutionAcceptsLoopbackAndRejectsRemoteAndNonHttp()
    {
        Assert.True(GetFlag("baseUrlOk"));
        Assert.True(GetFlag("baseUrlRejectsRemote"));
        Assert.True(GetFlag("baseUrlRejectsNonHttp"));
    }

    [Fact]
    public void RunTagIsSanitizedAndFailsClosed()
    {
        Assert.True(GetFlag("runTagSanitizes"));
        Assert.True(GetFlag("runTagFailsClosed"));
    }

    [Fact]
    public void WorkRootAndCredentialPathsResolveUnderApprovedTempRoot()
    {
        Assert.True(GetFlag("workRootExplicit"));
        Assert.True(GetFlag("workRootDefault"));
        Assert.True(GetFlag("credentialPath"));
        Assert.True(GetFlag("manifestPath"));
    }

    [Fact]
    public void ConnectionStringApprovalTargetsOnlyLocalhostSanadDb()
    {
        Assert.True(GetFlag("connApproved"));
        Assert.True(GetFlag("connRejectRemote"));
        Assert.True(GetFlag("connRejectOtherDb"));
        Assert.True(GetFlag("connRejectOtherPort"));
    }

    [Fact]
    public void CredentialFileJsonValidatesTargetAndSecretLengths()
    {
        Assert.True(GetFlag("credentialJsonShape"));
        Assert.True(GetFlag("credentialJsonRejectsRemote"));
        Assert.True(GetFlag("credentialJsonRejectsShortKey"));
    }

    [Fact]
    public void RedactionRemovesTokensAndPasswords()
    {
        Assert.True(GetFlag("redactionWorks"));
    }

    [Fact]
    public void RedactionAlsoCoversStderrAndCredentialBearingSurfaces()
    {
        Assert.True(GetFlag("redactionExtended"));
        Assert.True(GetFlag("redactionStreamStderr"));
    }

    [Fact]
    public void PlatformDetectionAndPrivateArtifactBoundaryFailClosed()
    {
        Assert.True(GetFlag("platformDetected"));
        Assert.True(GetFlag("artifactOutsideRepo"));
        Assert.True(GetFlag("artifactInsideRepoRefused"));
    }

    [Fact]
    public void RevisionTokenIsOptionalAndBounded()
    {
        Assert.True(GetFlag("revisionAccepts"));
        Assert.True(GetFlag("revisionFailsClosed"));
    }

    [Fact]
    public void GroupInventoryPinsExactlyTwelveIdsInOrder()
    {
        Assert.True(GetFlag("groupCount12"));
        Assert.Equal(ExpectedGroupIds, Harness.Value.GetStringArray("groupIds"));
    }

    [Fact]
    public void PlanIsDeterministicAndCoversTwelveGroups()
    {
        Assert.True(GetFlag("planIdempotent"));
        Assert.True(GetFlag("planHasTwelveGroups"));
        Assert.True(GetFlag("planAddress"));
        Assert.True(GetFlag("planCleanupReportOnly"));
        Assert.True(GetFlag("planPaymentDoubles"));
        Assert.True(GetFlag("planInertDoubles"));
    }

    [Fact]
    public void ModeCombinationFailsClosedPerMode()
    {
        Assert.True(GetFlag("startRequiresConfirm"));
        Assert.True(GetFlag("startAcceptsConfirm"));
        Assert.True(GetFlag("stopRequiresConfirm"));
        Assert.True(GetFlag("stopAcceptsConfirm"));
        Assert.True(GetFlag("preflightRequiresCredential"));
        Assert.True(GetFlag("cleanupNoConfirm"));
        Assert.True(GetFlag("cleanupRejectsConfirm"));
        Assert.True(GetFlag("planRejectsStray"));
        Assert.True(GetFlag("unknownModeRejected"));
    }

    [Fact]
    public void HostArgumentsAreBuiltPerMode()
    {
        Assert.True(GetFlag("hostArgsStart"));
        Assert.True(GetFlag("hostArgsReuse"));
        Assert.True(GetFlag("hostArgsAllowPartial"));
    }

    [Fact]
    public void IdentityProbeAcceptsOnlyThisHostForThePinnedRevision()
    {
        Assert.True(GetFlag("probeMatches"));
        Assert.True(GetFlag("probeRefusesForeign"));
        Assert.True(GetFlag("probeRefusesMissing"));
        Assert.True(GetFlag("probeRefusesRevisionMismatch"));
        Assert.True(GetFlag("probeRefusesUnparseable"));
    }

    [Fact]
    public void CleanupIsReportOnlyAndHasNoDeleteSurface()
    {
        Assert.True(GetFlag("cleanupReportShape"));
        Assert.True(GetFlag("noCleanupSqlFunction"));
        Assert.True(GetFlag("noCleanupSqlGuardFunction"));
        Assert.True(GetFlag("noDeleteFromInTool"));
    }

    [Fact]
    public void HostSelfCheckExecutesTheSharedDomainDriversWithoutADatabase()
    {
        var invocation = ResolveSelfCheckInvocation();
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = invocation.FileName,
            Arguments = invocation.Arguments,
            WorkingDirectory = invocation.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("The bounded host self-check process could not be started.");
        var output = process.StandardOutput.ReadToEnd();
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0,
            $"The bounded host self-check exited with {process.ExitCode}. stdout: {output} stderr: {errors}");
        Assert.Contains("\"passed\":true", output, StringComparison.Ordinal);

        // The self-check runs the SAME domain sequences the provisioner uses, so
        // every claimed money state must be reached through real domain methods.
        foreach (var check in new[]
        {
            "families.booking.completed",
            "families.refund.failed",
            "families.refund.ambiguous",
            "carehome.reject.refundpending",
        })
        {
            Assert.Contains(check, output, StringComparison.Ordinal);
        }
    }

    private static (string FileName, string Arguments, string WorkingDirectory) ResolveSelfCheckInvocation()
    {
        var repoRoot = FindRepoRoot();
        const string instant = "2026-10-24T10:00:00Z";

        // Prefer the already-built host assembly (the release check builds it
        // clean first). The host is not directly referenceable from this test
        // project because its csproj is outside this task's editable manifest.
        foreach (var configuration in new[] { "Release", "Debug" })
        {
            var dll = Path.Combine(
                repoRoot, "tools", "Sanad.PortalFixtureHost", "bin", configuration, "net10.0",
                "Sanad.PortalFixtureHost.dll");
            if (File.Exists(dll))
            {
                return ("dotnet", $"\"{dll}\" --mode selfcheck --instant {instant}", repoRoot);
            }
        }

        var project = Path.Combine(repoRoot, "tools", "Sanad.PortalFixtureHost", "Sanad.PortalFixtureHost.csproj");
        if (File.Exists(project))
        {
            return ("dotnet",
                $"run --project \"{project}\" -c Release --no-launch-profile -- --mode selfcheck --instant {instant}",
                repoRoot);
        }

        throw new InvalidOperationException(
            "The bounded portal fixture host was not found under <repo>/tools; the self-check cannot run.");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "tools", "Invoke-PortalReportFixturePack.ps1")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root could not be located from the test output.");
    }

    private static bool GetFlag(string key) => Harness.Value.GetFlag(key);

    private static OfflineHarnessResult RunOfflineHarness()
    {
        var toolPath = FindToolPath();
        var harnessPath = Path.Combine(Path.GetTempPath(), $"sanad-portal-fixture-pack-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(harnessPath, BuildHarnessScript(toolPath));
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{harnessPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new InvalidOperationException("The offline PowerShell harness could not be started.");
            var output = process.StandardOutput.ReadToEnd();
            var errors = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"The offline harness exited with {process.ExitCode}: {errors}");
            }

            var marker = output.IndexOf("RESULTS ", StringComparison.Ordinal);
            if (marker < 0)
            {
                throw new InvalidOperationException($"The offline harness produced unexpected output: {output}");
            }

            var resultsJson = output[(marker + "RESULTS ".Length)..].Trim();
            return new OfflineHarnessResult(JsonDocument.Parse(resultsJson).RootElement.Clone());
        }
        finally
        {
            try
            {
                File.Delete(harnessPath);
            }
            catch (IOException)
            {
                // Temp cleanup is best effort; never masks a test result.
            }
        }
    }

    private static string FindToolPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "Invoke-PortalReportFixturePack.ps1");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Invoke-PortalReportFixturePack.ps1 was not found relative to the test output; the tool file must live under <repo>/tools.");
    }

    private static string BuildHarnessScript(string toolPath)
    {
        // A plain raw string keeps every PowerShell token verbatim; the dynamic
        // paths are token-replaced, never interpolated.
        var script = """
             $ErrorActionPreference = 'Stop'
             . '__TOOL_PATH__' -LibraryOnly

             $results = [ordered]@{}

             $results.loopbackOk = (Test-LoopbackHost '127.0.0.1') -and (Test-LoopbackHost 'localhost') -and (Test-LoopbackHost '::1') -and (Test-LoopbackHost '[::1]')
             $results.loopbackReject = (-not (Test-LoopbackHost 'example.com')) -and (-not (Test-LoopbackHost '')) -and (-not (Test-LoopbackHost '10.0.0.5'))

             $uri = Assert-LoopbackBaseUrl -Url 'http://localhost:5299'
             $results.baseUrlOk = ($uri.Host -eq 'localhost') -and ($uri.Port -eq 5299)
             $baseRemoteThrew = $false
             try { Assert-LoopbackBaseUrl -Url 'https://api.sanad.example' | Out-Null } catch { $baseRemoteThrew = $true }
             $results.baseUrlRejectsRemote = $baseRemoteThrew
             $baseSchemeThrew = $false
             try { Assert-LoopbackBaseUrl -Url 'ftp://localhost/x' | Out-Null } catch { $baseSchemeThrew = $true }
             $results.baseUrlRejectsNonHttp = $baseSchemeThrew

             $results.runTagSanitizes = ((Assert-SafeRunTag -RunTag 'w5f-aaaa') -eq 'w5faaaa') -and ((Assert-SafeRunTag -RunTag 'W5F AAAA!!!') -eq 'w5faaaa')
             $shortTagThrew = $false
             try { Assert-SafeRunTag -RunTag 'a' | Out-Null } catch { $shortTagThrew = $true }
             $results.runTagFailsClosed = $shortTagThrew

             $results.workRootExplicit = ((Resolve-FixtureWorkRoot -WorkRoot 'C:\tmp\sanad-fixture\') -eq 'C:\tmp\sanad-fixture')
             $results.workRootDefault = ((Resolve-FixtureWorkRoot -WorkRoot '' -TempRoot 'C:\temp') -eq 'C:\temp\opencode\sanad-portal-fixture')
             $results.credentialPath = ((Resolve-FixtureCredentialPath -WorkRoot 'C:\temp\opencode\sanad-portal-fixture' -RunTag 'w5faaaa') -eq 'C:\temp\opencode\sanad-portal-fixture\credentials-w5faaaa.json')
             $results.manifestPath = ((Resolve-FixtureManifestPath -WorkRoot 'C:\temp\opencode\sanad-portal-fixture' -RunTag 'w5faaaa') -eq 'C:\temp\opencode\sanad-portal-fixture\portal-fixture-manifest-w5faaaa.json')

             $results.connApproved = Test-ApprovedLocalConnectionString -ConnectionString 'Host=localhost;Port=5432;Database=SanadDb;Username=postgres;Password=x'
             $results.connRejectRemote = -not (Test-ApprovedLocalConnectionString -ConnectionString 'Host=db.prod.example;Port=5432;Database=SanadDb')
             $results.connRejectOtherDb = -not (Test-ApprovedLocalConnectionString -ConnectionString 'Host=localhost;Port=5432;Database=postgres')
             $results.connRejectOtherPort = -not (Test-ApprovedLocalConnectionString -ConnectionString 'Host=localhost;Port=6432;Database=SanadDb')

             $credJson = New-FixtureCredentialFileJson -ConnectionString 'Host=localhost;Port=5432;Database=SanadDb' -JwtSigningKey ('k' * 40) -FixturePassword ('p' * 14)
             $credObj = ConvertFrom-Json $credJson
             $results.credentialJsonShape = ($credObj.connectionString -eq 'Host=localhost;Port=5432;Database=SanadDb') -and ($credObj.jwtSigningKey.Length -eq 40) -and ($credObj.fixturePassword.Length -eq 14)
             $credRemoteThrew = $false
             try { New-FixtureCredentialFileJson -ConnectionString 'Host=remote;Port=5432;Database=SanadDb' -JwtSigningKey ('k' * 40) -FixturePassword ('p' * 14) | Out-Null } catch { $credRemoteThrew = $true }
             $results.credentialJsonRejectsRemote = $credRemoteThrew
             $credShortKeyThrew = $false
             try { New-FixtureCredentialFileJson -ConnectionString 'Host=localhost;Port=5432;Database=SanadDb' -JwtSigningKey 'short' -FixturePassword ('p' * 14) | Out-Null } catch { $credShortKeyThrew = $true }
             $results.credentialJsonRejectsShortKey = $credShortKeyThrew

             $redacted = Get-RedactedText 'Bearer eyJhbGciOiJIUzI1NiJ9.payload.sig password=secret123'
             $results.redactionWorks = ($redacted -notmatch 'eyJhbGciOiJIUzI1NiJ9') -and ($redacted -notmatch 'secret123')

             # Redaction must also cover connection-string fragments, the private
             # credential-file name and SANAD_W5_* surfaces, on BOTH channels.
             $redactedExt = Get-RedactedText 'Host=localhost;Port=5432;Database=SanadDb;Username=postgres;Password=dbpass credentials-w5faaaa.json SANAD_W5_JWT_SIGNING_KEY=abc'
             $results.redactionExtended = ($redactedExt -notmatch 'dbpass') -and ($redactedExt -notmatch 'credentials-w5faaaa\.json') -and ($redactedExt -notmatch 'SANAD_W5_JWT_SIGNING_KEY')
             $stderrLine = Get-RedactedStreamLine -Line 'Bearer abc123.token hit an error' -Kind 'stderr'
             $stdoutLine = Get-RedactedStreamLine -Line 'Host=localhost;Port=5432;Database=SanadDb Password=dbpass' -Kind 'stdout'
             $results.redactionStreamStderr = ($stderrLine -match '^stderr: ') -and ($stderrLine -notmatch 'abc123\.token') -and ($stdoutLine -notmatch '^stderr: ') -and ($stdoutLine -notmatch 'dbpass')

             # Correct OS detection: on Windows PowerShell 5.1 the old check
             # silently resolved to "not Windows" and skipped ACL hardening.
             $results.platformDetected = (Test-IsWindowsPlatform) -eq $true

             # A private credential artifact must live OUTSIDE the repository,
             # and an in-repo path must be refused (fail closed).
             $fixtureRepoRoot = Split-Path -Parent (Split-Path -Parent '__TOOL_PATH__')
             $outsideArtifact = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'sanad-fixture-outside.json')
             $results.artifactOutsideRepo = (Assert-ArtifactOutsideRepo -Path $outsideArtifact -RepoRoot $fixtureRepoRoot)
             $insideArtifactThrew = $false
             try { Assert-ArtifactOutsideRepo -Path ([System.IO.Path]::Combine($fixtureRepoRoot, 'tools', 'leak.json')) -RepoRoot $fixtureRepoRoot | Out-Null } catch { $insideArtifactThrew = $true }
             $results.artifactInsideRepoRefused = $insideArtifactThrew

             $results.revisionAccepts = (Assert-SafeRevision -Revision 'eb218a0') -and (Assert-SafeRevision -Revision '')
             $revThrew = $false
             try { Assert-SafeRevision -Revision 'bad rev!' | Out-Null } catch { $revThrew = $true }
             $results.revisionFailsClosed = $revThrew

             # Dot-sourcing the tool creates its [string] parameter variables in
             # this scope, and PowerShell names are case-insensitive: a local
             # $groups would inherit the tool's [string]$Groups constraint and
             # silently collapse the array to a space-joined string.
             $fixtureGroupIds = @(Get-PortalFixtureGroups)
             $results.groupCount12 = ($fixtureGroupIds.Count -eq 12)
             $results.groupIds = $fixtureGroupIds

             $plan1 = Get-PortalFixturePlan -RunTag 'w5faaaa'
             $plan2 = Get-PortalFixturePlan -RunTag 'w5faaaa'
             $results.planIdempotent = ((ConvertTo-PortalFixtureJson $plan1 8) -eq (ConvertTo-PortalFixtureJson $plan2 8))
             $results.planHasTwelveGroups = (@($plan1.groups).Count -eq 12)
             $results.planAddress = ($plan1.address -eq 'http://127.0.0.1:5299') -and ($plan1.target -eq 'localhost:5432/SanadDb')
             $results.planCleanupReportOnly = ($plan1.cleanupSurface -eq 'report-only')
             $results.planPaymentDoubles = (@($plan1.paymentDoubles).Count -eq 2)
             $results.planInertDoubles = (@($plan1.inertDoubles).Count -eq 4)

             $startNoConfirm = $false
             try { Assert-PortalFixtureModeCombination -Mode 'Start' -HasConfirmStart $false -HasConfirmStop $false -HasCredentialFile $true -HasRunManifestPath $false | Out-Null } catch { $startNoConfirm = $true }
             $results.startRequiresConfirm = $startNoConfirm
             $startOk = $true
             try { Assert-PortalFixtureModeCombination -Mode 'Start' -HasConfirmStart $true -HasConfirmStop $false -HasCredentialFile $true -HasRunManifestPath $false | Out-Null } catch { $startOk = $false }
             $results.startAcceptsConfirm = $startOk
             $stopNoConfirm = $false
             try { Assert-PortalFixtureModeCombination -Mode 'Stop' -HasConfirmStart $false -HasConfirmStop $false -HasCredentialFile $false -HasRunManifestPath $false | Out-Null } catch { $stopNoConfirm = $true }
             $results.stopRequiresConfirm = $stopNoConfirm
             $stopOk = $true
             try { Assert-PortalFixtureModeCombination -Mode 'Stop' -HasConfirmStart $false -HasConfirmStop $true -HasCredentialFile $false -HasRunManifestPath $false | Out-Null } catch { $stopOk = $false }
             $results.stopAcceptsConfirm = $stopOk
             $preflightNoCred = $false
             try { Assert-PortalFixtureModeCombination -Mode 'Preflight' -HasConfirmStart $false -HasConfirmStop $false -HasCredentialFile $false -HasRunManifestPath $false | Out-Null } catch { $preflightNoCred = $true }
             $results.preflightRequiresCredential = $preflightNoCred
             $cleanupOk = $true
             try { Assert-PortalFixtureModeCombination -Mode 'Cleanup' -HasConfirmStart $false -HasConfirmStop $false -HasCredentialFile $false -HasRunManifestPath $true | Out-Null } catch { $cleanupOk = $false }
             $results.cleanupNoConfirm = $cleanupOk
             $cleanupConfirmThrew = $false
             try { Assert-PortalFixtureModeCombination -Mode 'Cleanup' -HasConfirmStart $true -HasConfirmStop $false -HasCredentialFile $false -HasRunManifestPath $true | Out-Null } catch { $cleanupConfirmThrew = $true }
             $results.cleanupRejectsConfirm = $cleanupConfirmThrew
             $planStrayThrew = $false
             try { Assert-PortalFixtureModeCombination -Mode 'Plan' -HasConfirmStart $true -HasConfirmStop $false -HasCredentialFile $false -HasRunManifestPath $false | Out-Null } catch { $planStrayThrew = $true }
             $results.planRejectsStray = $planStrayThrew
             $unknownThrew = $false
             try { Assert-PortalFixtureModeCombination -Mode 'Nope' -HasConfirmStart $false -HasConfirmStop $false -HasCredentialFile $false -HasRunManifestPath $false | Out-Null } catch { $unknownThrew = $true }
             $results.unknownModeRejected = $unknownThrew

             $startArgs = @(Get-PortalFixtureHostArguments -Mode 'Start' -Address 'http://127.0.0.1:5299' -RunTag 'w5f-aaaa' -Revision 'eb218a0' -CredentialFile 'C:\c.json' -WorkRoot 'C:\w' -Instant '2026-10-24T10:00:00Z' -Groups 'caregiver-refund-ambiguity' -AllowPartial $false)
             $results.hostArgsStart = (($startArgs -join ' ') -eq '--mode start --address http://127.0.0.1:5299 --run-tag w5faaaa --revision eb218a0 --credential-file C:\c.json --work-root C:\w --instant 2026-10-24T10:00:00Z --provision-groups caregiver-refund-ambiguity')
             $reuseArgs = @(Get-PortalFixtureHostArguments -Mode 'Reuse' -Address 'http://127.0.0.1:5299' -RunTag '' -Revision '' -CredentialFile '' -WorkRoot '' -Instant '' -Groups '' -AllowPartial $false)
             $results.hostArgsReuse = (($reuseArgs -join ' ') -eq '--mode reuse --address http://127.0.0.1:5299')
             $partialArgs = @(Get-PortalFixtureHostArguments -Mode 'Start' -Address '' -RunTag 'w5p' -Revision '' -CredentialFile '' -WorkRoot '' -Instant '' -Groups '' -AllowPartial $true)
             $results.hostArgsAllowPartial = $partialArgs -contains '--allow-partial'

             $decision = ConvertFrom-PortalFixtureIdentityProbe -Json '{"marker":"sanad-portal-fixture-host","revision":"eb218a0"}' -ExpectedRevision 'eb218a0'
             $results.probeMatches = ($decision.passed -eq $true) -and ($decision.isPortalFixtureHost -eq $true) -and ($decision.revisionMatches -eq $true)
             $foreign = ConvertFrom-PortalFixtureIdentityProbe -Json '{"marker":"other-app","revision":"eb218a0"}' -ExpectedRevision 'eb218a0'
             $results.probeRefusesForeign = ($foreign.passed -eq $false) -and ($foreign.isPortalFixtureHost -eq $false)
             $missing = ConvertFrom-PortalFixtureIdentityProbe -Json '{"status":"ok"}' -ExpectedRevision 'eb218a0'
             $results.probeRefusesMissing = ($missing.passed -eq $false)
             $mismatch = ConvertFrom-PortalFixtureIdentityProbe -Json '{"marker":"sanad-portal-fixture-host","revision":"deadbee"}' -ExpectedRevision 'eb218a0'
             $results.probeRefusesRevisionMismatch = ($mismatch.passed -eq $false) -and ($mismatch.isPortalFixtureHost -eq $true)
             $unparseable = ConvertFrom-PortalFixtureIdentityProbe -Json 'not json' -ExpectedRevision 'eb218a0'
             $results.probeRefusesUnparseable = ($unparseable.passed -eq $false)

             $records = @(
                 [ordered]@{ Kind = 'FamiliesBooking'; Id = 'b1'; State = 'Confirmed' },
                 [ordered]@{ Kind = 'DuplicateChargeClaim'; Id = 'c1'; State = 'Submitted' },
                 [ordered]@{ Kind = 'ComplaintReview'; Id = 'r1'; State = 'Received' }
             )
             $cleanup = @(Get-PortalFixtureCleanupReport -Records $records)
             $results.cleanupReportShape = ($cleanup.Count -eq 3) -and ($cleanup[0].classification -eq 'retained-operator-decision') -and ($cleanup[1].classification -eq 'report-only-money-free') -and ($cleanup[2].classification -eq 'report-only-money-free')

             $results.noCleanupSqlFunction = $null -eq (Get-Command Get-PortalFixtureCleanupSql -ErrorAction SilentlyContinue)
             $results.noCleanupSqlGuardFunction = $null -eq (Get-Command Assert-PortalFixtureCleanupSqlSafety -ErrorAction SilentlyContinue)
             $toolText = [System.IO.File]::ReadAllText('__TOOL_PATH__')
             $results.noDeleteFromInTool = -not ($toolText -match '(?i)\bdelete\s+from\b')

             Write-Output 'RESULTS ' (ConvertTo-PortalFixtureJson $results 8)
             """;

        // PowerShell single-quoted paths escape embedded quotes by doubling.
        var escapedToolPath = toolPath.Replace("'", "''", StringComparison.Ordinal);
        return script.Replace("__TOOL_PATH__", escapedToolPath, StringComparison.Ordinal);
    }

    private sealed record OfflineHarnessResult(JsonElement Flags)
    {
        public bool GetFlag(string key) =>
            Flags.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;

        public string[] GetStringArray(string key)
        {
            if (!Flags.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"Harness payload key '{key}' is missing or not an array.");
            }

            return [.. value.EnumerateArray().Select(element => element.GetString() ?? string.Empty)];
        }
    }
}
