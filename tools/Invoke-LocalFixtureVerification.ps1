#requires -Version 5.1
<#
.SYNOPSIS
    Lightweight persistent-local fixture and HTTP verification for the L1
    follow-up slice (contract section "L1 - Persistent local fixtures and fast
    verification").

.DESCRIPTION
    Fail-closed local workflow:

      1. Read-only target verification of all nine module contexts, pending
         migrations, existing records, storage root and non-live integrations
         (`Sanad.API.dll --verify-local-fixture-target`). No write happens until
         this report passes.
      2. Idempotent Minimal fixture seed (`--seed-local-fixtures`), then a second
         read-only verification to show no duplicate identities or data loss.
      3. Start (or reuse) the Development API once against the verified local
         target and run a small HTTP check set on one already-existing endpoint
         (Admin wellness tips): success, paging clamp, status filter, unknown id,
         invalid status, unauthenticated, role-denied.
      4. Repeat the seed and the success check to demonstrate idempotence.

    Safety properties:
      - The base URL must be a loopback HTTP(S) listener; there is no remote
        fallback and no way to pass one.
      - Live SMS/SMTP/Push provider configuration is cleared for every process
        this script starts; migrations stay disabled
        (`Database:ApplyMigrationsOnStartup=false`).
      - Mutations are never retried automatically.
      - Tokens, credentials, connection strings and payloads are never printed;
        failed checks report only status, safe business code, field names,
        trace id and a redacted local log line.
      - Offline tests dot-source this file with -LibraryOnly and exercise only
        the pure functions; no API, database or HTTP call occurs there.

.PARAMETER BaseUrl
    Loopback HTTP(S) base URL of the API. Also used as ASPNETCORE_URLS when the
    script starts the API.

.PARAMETER ExpectedWorktreeRoot
    Optional. When supplied it must equal this script's repository root.

.PARAMETER ApiAssemblyPath
    Optional. Built API assembly. Defaults to the Release output.

.PARAMETER FixturePassword
    Synthetic fixture password for the Minimal identities. Never a real
    credential and never emitted in output.

.PARAMETER LibraryOnly
    Returns after defining the helper functions so offline tests can dot-source
    them without any runtime work.

.PARAMETER SkipSeed
    Skips both seed invocations (verification and HTTP checks still run).

.PARAMETER SkipApiStart
    Never starts the API; the script requires an already-reachable listener.
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:5235',

    [string]$ExpectedWorktreeRoot,

    [string]$ApiAssemblyPath,

    [string]$FixturePassword = 'Test-1234!',

    [switch]$LibraryOnly,

    [switch]$SkipSeed,

    [switch]$SkipApiStart
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Pure helpers (offline-testable; no I/O)
# ---------------------------------------------------------------------------

function Resolve-FullPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $Path }
    return [System.IO.Path]::GetFullPath($Path).TrimEnd([char[]]@('\', '/'))
}

function Test-LoopbackHost {
    <#
        True only for localhost or a literal loopback IP address. .NET expands
        IPv6 hosts to the full bracketed form, so parse instead of matching text.
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
    [CmdletBinding()]
    param([string]$Url)

    if ([string]::IsNullOrWhiteSpace($Url)) {
        throw 'BaseUrl is required and must target a loopback HTTP(S) listener. No remote fallback exists.'
    }

    try {
        $uri = [System.Uri]$Url
    }
    catch {
        throw 'BaseUrl must be a valid HTTP(S) URL.'
    }

    if (-not $uri.IsAbsoluteUri -or
        $uri.Scheme -notin @('http', 'https') -or
        -not (Test-LoopbackHost $uri.Host) -or
        $uri.UserInfo -or
        $uri.AbsolutePath -ne '/' -or
        $uri.Query -or
        $uri.Fragment) {
        throw 'BaseUrl must target a loopback HTTP(S) listener (localhost, 127.0.0.1 or ::1). No remote fallback exists.'
    }

    return $uri
}

function Get-LocalFixtureEnvironment {
    <#
        Environment overrides for every process this script starts.

        EnableSeed=$true is required by the read-only verification and the
        one-shot seed. EnableSeed=$false is used for the serving API so a normal
        start cannot re-seed; the same verified connection configuration applies
        either way.

        Every live provider input is cleared so the process cannot contact SMS
        Misr, SMTP or Firebase: Development then selects the Development no-op
        SMS/e-mail senders and the Development no-op push sender.
    #>
    [CmdletBinding()]
    param(
        [string]$Password = 'Test-1234!',
        [switch]$EnableSeed
    )

    $enabled = $false
    if ($EnableSeed) { $enabled = $true }

    return [ordered]@{
        'DOTNET_ENVIRONMENT'                     = 'Development'
        'ASPNETCORE_ENVIRONMENT'                 = 'Development'
        'App__TestUserSeed__Enabled'             = $enabled.ToString().ToLowerInvariant()
        'App__TestUserSeed__TargetMode'          = 'PersistentLocal'
        'App__TestUserSeed__Scope'               = 'Minimal'
        'App__TestUserSeed__Password'            = $Password
        'App__TestUserSeed__ElderlyPassword'     = ''
        'App__TestUserSeed__RunId'               = ''
        'Database__ApplyMigrationsOnStartup'     = 'false'
        'Logging__LogLevel__Default'             = 'Warning'
        'Logging__LogLevel__Microsoft.AspNetCore' = 'Warning'
        'Identity__Sms__SmsMisr__Username'       = ''
        'Identity__Sms__SmsMisr__Password'       = ''
        'Identity__Sms__SmsMisr__Sender'         = ''
        'Identity__Sms__SmsMisr__Environment'    = ''
        'Identity__Email__Smtp__Host'            = ''
        'Identity__Email__Smtp__FromAddress'     = ''
        'Identity__Email__Smtp__FromName'        = ''
        'Identity__Email__Smtp__Port'            = ''
        'Identity__Email__Smtp__Username'        = ''
        'Identity__Email__Smtp__Password'        = ''
        'Identity__Email__Smtp__UseSsl'          = ''
        'Push__Firebase__ServiceAccountJson'     = ''
    }
}

function Get-RedactedText {
    <#
        Removes tokens, credentials, API keys and connection strings from any
        text this script might surface. Over-redaction is preferred to leakage.
    #>
    [CmdletBinding()]
    param([string]$Text)

    if ([string]::IsNullOrEmpty($Text)) { return $Text }

    $redacted = $Text
    $redacted = [regex]::Replace($redacted, '(?i)("(?:access|refresh|id)?token"\s*:\s*")[^"]*(")', '$1<redacted>$2')
    $redacted = [regex]::Replace($redacted, '(?i)\b(bearer)\s+[A-Za-z0-9\-._~+/]{8,}=*', '$1 <redacted>')
    $redacted = [regex]::Replace($redacted, '\beyJ[A-Za-z0-9_\-]{5,}\.[A-Za-z0-9_\-]{5,}\.[A-Za-z0-9_\-]{5,}', '<redacted-token>')
    $redacted = [regex]::Replace($redacted, '(?i)\b(password|pwd|username|userid|user\s+id)\s*=\s*[^;"''\s]+', '$1=<redacted>')
    $redacted = [regex]::Replace($redacted, '(?i)\b(?:host|server)\s*=\s*[^;"''\s]+\s*(?:;[^;"''\s]+\s*){2,}', '<redacted-connection-string>')
    $redacted = [regex]::Replace($redacted, '(?i)\b(?:egy_(?:sk|pk|csk)_(?:live|test)|(?:sk|pk)_(?:live|test)|egy)_[A-Za-z0-9_-]{8,}', '<redacted-key>')
    $redacted = [regex]::Replace($redacted, '(?i)("(?:password|secret|secretkey|signingkey|hmacsecret|serviceaccountjson)"\s*:\s*")[^"]*(")', '$1<redacted>$2')
    return $redacted
}

function Get-TrimmedRedactedLine {
    [CmdletBinding()]
    param([string]$Text, [int]$MaxLength = 300)

    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    $single = ($Text -replace '[\r\n\t]+', ' ').Trim()
    $redacted = Get-RedactedText $single
    if ($redacted.Length -gt $MaxLength) { return $redacted.Substring(0, $MaxLength) + '...' }
    return $redacted
}

function ConvertFrom-SafeJson {
    [CmdletBinding()]
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    try { return ($Text | ConvertFrom-Json) } catch { return $null }
}

function Get-SafeErrorSummary {
    <#
        Business code, title and field names only - never field values or the
        raw body, which can echo sensitive input.
    #>
    [CmdletBinding()]
    param($Parsed)

    if ($null -eq $Parsed) { return 'no-json-body' }

    $parts = @()
    if ($Parsed.PSObject.Properties.Name -contains 'code' -and $Parsed.code) { $parts += "code=$($Parsed.code)" }
    if ($Parsed.PSObject.Properties.Name -contains 'title' -and $Parsed.title) { $parts += "title=$($Parsed.title)" }
    if ($Parsed.PSObject.Properties.Name -contains 'errors' -and $Parsed.errors) {
        $fieldNames = @($Parsed.errors.PSObject.Properties.Name)
        if ($fieldNames.Count -gt 0) { $parts += ('fields=' + ($fieldNames -join ',')) }
    }

    if ($parts.Count -eq 0) { return 'json-without-safe-code' }
    return ($parts -join ' ')
}

function Get-CheckOutcome {
    <#
        Pure evaluation of one planned check. Offline tests pass synthetic
        status/body values here; no HTTP call is made.
    #>
    [CmdletBinding()]
    param(
        [hashtable]$Check,
        [int]$Status,
        [string]$BodyText,
        [string]$TraceId,
        [string]$Location
    )

    $parsed = ConvertFrom-SafeJson $BodyText
    $problems = @()
    $actual = "status=$Status"

    if ($Status -ne $Check.ExpectedStatus) {
        $problems += "expected HTTP $($Check.ExpectedStatus), got $Status ($(Get-SafeErrorSummary $parsed))"
        $actual = "status=$Status; $(Get-SafeErrorSummary $parsed)"
    }

    if ($Check.ContainsKey('Assert') -and $null -ne $Check.Assert) {
        $assertion = & $Check.Assert $Status $parsed
        if ($assertion.Actual) { $actual = $assertion.Actual }
        foreach ($problem in @($assertion.Problems)) {
            if (-not [string]::IsNullOrWhiteSpace($problem)) { $problems += $problem }
        }
    }

    $traceCorrelation = 'no-trace-id'
    if ($TraceId -and $Location) { $traceCorrelation = 'matched-local-log' }
    elseif ($TraceId) { $traceCorrelation = 'unavailable' }

    return [ordered]@{
        name             = $Check.Name
        method           = $Check.Method
        path             = $Check.Path
        actor            = $Check.Actor
        expected         = $Check.Expected
        actual           = $actual
        passed           = ($problems.Count -eq 0)
        problems         = @($problems)
        traceId          = $TraceId
        traceCorrelation = $traceCorrelation
        localLocation    = $Location
    }
}

function Get-TraceLocation {
    [CmdletBinding()]
    param([string]$TraceId, [string[]]$LogLines)

    if ([string]::IsNullOrWhiteSpace($TraceId)) { return $null }
    foreach ($line in @($LogLines)) {
        if ($line -and $line.Contains($TraceId)) { return (Get-TrimmedRedactedLine $line) }
    }
    return $null
}

function Get-CapturedLogLines {
    <#
        Reads captured API log files that the API process still holds open for
        writing. File.ReadAllLines fails with a sharing violation in that state
        (the redirected stdout handle does not share read access the way it
        expects), so open explicitly with FileShare.ReadWrite. A log file that
        cannot be read yields no lines instead of failing the run: trace
        correlation is a diagnostic, and the report states when it is
        unavailable.
    #>
    [CmdletBinding()]
    param([string[]]$Path)

    $lines = @()
    foreach ($item in @($Path)) {
        if ([string]::IsNullOrWhiteSpace($item) -or -not (Test-Path -LiteralPath $item -PathType Leaf)) { continue }

        $stream = $null
        $reader = $null
        try {
            $stream = [System.IO.FileStream]::new(
                $item,
                [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::Read,
                [System.IO.FileShare]::ReadWrite)
            $reader = [System.IO.StreamReader]::new($stream)
            while (-not $reader.EndOfStream) {
                $line = $reader.ReadLine()
                if ($null -ne $line) { $lines += $line }
            }
        }
        catch {
            # Unreadable captured log: report no lines, never abort the run.
        }
        finally {
            if ($reader) { $reader.Dispose() }
            elseif ($stream) { $stream.Dispose() }
        }
    }

    return @($lines)
}

function Get-LocalFixtureCheckPlan {
    <#
        One already-existing endpoint (Admin wellness tips, published by the
        repository) covering success, paging, filter, unknown id, invalid input,
        authentication and role cases. No endpoint feature is added or changed.
    #>
    [CmdletBinding()]
    param(
        [string]$ContentAdminToken,
        [string]$FamilyViewerToken,
        [string]$UnknownGuid,
        [string]$AdminRoute = '/api/v1/admin/wellness-tips'
    )

    $unknownDetail = "${AdminRoute}/${UnknownGuid}"

    $successAssert = {
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
        }
        else {
            $items = @($body.items)
            $actual = "status=$status items=$($items.Count) page=$($body.page) pageSize=$($body.pageSize) totalCount=$($body.totalCount)"
            if ($body.page -ne 1) { $problems += "page must be 1, got $($body.page)" }
            if ($body.pageSize -ne 5) { $problems += "pageSize must be 5, got $($body.pageSize)" }
            if ($null -eq $body.totalCount) { $problems += 'totalCount is missing' }
            if ($body.PSObject.Properties.Name -notcontains 'items') { $problems += 'items array is missing' }
        }
        return @{ Actual = $actual; Problems = $problems }
    }

    $clampAssert = {
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
        }
        else {
            $actual = "status=$status page=$($body.page) pageSize=$($body.pageSize)"
            if ($body.page -ne 1) { $problems += "page=0 must clamp to 1, got $($body.page)" }
            if ($body.pageSize -ne 100) { $problems += "pageSize=500 must clamp to 100, got $($body.pageSize)" }
        }
        return @{ Actual = $actual; Problems = $problems }
    }

    $filterAssert = {
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
        }
        else {
            $items = @($body.items)
            $statuses = @($items | ForEach-Object { $_.status } | Sort-Object -Unique)
            $actual = "status=$status items=$($items.Count) itemStatuses=$($statuses -join ',')"
            foreach ($item in $items) {
                if ($item.status -ne 1) { $problems += "status filter returned item status $($item.status)" }
            }
        }
        return @{ Actual = $actual; Problems = $problems }
    }

    $unknownAssert = {
        param($status, $body)
        $actual = "status=$status $(Get-SafeErrorSummary $body)"
        $problems = @()
        if ($body.code -ne 'Cms.WellnessTip.NotFound') { $problems += "expected code Cms.WellnessTip.NotFound, got '$($body.code)'" }
        return @{ Actual = $actual; Problems = $problems }
    }

    $invalidAssert = {
        param($status, $body)
        $actual = "status=$status $(Get-SafeErrorSummary $body)"
        $problems = @()
        $hasFieldErrors = $false
        if ($null -ne $body -and $body.PSObject.Properties.Name -contains 'errors' -and $body.errors) {
            $hasFieldErrors = (@($body.errors.PSObject.Properties.Name).Count -gt 0)
        }
        if (-not $hasFieldErrors) { $problems += 'expected field-level validation errors for the invalid enum value' }
        return @{ Actual = $actual; Problems = $problems }
    }

    $plainStatusAssert = {
        param($status, $body)
        return @{ Actual = "status=$status"; Problems = @() }
    }

    return @(
        [ordered]@{
            Name           = 'paging.page-size-clamp'
            Method         = 'GET'
            Path           = "${AdminRoute}?page=0&pageSize=500"
            Actor          = 'content-admin'
            Token          = $ContentAdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 with page clamped to 1 and pageSize clamped to 100 (existing bounds)'
            Assert         = $clampAssert
        }
        [ordered]@{
            Name           = 'filter.status-draft'
            Method         = 'GET'
            Path           = "${AdminRoute}?status=1&page=1&pageSize=5"
            Actor          = 'content-admin'
            Token          = $ContentAdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 and every returned item has status=1 (Draft)'
            Assert         = $filterAssert
        }
        [ordered]@{
            Name           = 'unknown.guid-detail'
            Method         = 'GET'
            Path           = $unknownDetail
            Actor          = 'content-admin'
            Token          = $ContentAdminToken
            ExpectedStatus = 404
            Expected       = 'HTTP 404 with business code Cms.WellnessTip.NotFound'
            Assert         = $unknownAssert
        }
        [ordered]@{
            Name           = 'invalid.enum-query'
            Method         = 'GET'
            Path           = "${AdminRoute}?status=999"
            Actor          = 'content-admin'
            Token          = $ContentAdminToken
            ExpectedStatus = 400
            Expected       = 'HTTP 400 with field-level validation errors for the invalid enum value'
            Assert         = $invalidAssert
        }
        [ordered]@{
            Name           = 'auth.unauthenticated'
            Method         = 'GET'
            Path           = $AdminRoute
            Actor          = 'anonymous'
            Token          = $null
            ExpectedStatus = 401
            Expected       = 'HTTP 401 because no bearer token is supplied'
            Assert         = $plainStatusAssert
        }
        [ordered]@{
            Name           = 'role.family-denied'
            Method         = 'GET'
            Path           = $AdminRoute
            Actor          = 'family-viewer'
            Token          = $FamilyViewerToken
            ExpectedStatus = 403
            Expected       = 'HTTP 403 because the Family account lacks the CmsContent policy'
            Assert         = $plainStatusAssert
        }
        [ordered]@{
            Name           = 'success.content-admin-list'
            Method         = 'GET'
            Path           = "${AdminRoute}?page=1&pageSize=5"
            Actor          = 'content-admin'
            Token          = $ContentAdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 with items, page=1, pageSize=5 and totalCount'
            Assert         = $successAssert
        }
    )
}

# ---------------------------------------------------------------------------
# Runtime helpers
# ---------------------------------------------------------------------------

function Get-RepositoryRoot {
    return (Resolve-FullPath (Join-Path $PSScriptRoot '..'))
}

function Get-PinnedHead([string]$RepoRoot) {
    $gitSafeRoot = $RepoRoot.Replace('\', '/')
    $head = (& git '-c' "safe.directory=$gitSafeRoot" '-C' $gitSafeRoot 'rev-parse' 'HEAD' 2>$null)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve the pinned git HEAD.' }
    $head = ([string]$head).Trim()
    if ($head -notmatch '^[0-9a-fA-F]{40}$') { throw 'Unable to resolve the pinned git HEAD.' }
    return $head
}

function Invoke-DotnetWithEnvironment {
    <#
        Runs `dotnet <assembly> <arguments>` with the supplied process-scope
        overrides, restoring the caller environment afterwards.
    #>
    [CmdletBinding()]
    param(
        [string]$RepoRoot,
        [string]$AssemblyPath,
        [string[]]$Arguments,
        [hashtable]$Environment,
        [string]$WorkingDirectory
    )

    $previous = @{}
    foreach ($name in $Environment.Keys) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $Environment[$name], 'Process')
    }

    try {
        Push-Location -LiteralPath $WorkingDirectory
        try {
            $output = & dotnet $AssemblyPath @Arguments 2>&1
            $exitCode = $LASTEXITCODE
        }
        finally {
            Pop-Location
        }
    }
    finally {
        foreach ($name in $Environment.Keys) {
            [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
        }
    }

    return @{
        ExitCode = $exitCode
        Lines    = @($output | ForEach-Object { [string]$_ })
    }
}

function Invoke-TargetVerification {
    [CmdletBinding()]
    param(
        [string]$RepoRoot,
        [string]$AssemblyPath,
        [hashtable]$Environment,
        [string]$ApiDirectory
    )

    $run = Invoke-DotnetWithEnvironment -RepoRoot $RepoRoot -AssemblyPath $AssemblyPath -Arguments @('--verify-local-fixture-target') -Environment $Environment -WorkingDirectory $ApiDirectory
    $jsonLine = @($run.Lines | Where-Object { $_ -and $_.TrimStart().StartsWith('{') }) | Select-Object -Last 1

    if ($run.ExitCode -ne 0 -and -not $jsonLine) {
        return [ordered]@{
            passed       = $false
            exitCode     = $run.ExitCode
            report       = $null
            failedChecks = @('verification-command-failed')
            message      = (Get-TrimmedRedactedLine (@($run.Lines) -join ' '))
        }
    }

    $report = ConvertFrom-SafeJson $jsonLine
    if ($null -eq $report) {
        return [ordered]@{
            passed       = $false
            exitCode     = $run.ExitCode
            report       = $null
            failedChecks = @('verification-report-unreadable')
            message      = 'The read-only verification command produced no parseable report.'
        }
    }

    return [ordered]@{
        passed       = [bool]$report.passed
        exitCode     = $run.ExitCode
        report       = $report
        failedChecks = @($report.failedChecks)
        message      = $null
    }
}

function Invoke-FixtureSeed {
    [CmdletBinding()]
    param(
        [string]$RepoRoot,
        [string]$AssemblyPath,
        [hashtable]$Environment,
        [string]$ApiDirectory
    )

    $run = Invoke-DotnetWithEnvironment -RepoRoot $RepoRoot -AssemblyPath $AssemblyPath -Arguments @('--seed-local-fixtures') -Environment $Environment -WorkingDirectory $ApiDirectory
    $jsonLine = @($run.Lines | Where-Object { $_ -and $_.TrimStart().StartsWith('{') }) | Select-Object -Last 1
    $result = ConvertFrom-SafeJson $jsonLine

    return [ordered]@{
        exitCode = $run.ExitCode
        passed   = ($run.ExitCode -eq 0 -and $null -ne $result -and [bool]$result.passed)
        message  = if ($null -ne $result) { $null } else { (Get-TrimmedRedactedLine (@($run.Lines) -join ' ')) }
    }
}

function Enable-HttpClientType {
    <#
        System.Net.Http is not loaded in a default Windows PowerShell session,
        so `New-Object System.Net.Http.HttpClient` throws "Cannot find type
        [System.Net.Http.HttpClient]". Every probe that builds its own client
        therefore failed silently and reported the base URL as unreachable,
        which made the API readiness wait always time out without a single
        request being sent. Load the assembly before any client is created.
    #>
    [CmdletBinding()]
    param()

    if (-not ([System.Management.Automation.PSTypeName]'System.Net.Http.HttpClient').Type) {
        Add-Type -AssemblyName System.Net.Http
    }
}

function Test-BaseUrlReachable {
    [CmdletBinding()]
    param([string]$Url, [int]$TimeoutSeconds = 3)

    Enable-HttpClientType

    try {
        $client = New-Object System.Net.Http.HttpClient
        $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)
        $response = $client.GetAsync("$Url/api/v1/deployment/info").GetAwaiter().GetResult()
        $reachable = $true
        $response.Dispose()
        $client.Dispose()
        return $reachable
    }
    catch {
        return $false
    }
}

function Start-FixtureApiProcess {
    [CmdletBinding()]
    param(
        [string]$AssemblyPath,
        [string]$WorkingDirectory,
        [string]$Url,
        [hashtable]$Environment,
        [string]$OutputLogPath,
        [string]$ErrorLogPath
    )

    $launchEnvironment = @{}
    foreach ($name in $Environment.Keys) { $launchEnvironment[$name] = $Environment[$name] }
    $launchEnvironment['ASPNETCORE_URLS'] = $Url

    $previous = @{}
    foreach ($name in $launchEnvironment.Keys) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $launchEnvironment[$name], 'Process')
    }

    try {
        $process = Start-Process -FilePath 'dotnet' -ArgumentList @($AssemblyPath) `
            -WorkingDirectory $WorkingDirectory `
            -RedirectStandardOutput $OutputLogPath -RedirectStandardError $ErrorLogPath `
            -PassThru -NoNewWindow
    }
    finally {
        foreach ($name in $launchEnvironment.Keys) {
            [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
        }
    }

    return $process
}

function Wait-ForEndpoint {
    [CmdletBinding()]
    param([string]$Url, [int]$TimeoutSeconds = 90, [System.Diagnostics.Process]$Process)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($Process -and $Process.HasExited) { return $false }
        if (Test-BaseUrlReachable -Url $Url) { return $true }
        Start-Sleep -Seconds 1
    }

    return $false
}

function New-SanadHttpClient {
    Enable-HttpClientType

    $client = New-Object System.Net.Http.HttpClient
    $client.Timeout = [TimeSpan]::FromSeconds(30)
    return $client
}

function Invoke-SanadHttpRequest {
    [CmdletBinding()]
    param(
        [System.Net.Http.HttpClient]$Client,
        [string]$Method,
        [string]$Url,
        [string]$Body,
        [string]$Token
    )

    $request = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::new($Method)), $Url
    try {
        if ($Token) {
            $request.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue ('Bearer', $Token)
        }
        if ($Body) {
            $request.Content = New-Object System.Net.Http.StringContent ($Body, [System.Text.Encoding]::UTF8, 'application/json')
        }

        $response = $Client.SendAsync($request, [System.Net.Http.HttpCompletionOption]::ResponseContentRead).GetAwaiter().GetResult()
        try {
            return @{
                Status = [int]$response.StatusCode
                Body   = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            }
        }
        finally {
            $response.Dispose()
        }
    }
    finally {
        $request.Dispose()
    }
}

function Test-NormalAccessType {
    <#
        AuthAccessType.Normal = 1 and AuthAccessType.RestrictedVerification = 2
        (src/Modules/Identity/Application/.../Tokens/AuthAccessType.cs). The enum
        starts at 1, not 0, so the preflight must not compare against 0: that
        rejects a correct Normal login and no authenticated check ever runs.
        RestrictedVerification means the identity still owes an email or phone
        verification and cannot pass the authenticated checks either.
    #>
    [CmdletBinding()]
    param([object]$AccessType)

    if ($null -eq $AccessType) { return $false }

    $value = 0
    try { $value = [int]$AccessType }
    catch { return $false }

    return ($value -eq 1)
}

function Get-LoginPayload {
    <#
        Login body for one fixture identity.

        DevicePlatform must be a defined, non-Unknown value: Unknown (0) is the
        enum default and LoginCommandValidator rejects it with "Device platform
        is invalid", so a probe sending the default fails the preflight with
        HTTP 400 and no authenticated check ever runs. Web (3) is what this
        HTTP-only verification run actually is.

        Name lengths stay within DeviceSession.MaximumDeviceNameLength (200) and
        MaximumAppVersionLength (50).
    #>
    [CmdletBinding()]
    param(
        [string]$Identifier,
        [string]$Password,
        [string]$DeviceName,
        [string]$AppVersion = 'l1-local-fixture-verification'
    )

    return (@{
        identifier     = $Identifier
        password       = $Password
        deviceName     = $DeviceName
        devicePlatform = 3
        appVersion     = $AppVersion
    } | ConvertTo-Json -Compress)
}

function Get-FixtureToken {
    <#
        Login preflight for one fixture identity. The token is returned for
        in-memory use only and is never written to output.
    #>
    [CmdletBinding()]
    param(
        [System.Net.Http.HttpClient]$Client,
        [string]$Url,
        [string]$Email,
        [string]$Password,
        [string]$DeviceName
    )

    $payload = Get-LoginPayload -Identifier $Email -Password $Password -DeviceName $DeviceName

    $response = Invoke-SanadHttpRequest -Client $Client -Method 'POST' -Url "$Url/api/v1/auth/login" -Body $payload -Token $null
    $parsed = ConvertFrom-SafeJson $response.Body

    if ($response.Status -ne 200) {
        throw "Login preflight failed for the fixture identity ($(Get-TrimmedRedactedLine $Email)): status=$($response.Status) $(Get-SafeErrorSummary $parsed)"
    }
    if ($null -eq $parsed -or [string]::IsNullOrWhiteSpace($parsed.accessToken)) {
        throw 'Login preflight returned no access token.'
    }
    if (-not (Test-NormalAccessType -AccessType $parsed.accessType)) {
        throw "Login preflight expected Normal access (1); got accessType=$($parsed.accessType)."
    }

    return [string]$parsed.accessToken
}

function Invoke-PlannedChecks {
    [CmdletBinding()]
    param(
        [System.Net.Http.HttpClient]$Client,
        [string]$Url,
        [hashtable[]]$Plan,
        [string[]]$LogPath
    )

    $results = @()
    foreach ($check in $Plan) {
        $response = Invoke-SanadHttpRequest -Client $Client -Method $check.Method -Url "$Url$($check.Path)" -Body $null -Token $check.Token
        $parsed = ConvertFrom-SafeJson $response.Body
        $traceId = $null
        if ($null -ne $parsed -and $parsed.PSObject.Properties.Name -contains 'traceId') { $traceId = [string]$parsed.traceId }

        # The response has already been produced, so the matching server log
        # line - when one exists - is on disk now. Read the captured log per
        # failure rather than up front, and always with a share mode that
        # tolerates the API still writing to it.
        $location = $null
        if ($traceId) {
            $location = Get-TraceLocation -TraceId $traceId -LogLines (Get-CapturedLogLines -Path $LogPath)
        }

        $results += Get-CheckOutcome -Check $check -Status $response.Status -BodyText $response.Body -TraceId $traceId -Location $location
    }

    return $results
}

# ---------------------------------------------------------------------------

if ($LibraryOnly) { return }

# ---------------------------------------------------------------------------
# Main flow
# ---------------------------------------------------------------------------

$repoRoot = Get-RepositoryRoot

if ($ExpectedWorktreeRoot) {
    $expected = Resolve-FullPath $ExpectedWorktreeRoot
    if (-not $expected.Equals($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'ExpectedWorktreeRoot does not match this script''s repository root.'
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'AGENTS.md') -PathType Leaf)) {
    throw 'Run this script from the Sanad API repository checkout.'
}

$head = Get-PinnedHead $repoRoot
$uri = Assert-LoopbackBaseUrl $BaseUrl
$baseUrl = "$($uri.Scheme)://$($uri.Authority)"

$apiDirectory = Join-Path $repoRoot 'src/API/Sanad.API'
if (-not $ApiAssemblyPath) {
    $ApiAssemblyPath = Join-Path $apiDirectory 'bin/Release/net10.0/Sanad.API.dll'
}
$assemblyPath = Resolve-FullPath $ApiAssemblyPath
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw 'The built API assembly was not found. Build Release first (dotnet build Sanad.slnx -c Release).'
}

$seedEnvironment = Get-LocalFixtureEnvironment -Password $FixturePassword -EnableSeed
$serveEnvironment = Get-LocalFixtureEnvironment -Password $FixturePassword

$failureNotes = @()
$apiProcess = $null
$apiReused = Test-BaseUrlReachable -Url $baseUrl

# 1. Read-only target verification before any write.
$preflight = Invoke-TargetVerification -RepoRoot $repoRoot -AssemblyPath $assemblyPath -Environment $seedEnvironment -ApiDirectory $apiDirectory
if (-not $preflight.passed) {
    $result = [ordered]@{
        workflow          = 'l1-local-fixture-verification'
        head              = $head
        baseUrl           = $baseUrl
        phase             = 'target-verification'
        passed            = $false
        targetVerification = $preflight
        checks            = @()
        results           = [ordered]@{ total = 0; passed = 0; failed = 0 }
        notes             = @('The read-only target verification failed; no seed, no API start and no database write occurred.')
    }
    $result | ConvertTo-Json -Depth 8
    exit 1
}

# 2. Idempotent Minimal seed, then read-back verification.
$seedResults = @()
$targetReports = @($preflight)
if (-not $SkipSeed) {
    $firstSeed = Invoke-FixtureSeed -RepoRoot $repoRoot -AssemblyPath $assemblyPath -Environment $seedEnvironment -ApiDirectory $apiDirectory
    $seedResults += $firstSeed
    if (-not $firstSeed.passed) {
        $failureNotes += 'The one-shot Minimal fixture seed failed; HTTP checks were skipped.'
    }

    $afterSeed = Invoke-TargetVerification -RepoRoot $repoRoot -AssemblyPath $assemblyPath -Environment $seedEnvironment -ApiDirectory $apiDirectory
    $targetReports += $afterSeed
    if (-not $afterSeed.passed) { $failureNotes += 'Post-seed target verification failed.' }
    elseif ($afterSeed.report.fixtureIdentityPresentCount -ne 2) {
        $failureNotes += "Post-seed verification reports $($afterSeed.report.fixtureIdentityPresentCount) of 2 Minimal fixture identities."
    }
}

$canRunChecks = ($failureNotes.Count -eq 0)

# 3. Start or reuse the API once.
if ($canRunChecks -and -not $apiReused -and -not $SkipApiStart) {
    $logDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sanad-l1-verify-' + [Guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($logDirectory) | Out-Null
    $outputLog = Join-Path $logDirectory 'api.stdout.log'
    $errorLog = Join-Path $logDirectory 'api.stderr.log'

    $apiProcess = Start-FixtureApiProcess -AssemblyPath $assemblyPath -WorkingDirectory $apiDirectory -Url $baseUrl -Environment $serveEnvironment -OutputLogPath $outputLog -ErrorLogPath $errorLog

    $ready = Wait-ForEndpoint -Url $baseUrl -TimeoutSeconds 90 -Process $apiProcess
    if (-not $ready) {
        if ($apiProcess -and -not $apiProcess.HasExited) {
            Stop-Process -Id $apiProcess.Id -Force
            Wait-Process -Id $apiProcess.Id -Timeout 15 -ErrorAction SilentlyContinue
        }
        $apiProcess = $null

        # Bounded diagnostic once the process has stopped (so the captured log is
        # complete): one redacted line explains most startup failures without
        # dumping a log. The reader tolerates a writer that still holds the file.
        $logLines = @(Get-CapturedLogLines -Path @($outputLog, $errorLog))
        $lastLogLine = $logLines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1
        if ($lastLogLine) {
            $failureNotes += ('Last captured API log line: ' + (Get-TrimmedRedactedLine $lastLogLine))
        }

        $failureNotes += 'The Development API did not become reachable within 90 seconds.'
        $canRunChecks = $false
    }

    $script:apiLogPaths = @($outputLog, $errorLog)
}

$checkResults = @()
$tokens = @{}
$client = $null

try {
    if ($canRunChecks) {
        try {
            $client = New-SanadHttpClient

            # 4. Login preflight for both Minimal fixture identities.
            $tokens['content-admin'] = Get-FixtureToken -Client $client -Url $baseUrl `
                -Email 'content.admin@test.sanad.local' -Password $FixturePassword -DeviceName 'l1-verify-content-admin'
            $tokens['family-viewer'] = Get-FixtureToken -Client $client -Url $baseUrl `
                -Email 'family.viewer@test.sanad.local' -Password $FixturePassword -DeviceName 'l1-verify-family-viewer'

            # 5. Small existing-endpoint check set. Captured logs are read per
            #    check inside the planner, after the response exists.
            $plan = Get-LocalFixtureCheckPlan -ContentAdminToken $tokens['content-admin'] -FamilyViewerToken $tokens['family-viewer'] -UnknownGuid ([Guid]::NewGuid().ToString())

            $checkResults += Invoke-PlannedChecks -Client $client -Url $baseUrl -Plan $plan -LogPath $script:apiLogPaths

            # 6. Repeat the seed and the success check to prove idempotence.
            if (-not $SkipSeed) {
                $secondSeed = Invoke-FixtureSeed -RepoRoot $repoRoot -AssemblyPath $assemblyPath -Environment $seedEnvironment -ApiDirectory $apiDirectory
                $seedResults += $secondSeed
                if (-not $secondSeed.passed) { $failureNotes += 'The repeat Minimal fixture seed failed.' }

                $repeatVerify = Invoke-TargetVerification -RepoRoot $repoRoot -AssemblyPath $assemblyPath -Environment $seedEnvironment -ApiDirectory $apiDirectory
                $targetReports += $repeatVerify
                if (-not $repeatVerify.passed) { $failureNotes += 'Repeat target verification failed.' }
                elseif ($repeatVerify.report.fixtureIdentityPresentCount -ne 2) {
                    $failureNotes += "Repeat verification reports $($repeatVerify.report.fixtureIdentityPresentCount) of 2 Minimal fixture identities."
                }

                $repeatToken = Get-FixtureToken -Client $client -Url $baseUrl `
                    -Email 'content.admin@test.sanad.local' -Password $FixturePassword -DeviceName 'l1-verify-content-admin-repeat'
                $repeatPlan = @(Get-LocalFixtureCheckPlan -ContentAdminToken $repeatToken -FamilyViewerToken $tokens['family-viewer'] -UnknownGuid ([Guid]::NewGuid().ToString()) |
                    Where-Object { $_.Name -eq 'success.content-admin-list' })
                $checkResults += Invoke-PlannedChecks -Client $client -Url $baseUrl -Plan $repeatPlan -LogPath $script:apiLogPaths
            }
        }
        catch {
            $failureNotes += ('HTTP verification could not complete: ' + (Get-TrimmedRedactedLine $_.Exception.Message))
        }
        finally {
            if ($client) { $client.Dispose() }
        }
    }
}
finally {
    # 7. Stop the API this script started. Runs even when verification failed,
    #    so a failed run can never leave an orphaned listener behind.
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force
        Wait-Process -Id $apiProcess.Id -Timeout 15 -ErrorAction SilentlyContinue
    }
}

$passedCount = @($checkResults | Where-Object { $_.passed }).Count
$failedCount = @($checkResults | Where-Object { -not $_.passed }).Count
$seedEvidence = @($seedResults | ForEach-Object { [ordered]@{ exitCode = $_.exitCode; passed = $_.passed } })
$identityCounts = @($targetReports | ForEach-Object {
    if ($_.report) { [ordered]@{ identityUserCount = $_.report.identityUserCount; fixtureIdentityPresentCount = $_.report.fixtureIdentityPresentCount } } else { $null }
})

$overallPassed = ($failureNotes.Count -eq 0) -and ($failedCount -eq 0) -and ($seedResults | Where-Object { -not $_.passed }).Count -eq 0

$output = [ordered]@{
    workflow            = 'l1-local-fixture-verification'
    head                = $head
    baseUrl             = $baseUrl
    apiStarted          = ($null -ne $apiProcess) -or ($canRunChecks -and $apiReused)
    apiReused           = $apiReused
    seedSkipped         = [bool]$SkipSeed
    passed              = $overallPassed
    seeds               = $seedEvidence
    targetVerifications = @($targetReports | ForEach-Object {
        [ordered]@{
            passed       = $_.passed
            failedChecks = $_.failedChecks
            report       = $_.report
        }
    })
    identityCounts      = $identityCounts
    checks              = $checkResults
    results             = [ordered]@{ total = $checkResults.Count; passed = $passedCount; failed = $failedCount }
    notes                = $failureNotes
    limitation          = @(
        'Startup migrations stay disabled for every process this script starts; migration state is only read.',
        'A local API start also starts the repository''s existing outbox/notification background services. SMS, SMTP and Firebase inputs are cleared so no provider can be contacted, and push runs the Development no-op sender.',
        'Trace correlation is reported as unavailable when no captured local log line carries the trace id of a failed check.'
    )
}

$output | ConvertTo-Json -Depth 8

if (-not $overallPassed -or $failedCount -gt 0 -or $failureNotes.Count -gt 0) {
    exit 1
}
