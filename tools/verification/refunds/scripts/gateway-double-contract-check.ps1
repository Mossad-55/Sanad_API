#requires -Version 5.1
<#
.SYNOPSIS
    Paymob client-type contract check — STATIC config + DI source inspection only.

.DESCRIPTION
    W2-V0 / S7 verification asset. Verifies the config seams that decide which
    IPaymobClient the DI graph binds, WITHOUT executing the payment path. It
    never starts the API, never opens a connection, never calls Paymob.

    Why this exists (W0 D4, highest verification risk): the selection is
    KEY-PRESENCE-GATED, not environment-gated. `AddFamiliesInfrastructure`
    registers DevelopmentPaymobClient when `Paymob:SecretKey` is blank and the
    real PaymobClient when it is present, regardless of DOTNET_ENVIRONMENT. A
    Development host that happens to carry a real key would bind the REAL
    client, so every refund verification run must assert the resolved type.

    This script proves the seam two ways and prints the resolved client type:
      1. Reads the DI source of truth (DependencyInjection.cs) and confirms the
         key-presence gate is the only switch — no environment check exists.
      2. Reports, for a given appsettings file or process environment, which
         type the graph WOULD produce (absent key -> DevelopmentPaymobClient;
         present key -> PaymobClient). The raw key value is never printed.

.PARAMETER AppSettingsPath
    Optional. An appsettings*.json file whose `Paymob:SecretKey` presence is
    inspected. The value itself is never emitted — only present/absent.

.PARAMETER UseProcessEnvironment
    Optional. Instead of a file, inspect the current process environment for
    `Paymob__SecretKey` / `Paymob:SecretKey` presence.

.PARAMETER RepoRoot
    Optional. Repository root. Defaults to four levels above this script
    (tools/verification/refunds/scripts).

.PARAMETER Expect
    Optional. 'Double' (default) or 'Real'. The script fails when the resolved
    type does not match. Refund verification runs pass -Expect Double.

.NOTES
    Authored NOT RUN. Static inspection only: no API start, no DB, no gateway.
#>
[CmdletBinding()]
param(
    [string]$AppSettingsPath,

    [switch]$UseProcessEnvironment,

    [string]$RepoRoot,

    [ValidateSet('Double', 'Real')]
    [string]$Expect = 'Double'
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$failures = New-Object System.Collections.Generic.List[string]

Write-Host 'gateway-double-contract-check (static inspection only)'
Write-Host ("  repo root : {0}" -f $RepoRoot)
Write-Host ''

# ---------------------------------------------------------------------------
# 1. DI source of truth — the key-presence gate, and the absence of any
#    environment gate for the Paymob client.
# ---------------------------------------------------------------------------
$diPath = Join-Path $RepoRoot 'src\Modules\Families\Infrastructure\Sanad.Modules.Families.Infrastructure\DependencyInjection.cs'
if (-not (Test-Path -LiteralPath $diPath)) {
    $failures.Add("DI source not found: $diPath")
}
else {
    $diText = Get-Content -LiteralPath $diPath -Raw

    $gatePattern = '(?s)paymobSecretKey\s*=.*?IsNullOrWhiteSpace\(paymobSecretKey\)'
    if ($diText -notmatch $gatePattern) {
        $failures.Add('DI source no longer contains the key-presence gate IsNullOrWhiteSpace(paymobSecretKey); the selection contract changed.')
    }
    else {
        Write-Host '  [ok] key-presence gate present in AddFamiliesInfrastructure'
    }

    if ($diText -match 'DevelopmentPaymobClient' -and $diText -match 'PaymobClient') {
        Write-Host '  [ok] both client registrations present'
    }
    else {
        $failures.Add('DI source does not register both DevelopmentPaymobClient and PaymobClient.')
    }

    # The client selection must NOT depend on the environment. If an
    # environment variable read appears next to the client registration, the
    # gate is no longer key-presence-only and this pack's core assumption broke.
    if ($diText -match '(?i)(GetEnvironmentVariable|DOTNET_ENVIRONMENT|ASPNETCORE_ENVIRONMENT|IsDevelopment)') {
        $failures.Add('DI source now references an environment check near the Paymob client selection; selection must remain key-presence-gated (W0 D4).')
    }
    else {
        Write-Host '  [ok] no environment gate in the Paymob client selection'
    }
}

# ---------------------------------------------------------------------------
# 2. Resolve the client type the graph WOULD produce for the given config.
# ---------------------------------------------------------------------------
function Resolve-PaymobClientType {
    [CmdletBinding()]
    param(
        [string]$SecretKeyValue
    )

    if ([string]::IsNullOrWhiteSpace($SecretKeyValue)) { return 'DevelopmentPaymobClient' }
    return 'PaymobClient'
}

$keyPresent = $false
$sourceDescription = ''

if ($UseProcessEnvironment -or -not $AppSettingsPath) {
    $envA = [Environment]::GetEnvironmentVariable('Paymob__SecretKey')
    $envB = [Environment]::GetEnvironmentVariable('Paymob:SecretKey')
    $keyPresent = -not ([string]::IsNullOrWhiteSpace($envA) -and [string]::IsNullOrWhiteSpace($envB))
    $sourceDescription = 'process environment (Paymob__SecretKey / Paymob:SecretKey presence)'
}
else {
    if (-not (Test-Path -LiteralPath $AppSettingsPath)) {
        throw "AppSettingsPath not found: $AppSettingsPath"
    }
    $json = Get-Content -LiteralPath $AppSettingsPath -Raw | ConvertFrom-Json
    $sourceDescription = "appsettings file: $AppSettingsPath"
    if ($json.PSObject.Properties.Name -contains 'Paymob') {
        $paymob = $json.Paymob
        if ($null -ne $paymob.PSObject.Properties['SecretKey']) {
            $keyPresent = -not [string]::IsNullOrWhiteSpace([string]$paymob.SecretKey)
        }
    }
}

$resolved = Resolve-PaymobClientType -SecretKeyValue ($(if ($keyPresent) { 'present' } else { '' }))

Write-Host ''
Write-Host ("  source          : {0}" -f $sourceDescription)
Write-Host ("  Paymob:SecretKey : {0}" -f $(if ($keyPresent) { 'PRESENT (value withheld)' } else { 'absent' }))
Write-Host ("  resolved client  : {0}" -f $resolved)
Write-Host ("  expected         : {0}" -f $(if ($Expect -eq 'Double') { 'DevelopmentPaymobClient' } else { 'PaymobClient' }))

$expectedType = if ($Expect -eq 'Double') { 'DevelopmentPaymobClient' } else { 'PaymobClient' }
if ($resolved -ne $expectedType) {
    $failures.Add("Resolved client '$resolved' does not match expected '$expectedType'.")
}

# ---------------------------------------------------------------------------
# 3. Result.
# ---------------------------------------------------------------------------
Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host 'RESULT: FAIL'
    foreach ($failure in $failures) { Write-Host ("  - {0}" -f $failure) }
    exit 1
}

Write-Host 'RESULT: PASS — resolved Paymob client matches the expected deterministic double.'
exit 0
