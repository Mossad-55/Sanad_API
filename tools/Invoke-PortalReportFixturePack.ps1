#requires -Version 5.1
<#
.SYNOPSIS
    Safe local lifecycle wrapper for the W5 portal-report fixture pack
    (portal-api-reconciliation brief, section "W5 fixture inventory").

.DESCRIPTION
    The wrapper never writes fixture data itself and never talks to a provider.
    It drives the bounded test-only host under
    tools/Ahlena.PortalFixtureHost, which serves the REAL API controllers on a
    loopback address and substitutes every provider-capable seam with explicit
    scripted/inert doubles proven by DI resolution before it listens.

    Modes (Plan is the default and does nothing but print a plan):

      Plan        Deterministic description of the host contract, the twelve
                  W5 scenario groups and the loopback address. No credential,
                  no process, no database, no listener.

      Preflight   Builds the host container, proves the exact local target
                  (localhost:5432/AhlenaDb, zero pending migrations) and the DI
                  isolation, prints a JSON report and exits. Read-only: it
                  never listens, never writes and never migrates.

      Start       Requires -ConfirmStart. Writes the private runtime
                  credential file under the approved OpenCode temp root with a
                  current-user-only ACL, then runs the host in start mode:
                  preflight, bounded provisioning, then the loopback listener.

      Reuse       Probes an already-running listener at -BaseUrl and reports
                  whether it is THIS fixture host for the pinned revision. An
                  unknown listener is refused.

      Stop        Requires -ConfirmStop and -BaseUrl. Confirms the listener is
                  THIS host (never an unknown process) and stops only that
                  process.

      Cleanup     REPORT ONLY. Reads a run manifest and classifies its
                  fixture-owned records. This tool has no delete surface at
                  all: it never emits or executes SQL, and it deletes nothing.

    Safety properties (enforced, not assumed):

      - The base URL must be a loopback HTTP(S) listener. There is no remote
        fallback and no way to pass one.
      - Credentials are read from AHLENA_W5_* environment variables, validated
        against the approved local target, written only to the private
        credential file and never printed, logged or written to a manifest.
      - The connection string must target localhost:5432/AhlenaDb exactly.
      - Cleanup is report-only; no DELETE statement is constructed anywhere.
      - Offline tests dot-source this file with -LibraryOnly, which defines the
        pure functions and returns immediately: no I/O, no process, no HTTP,
        no database and no credential access.

.NOTES
    Full safe-use workflow and the scenario map live in
    docs/internal/work/portal-api-reconciliation/portal-report-fixture-pack.md
#>
[CmdletBinding()]
param(
    [ValidateSet('Plan', 'Preflight', 'Start', 'Reuse', 'Stop', 'Cleanup')]
    [string]$Mode = 'Plan',

    [string]$BaseUrl = 'http://127.0.0.1:5299',

    [string]$WorkRoot,

    [string]$RunTag = 'w5p',

    [string]$Revision,

    [string]$Instant,

    [string]$Groups,

    [string]$RunManifestPath,

    [string]$HostProjectPath,

    [switch]$AllowPartial,

    [switch]$ConfirmStart,

    [switch]$ConfirmStop,

    [switch]$LibraryOnly
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Pure helpers (offline-testable; define no I/O and are safe to dot-source)
# ---------------------------------------------------------------------------

function ConvertTo-PortalFixtureJson {
    [CmdletBinding()]
    param([object]$Value, [int]$Depth = 8)
    return (ConvertTo-Json -InputObject $Value -Depth $Depth -Compress)
}

function Test-LoopbackHost {
    <#
        True only for localhost or a literal loopback IP address. .NET expands
        IPv6 hosts to the bracketed form, so parse rather than match text.
    #>
    [CmdletBinding()]
    param([string]$HostName)

    if ([string]::IsNullOrWhiteSpace($HostName)) { return $false }

    $candidate = $HostName.Trim([char[]]@('[', ']'))
    if ($candidate -eq 'localhost') { return $true }

    $address = $null
    if ([System.Net.IPAddress]::TryParse($candidate, [ref]$address)) {
        return [System.Net.IPAddress]::IsLoopback($address)
    }

    return $false
}

function Assert-LoopbackBaseUrl {
    <#
        Resolves a base URL and fails closed unless it is an absolute
        HTTP(S) URL whose host is loopback.
    #>
    [CmdletBinding()]
    param([string]$Url)

    if ([string]::IsNullOrWhiteSpace($Url)) {
        throw 'A loopback base URL is required.'
    }

    $uri = $null
    if (-not [System.Uri]::TryCreate($Url, [System.UriKind]::Absolute, [ref]$uri)) {
        throw "The base URL '$Url' is not an absolute URL."
    }

    if ($uri.Scheme -ne 'http' -and $uri.Scheme -ne 'https') {
        throw "The base URL '$Url' must use http or https."
    }

    if (-not (Test-LoopbackHost -HostName $uri.Host)) {
        throw "The base URL '$Url' must target a loopback host (localhost / 127.0.0.1 / ::1)."
    }

    return $uri
}

function Assert-SafeRunTag {
    <#
        Normalizes a run tag to a bounded [a-z0-9] token and fails closed when
        nothing usable remains. The tag is stamped on every fixture narrative
        field, so it must be a safe single token.
    #>
    [CmdletBinding()]
    param([string]$RunTag)

    if ([string]::IsNullOrWhiteSpace($RunTag)) {
        throw 'A run tag is required.'
    }

    $tag = ($RunTag.ToLowerInvariant() -replace '[^a-z0-9]', '')
    if ($tag.Length -lt 2) {
        throw "The run tag '$RunTag' must contain at least two letters or digits."
    }

    if ($tag.Length -gt 16) { $tag = $tag.Substring(0, 16) }
    return $tag
}

function Resolve-FixtureWorkRoot {
    <#
        Resolves the private artifact root. With no explicit -WorkRoot it lives
        under the approved OpenCode temp root so no repo path is ever used.
    #>
    [CmdletBinding()]
    param([string]$WorkRoot, [string]$TempRoot)

    if (-not [string]::IsNullOrWhiteSpace($WorkRoot)) {
        return [System.IO.Path]::GetFullPath($WorkRoot).TrimEnd([char[]]@('\', '/'))
    }

    $root = $TempRoot
    if ([string]::IsNullOrWhiteSpace($root)) {
        $root = [System.IO.Path]::GetTempPath()
    }

    return [System.IO.Path]::GetFullPath(
        (Join-Path (Join-Path $root 'opencode') 'sanad-portal-fixture')).TrimEnd([char[]]@('\', '/'))
}

function Resolve-FixtureCredentialPath {
    [CmdletBinding()]
    param([string]$WorkRoot, [string]$RunTag)
    return [System.IO.Path]::Combine((Resolve-FixtureWorkRoot -WorkRoot $WorkRoot), "credentials-$RunTag.json")
}

function Resolve-FixtureManifestPath {
    [CmdletBinding()]
    param([string]$WorkRoot, [string]$RunTag)
    return [System.IO.Path]::Combine((Resolve-FixtureWorkRoot -WorkRoot $WorkRoot), "portal-fixture-manifest-$RunTag.json")
}

function Test-ApprovedLocalConnectionString {
    <#
        True only when the connection string targets localhost:5432/AhlenaDb.
        Never echoes the string.
    #>
    [CmdletBinding()]
    param([string]$ConnectionString)

    if ([string]::IsNullOrWhiteSpace($ConnectionString)) { return $false }

    try {
        $builder = New-Object System.Data.Common.DbConnectionStringBuilder
        $builder.set_ConnectionString($ConnectionString)
    } catch {
        return $false
    }

    $keys = @($builder.Keys)
    if (-not ($keys -contains 'Host') -or -not ($keys -contains 'Port') -or -not ($keys -contains 'Database')) {
        return $false
    }

    $hostValue = [string]$builder['Host']
    $port = 0
    if (-not [int]::TryParse([string]$builder['Port'], [ref]$port)) { return $false }

    return (Test-LoopbackHost -HostName $hostValue) -and ($port -eq 5432) -and ([string]$builder['Database'] -eq 'AhlenaDb')
}

function New-FixtureCredentialFileJson {
    <#
        Builds the private credential file body. It validates the approved
        local target and minimum secret lengths and never returns a value to
        the caller's output stream unless asked, so no secret is logged.
    #>
    [CmdletBinding()]
    param(
        [string]$ConnectionString,
        [string]$JwtSigningKey,
        [string]$FixturePassword
    )

    if (-not (Test-ApprovedLocalConnectionString -ConnectionString $ConnectionString)) {
        throw 'The connection string must target localhost:5432/AhlenaDb.'
    }

    if ([string]::IsNullOrWhiteSpace($JwtSigningKey) -or $JwtSigningKey.Length -lt 32) {
        throw 'The JWT signing key must be at least 32 characters.'
    }

    if ([string]::IsNullOrWhiteSpace($FixturePassword) -or $FixturePassword.Length -lt 12) {
        throw 'The fixture password must be at least 12 characters.'
    }

    $body = [ordered]@{
        connectionString = $ConnectionString
        jwtSigningKey    = $JwtSigningKey
        fixturePassword  = $FixturePassword
    }

    return (ConvertTo-PortalFixtureJson $body 4)
}

function Get-RedactedText {
    <#
        Removes tokens, passwords, connection strings, credential-env values
        and private credential-file names from any line so a failure — on
        stdout OR stderr, from any mode — can never leak a credential.
    #>
    [CmdletBinding()]
    param([string]$Text)

    if ([string]::IsNullOrEmpty($Text)) { return $Text }

    $redacted = [regex]::Replace($Text, '(?i)\b(bearer|basic)\s+[A-Za-z0-9._\-]+', '$1 <redacted>')
    $redacted = [regex]::Replace($redacted, '(?i)(password|passwd|pwd|secret|signingkey|signing_key|token)\s*[=:]\s*[^\s;]+', '$1=<redacted>')
    # A connection-string fragment (Host=...;Port=...;Username=...;...) carries
    # the local DB password: redact the whole token, never just the password key.
    $redacted = [regex]::Replace($redacted, '(?i)\b(host|server)\s*=\s*[^\s]+', '$1=<redacted>')
    # The private credential file itself is a credential-bearing path.
    $redacted = [regex]::Replace($redacted, '(?i)credentials-[a-z0-9]+\.json', 'credentials-<redacted>.json')
    # Any AHLENA_W5_* surface (env name or assignment) is private.
    $redacted = [regex]::Replace($redacted, '(?i)AHLENA_W5_[A-Z0-9_]+', 'AHLENA_W5_<redacted>')
    return $redacted
}

function Get-RedactedStreamLine {
    <#
        Redacts one streamed process line. Both diagnostics channels go through
        this, so stdout and stderr are redacted identically for every mode.
    #>
    [CmdletBinding()]
    param(
        [string]$Line,
        [ValidateSet('stdout', 'stderr')]
        [string]$Kind = 'stdout'
    )

    $prefix = if ($Kind -eq 'stderr') { 'stderr: ' } else { '' }
    return $prefix + (Get-RedactedText -Text $Line)
}

function Test-IsWindowsPlatform {
    <#
        Correct OS detection across Windows PowerShell 5.1 and PowerShell 7+.
        In 5.1 neither $IsWindows nor $PSVersionTable.Platform exists, so the
        old check silently resolved to "not Windows" and skipped ACL hardening.
    #>
    [CmdletBinding()]
    param()

    if ($PSVersionTable.PSVersion.Major -ge 6) {
        return [bool]$IsWindows
    }

    return ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT)
}

function Assert-ArtifactOutsideRepo {
    <#
        A private credential artifact must live OUTSIDE the repository. Fails
        closed (throws) when the resolved path is inside the repo root.
    #>
    [CmdletBinding()]
    param([string]$Path, [string]$RepoRoot)

    if ([string]::IsNullOrWhiteSpace($Path) -or [string]::IsNullOrWhiteSpace($RepoRoot)) {
        throw 'Both the artifact path and the repository root are required.'
    }

    $full = [System.IO.Path]::GetFullPath($Path)
    $repo = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd([char[]]@('\', '/'))
    $prefix = $repo + [System.IO.Path]::DirectorySeparatorChar
    $inside = $full.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase) -or
        ($full.TrimEnd([char[]]@('\', '/')) -eq $repo)

    if ($inside) {
        throw "The private fixture artifact must live outside the repository; '$full' is inside '$repo'."
    }

    return $true
}

function Assert-SafeRevision {
    <#
        A revision token is an optional short hex SHA or safe identifier; it is
        echoed into the identity probe report, so reject anything else.
    #>
    [CmdletBinding()]
    param([string]$Revision)

    if ([string]::IsNullOrWhiteSpace($Revision)) { return $true }
    if ($Revision -notmatch '^[A-Za-z0-9._\-]{4,64}$') {
        throw 'The revision must be a short hex SHA or safe identifier.'
    }

    return $true
}

function Get-PortalFixtureGroups {
    <#
        The twelve W5 scenario groups. The ids are the host's
        PortalFixtureGroups constants and the manifest keys; keeping exactly
        one list here is what lets the offline tests pin the host contract.
    #>
    [CmdletBinding()]
    param()

    return @(
        'caregiver-refund-ambiguity',
        'caregiver-refund-failure',
        'caregiver-completed-booking',
        'carehome-paid-awaiting-decision',
        'carehome-refund-ambiguity',
        'carehome-refund-failure',
        'carehome-evidenced-external-refund',
        'carehome-booking-extension',
        'carehome-checkout-dispute',
        'subscription-and-duplicate-claim',
        'complaint-received',
        'complaint-investigating'
    )
}

function Get-PortalFixturePlan {
    <#
        Deterministic, side-effect-free plan: the loopback address, the local
        target, the two scripted payment doubles, the inert doubles and the
        twelve groups. Same run tag -> same plan.
    #>
    [CmdletBinding()]
    param([string]$RunTag = 'w5p')

    $tag = Assert-SafeRunTag -RunTag $RunTag

    return [ordered]@{
        host          = 'sanad-portal-fixture-host'
        runTag        = $tag
        address       = 'http://127.0.0.1:5299'
        target        = 'localhost:5432/AhlenaDb'
        paymentDoubles = @(
            'Families IPaymobClient -> ScriptedPaymobClient',
            'Finance ISubscriptionRefundGateway -> ScriptedSubscriptionRefundGateway'
        )
        inertDoubles  = @(
            'Identity ISmsSender -> PortalFixtureInertSmsSender',
            'Identity IEmailSender -> PortalFixtureInertEmailSender',
            'Notifications IPushSender -> PortalFixtureInertPushSender',
            'IDateTimeProvider -> PortalFixtureClock (test-host-only)'
        )
        modes         = @('Plan', 'Preflight', 'Start', 'Reuse', 'Stop', 'Cleanup')
        cleanupSurface = 'report-only'
        groups        = @(Get-PortalFixtureGroups)
    }
}

function Assert-PortalFixtureModeCombination {
    <#
        Fail closed on stray/absent switches per mode so a raw -ConfirmStart or
        a credential path can never be silently ignored.
    #>
    [CmdletBinding()]
    param(
        [string]$Mode,
        [bool]$HasConfirmStart,
        [bool]$HasConfirmStop,
        [bool]$HasCredentialFile,
        [bool]$HasRunManifestPath
    )

    switch ($Mode) {
        'Plan' {
            if ($HasConfirmStart -or $HasConfirmStop -or $HasCredentialFile -or $HasRunManifestPath) {
                throw 'Plan accepts no confirm switch, credential file or manifest path.'
            }
        }
        'Preflight' {
            if ($HasConfirmStart -or $HasConfirmStop) { throw 'Preflight accepts no confirm switch.' }
            if (-not $HasCredentialFile) { throw 'Preflight requires a private credential file.' }
        }
        'Start' {
            if (-not $HasCredentialFile) { throw 'Start requires a private credential file.' }
            if (-not $HasConfirmStart) { throw 'Start requires the explicit -ConfirmStart switch.' }
            if ($HasConfirmStop) { throw 'Start does not accept -ConfirmStop.' }
        }
        'Reuse' {
            if ($HasConfirmStart -or $HasConfirmStop -or $HasCredentialFile) {
                throw 'Reuse accepts no confirm switch or credential file.'
            }
        }
        'Stop' {
            if (-not $HasConfirmStop) { throw 'Stop requires the explicit -ConfirmStop switch.' }
            if ($HasConfirmStart -or $HasCredentialFile) { throw 'Stop accepts no credential file.' }
        }
        'Cleanup' {
            if ($HasConfirmStart -or $HasConfirmStop) { throw 'Cleanup is report-only and accepts no confirm switch.' }
            if ($HasCredentialFile) { throw 'Cleanup never reads a credential file.' }
            if (-not $HasRunManifestPath) { throw 'Cleanup requires -RunManifestPath.' }
        }
        default { throw "Unknown mode '$Mode'." }
    }

    return $true
}

function Get-PortalFixtureHostArguments {
    <#
        Builds the exact argv for the bounded host. Pure: nothing is executed.
    #>
    [CmdletBinding()]
    param(
        [string]$Mode,
        [string]$Address,
        [string]$RunTag,
        [string]$Revision,
        [string]$CredentialFile,
        [string]$WorkRoot,
        [string]$Instant,
        [string]$Groups,
        [bool]$AllowPartial
    )

    $arguments = New-Object System.Collections.Generic.List[string]
    $arguments.Add('--mode')
    $arguments.Add($Mode.ToLowerInvariant())

    if (-not [string]::IsNullOrWhiteSpace($Address)) {
        $arguments.Add('--address'); $arguments.Add($Address)
    }

    if (-not [string]::IsNullOrWhiteSpace($RunTag)) {
        $arguments.Add('--run-tag'); $arguments.Add((Assert-SafeRunTag -RunTag $RunTag))
    }

    if (-not [string]::IsNullOrWhiteSpace($Revision)) {
        Assert-SafeRevision -Revision $Revision | Out-Null
        $arguments.Add('--revision'); $arguments.Add($Revision)
    }

    if (-not [string]::IsNullOrWhiteSpace($CredentialFile)) {
        $arguments.Add('--credential-file'); $arguments.Add($CredentialFile)
    }

    if (-not [string]::IsNullOrWhiteSpace($WorkRoot)) {
        $arguments.Add('--work-root'); $arguments.Add($WorkRoot)
    }

    if (-not [string]::IsNullOrWhiteSpace($Instant)) {
        $arguments.Add('--instant'); $arguments.Add($Instant)
    }

    if (-not [string]::IsNullOrWhiteSpace($Groups)) {
        $arguments.Add('--provision-groups'); $arguments.Add($Groups)
    }

    if ($AllowPartial) { $arguments.Add('--allow-partial') }

    return $arguments.ToArray()
}

function ConvertFrom-PortalFixtureIdentityProbe {
    <#
        Parses the host's /__portal-fixture/identity body and decides whether
        the listener is THIS host for the pinned revision. An unknown listener
        (missing marker) is refused.
    #>
    [CmdletBinding()]
    param([string]$Json, [string]$ExpectedRevision)

    try {
        $parsed = ConvertFrom-Json -InputObject $Json
    } catch {
        return [ordered]@{ passed = $false; isPortalFixtureHost = $false; revisionMatches = $false; reason = 'unparseable' }
    }

    $marker = $null
    if ($parsed.PSObject.Properties.Name -contains 'marker') { $marker = [string]$parsed.marker }
    $revision = $null
    if ($parsed.PSObject.Properties.Name -contains 'revision') { $revision = [string]$parsed.revision }

    $isHost = ($marker -eq 'sanad-portal-fixture-host')
    $revisionMatches = [string]::IsNullOrWhiteSpace($ExpectedRevision) -or ($revision -eq $ExpectedRevision)

    return [ordered]@{
        passed              = ($isHost -and $revisionMatches)
        isPortalFixtureHost = $isHost
        revisionMatches     = $revisionMatches
        reason              = 'ok'
    }
}

function Get-PortalFixtureCleanupReport {
    <#
        REPORT ONLY classification of manifest records. Returns a bucket per
        record; it never builds SQL and never deletes anything. Money-adjacent
        records are always retained for an explicit operator decision.
    #>
    [CmdletBinding()]
    param([object[]]$Records)

    $moneyFree = @('DuplicateChargeClaim', 'ComplaintReview')
    $report = New-Object System.Collections.Generic.List[object]

    foreach ($record in @($Records)) {
        if ($null -eq $record) { continue }
        $kind = [string]$record.Kind
        $state = [string]$record.State
        $classification = if ($moneyFree -contains $kind) { 'report-only-money-free' } else { 'retained-operator-decision' }

        $report.Add([ordered]@{
            kind           = $kind
            id             = [string]$record.Id
            state          = $state
            classification = $classification
        })
    }

    return $report.ToArray()
}

# ---------------------------------------------------------------------------
# Library-only offline mode: nothing below this line runs during dot-source.
# ---------------------------------------------------------------------------

if ($LibraryOnly) { return }

# ---------------------------------------------------------------------------
# Mode dispatch (I/O happens only from here down)
# ---------------------------------------------------------------------------

function Resolve-FixtureRepoRoot {
    $candidate = Split-Path -Parent $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($candidate)) { return $PSScriptRoot }
    return $candidate
}

function Resolve-HostProjectPath {
    param([string]$HostProjectPath)

    if (-not [string]::IsNullOrWhiteSpace($HostProjectPath)) {
        return [System.IO.Path]::GetFullPath($HostProjectPath)
    }

    return [System.IO.Path]::Combine(
        (Resolve-FixtureRepoRoot), 'tools', 'Ahlena.PortalFixtureHost', 'Ahlena.PortalFixtureHost.csproj')
}

function Write-SafeLine {
    param([string]$Text)
    Write-Host (Get-RedactedText -Text $Text)
}

function Set-CurrentUserOnlyAcl {
    <#
        Restricts the credential file to the current user. FAILS CLOSED: any
        failure to prove the restriction throws, so a secret-bearing file is
        never left readable. Windows detection is correct across Windows
        PowerShell 5.1 and PowerShell 7+ (no silent skip).
        The file is created EMPTY first, then hardened, then filled — the ACL
        always exists before any secret byte is written.
    #>
    [CmdletBinding()]
    param([string]$Path)

    if (-not (Test-IsWindowsPlatform)) {
        # POSIX: the file must be owner-only (0600). Fail closed when we cannot
        # prove it rather than writing a world-readable secret.
        if ([System.Environment]::Version.Major -lt 7) {
            throw 'The credential file ACL cannot be restricted on this non-Windows host; refusing to write a secret-bearing file.'
        }

        [System.IO.File]::SetUnixFileMode(
            $Path,
            [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite)

        $mask = [System.IO.UnixFileMode]::GroupRead -bor [System.IO.UnixFileMode]::GroupWrite -bor
        [System.IO.UnixFileMode]::OtherRead -bor [System.IO.UnixFileMode]::OtherWrite
        if (([System.IO.File]::GetUnixFileMode($Path) -band $mask) -ne 0) {
            throw 'The credential file ACL is not owner-only; refusing to continue.'
        }

        return $true
    }

    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($rule in @($acl.Access)) { $acl.RemoveAccessRule($rule) | Out-Null }
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $identity, 'FullControl', 'Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $Path -AclObject $acl

    $verify = Get-Acl -LiteralPath $Path
    if (-not $verify.AreAccessRulesProtected) {
        throw 'The credential file ACL is not protected; refusing to continue.'
    }

    return $true
}

function New-FixtureCredentialFile {
    [CmdletBinding()]
    param([string]$Path, [string]$RepoRoot)

    # The private artifact must never be written inside the repository.
    Assert-ArtifactOutsideRepo -Path $Path -RepoRoot $RepoRoot | Out-Null

    $connectionString = [Environment]::GetEnvironmentVariable('AHLENA_W5_CONNECTION_STRING')
    $jwtSigningKey = [Environment]::GetEnvironmentVariable('AHLENA_W5_JWT_SIGNING_KEY')
    $fixturePassword = [Environment]::GetEnvironmentVariable('AHLENA_W5_FIXTURE_PASSWORD')

    $json = New-FixtureCredentialFileJson `
        -ConnectionString $connectionString `
        -JwtSigningKey $jwtSigningKey `
        -FixturePassword $fixturePassword

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    # Create EMPTY, harden the ACL, and only then write the secret bytes: the
    # file never exists with contents and a permissive ACL, not even briefly.
    [System.IO.File]::WriteAllText($Path, [string]::Empty)
    try {
        Set-CurrentUserOnlyAcl -Path $Path | Out-Null
    } catch {
        Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
        throw "The credential file ACL could not be restricted; refusing to write secret contents: $(Get-RedactedText -Text $_.Exception.Message)"
    }

    [System.IO.File]::WriteAllText($Path, $json)
    Write-SafeLine "credential file written to $Path (contents never printed)."
}

function Invoke-FixtureHost {
    <#
        Runs the bounded host and STREAMS both diagnostics channels through the
        redactor line by line, so no secret can be printed (stdout AND stderr,
        for every mode) and a long-running Start is never buffered until exit.
        The native exit code is preserved.
    #>
    [CmdletBinding()]
    param([string]$HostMode, [string[]]$ExtraArguments)

    $project = Resolve-HostProjectPath -HostProjectPath $HostProjectPath
    $arguments = @('run', '--project', $project, '-c', 'Release', '--no-launch-profile', '--')
    $arguments += (Get-PortalFixtureHostArguments `
        -Mode $HostMode `
        -Address $BaseUrl `
        -RunTag $RunTag `
        -Revision $Revision `
        -CredentialFile $ExtraArguments[0] `
        -WorkRoot $ExtraArguments[1] `
        -Instant $Instant `
        -Groups $Groups `
        -AllowPartial:$AllowPartial)

    $previousPreference = $ErrorActionPreference
    # Native stderr is DATA here, not a terminating error; keep streaming it.
    $ErrorActionPreference = 'Continue'
    try {
        & dotnet @arguments 2>&1 | ForEach-Object {
            if ($_ -is [System.Management.Automation.ErrorRecord]) {
                Write-SafeLine (Get-RedactedStreamLine -Line ([string]$_) -Kind 'stderr')
            } else {
                Write-SafeLine (Get-RedactedStreamLine -Line ([string]$_) -Kind 'stdout')
            }
        }

        $code = $LASTEXITCODE
    } catch {
        Write-SafeLine "fixture host invocation failed: $(Get-RedactedText -Text $_.Exception.Message)"
        return 1
    } finally {
        $ErrorActionPreference = $previousPreference
    }

    if ($null -eq $code) { return 1 }
    return [int]$code
}

# Every mode runs under one redacted failure boundary: an unexpected throw in
# ANY mode (including the credential- and process-handling paths above) is
# reported with credentials scrubbed and a non-zero exit, never as a raw
# exception message that could carry a secret or a credential-bearing path.
try {
    switch ($Mode) {
        'Plan' {
            Assert-PortalFixtureModeCombination -Mode 'Plan' -HasConfirmStart:$ConfirmStart.IsPresent `
                -HasConfirmStop:$ConfirmStop.IsPresent -HasCredentialFile:$false `
                -HasRunManifestPath:(-not [string]::IsNullOrWhiteSpace($RunManifestPath)) | Out-Null

            $plan = Get-PortalFixturePlan -RunTag $RunTag
            Write-Output (ConvertTo-Json -InputObject $plan -Depth 8)
        }

        'Preflight' {
            $workRoot = Resolve-FixtureWorkRoot -WorkRoot $WorkRoot
            $credentialFile = Resolve-FixtureCredentialPath -WorkRoot $workRoot -RunTag (Assert-SafeRunTag -RunTag $RunTag)
            Assert-PortalFixtureModeCombination -Mode 'Preflight' -HasConfirmStart:$false `
                -HasConfirmStop:$false -HasCredentialFile:$true -HasRunManifestPath:$false | Out-Null

            New-FixtureCredentialFile -Path $credentialFile -RepoRoot (Resolve-FixtureRepoRoot)
            $code = Invoke-FixtureHost -HostMode 'preflight' -ExtraArguments @($credentialFile, $workRoot)
            exit $code
        }

        'Start' {
            $workRoot = Resolve-FixtureWorkRoot -WorkRoot $WorkRoot
            $credentialFile = Resolve-FixtureCredentialPath -WorkRoot $workRoot -RunTag (Assert-SafeRunTag -RunTag $RunTag)
            Assert-PortalFixtureModeCombination -Mode 'Start' -HasConfirmStart:$ConfirmStart.IsPresent `
                -HasConfirmStop:$ConfirmStop.IsPresent -HasCredentialFile:$true -HasRunManifestPath:$false | Out-Null
            $uri = Assert-LoopbackBaseUrl -Url $BaseUrl

            New-FixtureCredentialFile -Path $credentialFile -RepoRoot (Resolve-FixtureRepoRoot)
            Write-SafeLine "starting bounded fixture host on $($uri.AbsoluteUri)"
            $code = Invoke-FixtureHost -HostMode 'start' -ExtraArguments @($credentialFile, $workRoot)
            exit $code
        }

        'Reuse' {
            Assert-PortalFixtureModeCombination -Mode 'Reuse' -HasConfirmStart:$false `
                -HasConfirmStop:$false -HasCredentialFile:$false -HasRunManifestPath:$false | Out-Null
            $uri = Assert-LoopbackBaseUrl -Url $BaseUrl

            try {
                $response = Invoke-WebRequest -Uri "$($uri.AbsoluteUri.TrimEnd('/'))/__portal-fixture/identity" `
                    -UseBasicParsing -TimeoutSec 10
                $decision = ConvertFrom-PortalFixtureIdentityProbe -Json $response.Content -ExpectedRevision $Revision
                Write-Output (ConvertTo-PortalFixtureJson $decision 4)
                exit $(if ($decision.passed) { 0 } else { 1 })
            } catch {
                Write-SafeLine "reuse probe refused: $(Get-RedactedText -Text $_.Exception.Message)"
                exit 1
            }
        }

        'Stop' {
            Assert-PortalFixtureModeCombination -Mode 'Stop' -HasConfirmStart:$false `
                -HasConfirmStop:$ConfirmStop.IsPresent -HasCredentialFile:$false -HasRunManifestPath:$false | Out-Null
            $uri = Assert-LoopbackBaseUrl -Url $BaseUrl

            # Never stop an unknown listener: confirm identity first.
            try {
                $response = Invoke-WebRequest -Uri "$($uri.AbsoluteUri.TrimEnd('/'))/__portal-fixture/identity" `
                    -UseBasicParsing -TimeoutSec 10
            } catch {
                throw 'Stop refused: no fixture-host identity probe answered on the address.'
            }

            $decision = ConvertFrom-PortalFixtureIdentityProbe -Json $response.Content -ExpectedRevision $Revision
            if (-not $decision.isPortalFixtureHost) {
                throw 'Stop refused: the listener on the address is not the portal fixture host.'
            }

            $listener = Get-NetTCPConnection -LocalPort $uri.Port -State Listen -ErrorAction SilentlyContinue |
                Select-Object -First 1
            if ($null -eq $listener) {
                throw 'Stop refused: no listening process was found on the address.'
            }

            $process = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
            if ($null -eq $process) {
                throw 'Stop refused: the listening process could not be resolved.'
            }

            $processPath = [string]$process.Path
            if ($processPath -notmatch 'Ahlena\.PortalFixtureHost|dotnet') {
                throw 'Stop refused: the listening process is not the fixture host.'
            }

            Stop-Process -Id $process.Id -Force
            Write-SafeLine "stopped fixture host process $($process.Id)."
        }

        'Cleanup' {
            Assert-PortalFixtureModeCombination -Mode 'Cleanup' -HasConfirmStart:$false `
                -HasConfirmStop:$false -HasCredentialFile:$false -HasRunManifestPath:$true | Out-Null

            if (-not (Test-Path -LiteralPath $RunManifestPath)) {
                throw "Cleanup refused: the run manifest '$RunManifestPath' does not exist."
            }

            $manifest = ConvertFrom-Json -InputObject ([System.IO.File]::ReadAllText($RunManifestPath))
            $records = New-Object System.Collections.Generic.List[object]
            foreach ($group in @($manifest.groups)) {
                foreach ($record in @($group.records)) { $records.Add($record) }
            }

            $report = Get-PortalFixtureCleanupReport -Records $records.ToArray()
            Write-Output (ConvertTo-PortalFixtureJson ([ordered]@{
                mode    = 'cleanup'
                surface = 'report-only'
                records = $report
            }) 6)
            Write-SafeLine 'report only: nothing was deleted. No delete statement exists in this tool.'
        }
    }
} catch {
    # A thrown mode failure after the credential file exists must not echo an
    # exception message that could carry a secret or a credential-bearing path.
    Write-SafeLine "portal fixture pack failed: $(Get-RedactedText -Text $_.Exception.Message)"
    exit 1
}
