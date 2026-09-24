[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,
    [string]$ExpectedRevision
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

$smokeUri = "{0}/api/v1/lookups/services" -f $BaseUrl.TrimEnd('/')
Add-Type -AssemblyName System.Net.Http
$httpClient = [System.Net.Http.HttpClient]::new()
try {
    $response = $httpClient.GetAsync($smokeUri).GetAwaiter().GetResult()
    if ([int]$response.StatusCode -ne 200) {
        throw "API smoke request returned HTTP $([int]$response.StatusCode), expected 200."
    }
}
finally {
    $httpClient.Dispose()
}

Write-Output "Manifest revision: $($manifest.revision)"
Write-Output "Required Families migration: $($manifest.requiredFamiliesMigration)"
Write-Output "Smoke endpoint: $smokeUri (HTTP $([int]$response.StatusCode))"
