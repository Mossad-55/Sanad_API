[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,
    [string]$ExpectedRevision,
    [string]$StartupLogPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Deployment manifest was not found: $ManifestPath"
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.application -ne 'Sanad.API') {
    throw "Unexpected deployment application in manifest: $($manifest.application)"
}

if (-not [string]::IsNullOrWhiteSpace($ExpectedRevision) -and
    $manifest.revision -ne $ExpectedRevision) {
    throw "Manifest revision '$($manifest.revision)' does not match expected revision '$ExpectedRevision'."
}

$requiredMigrations = @{}
foreach ($context in $manifest.requiredMigrations.PSObject.Properties) {
    $requiredMigrations[$context.Name] = [string]$context.Value
}
if ($requiredMigrations.Count -ne 9) {
    throw "Manifest must list all 9 module contexts; found $($requiredMigrations.Count)."
}

Add-Type -AssemblyName System.Net.Http
$httpClient = [System.Net.Http.HttpClient]::new()
$httpClient.Timeout = [TimeSpan]::FromSeconds(30)
try {
    $base = $BaseUrl.TrimEnd('/')

    # Safe remote revision/health evidence (no DB details leave the host).
    $infoUri = "$base/api/v1/deployment/info"
    $infoResponse = $httpClient.GetAsync($infoUri).GetAwaiter().GetResult()
    if ([int]$infoResponse.StatusCode -ne 200) {
        throw "Deployment info request returned HTTP $([int]$infoResponse.StatusCode), expected 200."
    }
    $info = $infoResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json

    if ([string]::IsNullOrWhiteSpace($info.manifestRevision)) {
        throw "Remote deployment info reports no manifest revision; the host may be running an unpublished build."
    }
    if ($info.manifestRevision -ne $manifest.revision) {
        throw "Remote revision '$($info.manifestRevision)' does not match manifest revision '$($manifest.revision)'."
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedRevision) -and
        $info.manifestRevision -ne $ExpectedRevision) {
        throw "Remote revision '$($info.manifestRevision)' does not match expected revision '$ExpectedRevision'."
    }

    if (-not $info.databaseReachable) {
        throw "Remote host reports its databases as unreachable; migration evidence is unavailable."
    }
    $applied = @{}
    foreach ($context in $info.appliedMigrations.PSObject.Properties) {
        $applied[$context.Name] = [string]$context.Value
    }
    $mismatches = @()
    foreach ($name in $requiredMigrations.Keys) {
        if (-not $applied.ContainsKey($name) -or [string]::IsNullOrWhiteSpace($applied[$name])) {
            $mismatches += "$name (no applied migration reported)"
        }
        elseif ($applied[$name] -ne $requiredMigrations[$name]) {
            $mismatches += "$name (remote '$($applied[$name])' vs required '$($requiredMigrations[$name])')"
        }
    }
    if ($mismatches.Count -gt 0) {
        throw "Remote migration evidence does not cover all 9 contexts:`n  - $($mismatches -join "`n  - ")"
    }

    $smokeUri = "$base/api/v1/lookups/services"
    $smokeResponse = $httpClient.GetAsync($smokeUri).GetAwaiter().GetResult()
    if ([int]$smokeResponse.StatusCode -ne 200) {
        throw "API smoke request returned HTTP $([int]$smokeResponse.StatusCode), expected 200."
    }
}
finally {
    $httpClient.Dispose()
}

if (-not [string]::IsNullOrWhiteSpace($StartupLogPath)) {
    if (-not (Test-Path -LiteralPath $StartupLogPath -PathType Leaf)) {
        throw "Startup log was not found: $StartupLogPath"
    }
    $startupLog = Get-Content -LiteralPath $StartupLogPath -Raw
    $completedContexts = @()
    foreach ($line in $startupLog -split "`r?`n") {
        if ($line -match 'Database migration check completed for (\w+);') {
            $completedContexts += $Matches[1]
        }
    }
    $completedContexts = @($completedContexts | Sort-Object -Unique)
    if ($completedContexts.Count -lt 9) {
        throw "Startup log proves migration checks for $($completedContexts.Count) context(s) only ($($completedContexts -join ', ')); 9 contexts are required."
    }
    Write-Output "Startup log migration contexts: $($completedContexts -join ', ')"
}

Write-Output "Manifest revision: $($manifest.revision)"
Write-Output "Remote revision: $($info.manifestRevision) (match)"
Write-Output "Remote environment: $($info.environment)"
Write-Output 'Required/applied migrations (9/9 match):'
foreach ($name in ($requiredMigrations.Keys | Sort-Object)) {
    Write-Output ("  {0}: {1}" -f $name, $requiredMigrations[$name])
}
Write-Output "Smoke endpoint: $smokeUri (HTTP 200)"
