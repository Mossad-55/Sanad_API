<#
.SYNOPSIS
  Offline validation for the All-Egypt catalogue candidate (PORTAL-06A2).
.DESCRIPTION
  Reads candidate.json (plus synthetic validation-fixtures.json) as data only.
  No network, API, database, credential, mutation, or import is performed.
  Uses built-in PowerShell/.NET facilities; no package required.
  Exit 0 when the candidate and all fixture expectations hold, else exit 1.
.PARAMETER CandidatePath
  Path to candidate.json. Defaults to the sibling candidate.json.
.PARAMETER FixturesPath
  Path to validation-fixtures.json. Defaults to the sibling file.
#>
[CmdletBinding()]
param(
  [string]$CandidatePath = '',
  [string]$FixturesPath = ''
)

$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptDir)) { $scriptDir = (Get-Location).Path }
if ([string]::IsNullOrWhiteSpace($CandidatePath)) { $CandidatePath = (Join-Path $scriptDir 'candidate.json') }
if ([string]::IsNullOrWhiteSpace($FixturesPath)) { $FixturesPath = (Join-Path $scriptDir 'validation-fixtures.json') }
$failures = New-Object System.Collections.Generic.List[string]

function Add-Failure([string]$message) {
  $failures.Add($message) | Out-Null
}

function Trim-Text($value) {
  return "$value".Trim()
}

function Test-Names($rows, [string]$level) {
  foreach ($r in $rows) {
    $ar = Trim-Text $r.arabicName
    $en = Trim-Text $r.englishName
    if ($ar -eq '') { Add-Failure "$level $($r.key): blank Arabic name" }
    if ($en -eq '') { Add-Failure "$level $($r.key): blank English name" }
    if ($ar.Length -gt 150) { Add-Failure "$level $($r.key): Arabic name over 150 chars" }
    if ($en.Length -gt 150) { Add-Failure "$level $($r.key): English name over 150 chars" }
  }
}

function Test-NoDupKeys($rows, [string]$level) {
  $seen = @{}
  foreach ($r in $rows) {
    $k = Trim-Text $r.key
    if ($k -eq '') { Add-Failure "$level row with blank key"; continue }
    if ($seen.ContainsKey($k)) { Add-Failure "$level duplicate key: $k" } else { $seen[$k] = $true }
  }
}

function Test-Candidate($data, [string]$label) {
  $govs = @($data.governorates)
  $cities = @($data.cities)
  $areas = @($data.areas)
  $gkeys = @{}
  foreach ($g in $govs) { $gkeys[(Trim-Text $g.key)] = $true }
  $ckeys = @{}
  foreach ($c in $cities) { $ckeys[(Trim-Text $c.key)] = $true }

  Test-NoDupKeys $govs "$label governorate"
  Test-NoDupKeys $cities "$label city"
  Test-NoDupKeys $areas "$label area"
  Test-Names $govs "$label governorate"
  Test-Names $cities "$label city"
  Test-Names $areas "$label area"

  foreach ($c in $cities) {
    if (-not $gkeys.ContainsKey((Trim-Text $c.governorateKey))) {
      Add-Failure "$label city $($c.key): parent governorate '$($c.governorateKey)' not found"
    }
  }
  foreach ($a in $areas) {
    if (-not $ckeys.ContainsKey((Trim-Text $a.cityKey))) {
      Add-Failure "$label area $($a.key): parent city '$($a.cityKey)' not found"
    }
    if ([string]::IsNullOrWhiteSpace($a.verification)) {
      Add-Failure "$label area $($a.key): missing verification flag"
    }
  }

  # Same-parent exact collisions (application rule: Arabic OR English equal).
  $cityArByGov = @{}
  $cityEnByGov = @{}
  foreach ($c in $cities) {
    $gk = Trim-Text $c.governorateKey
    if (-not $cityArByGov.ContainsKey($gk)) {
      $cityArByGov[$gk] = @{}
      $cityEnByGov[$gk] = @{}
    }
    $ar = Trim-Text $c.arabicName
    $en = Trim-Text $c.englishName
    if ($cityArByGov[$gk].ContainsKey($ar)) { Add-Failure "$label city collision under $gk : Arabic '$ar'" }
    else { $cityArByGov[$gk][$ar] = $true }
    if ($cityEnByGov[$gk].ContainsKey($en)) { Add-Failure "$label city collision under $gk : English '$en'" }
    else { $cityEnByGov[$gk][$en] = $true }
  }
  $areaArByCity = @{}
  $areaEnByCity = @{}
  foreach ($a in $areas) {
    $ck = Trim-Text $a.cityKey
    if (-not $areaArByCity.ContainsKey($ck)) {
      $areaArByCity[$ck] = @{}
      $areaEnByCity[$ck] = @{}
    }
    $ar = Trim-Text $a.arabicName
    $en = Trim-Text $a.englishName
    if ($areaArByCity[$ck].ContainsKey($ar)) { Add-Failure "$label area collision under city ${ck}: Arabic '$ar'" }
    else { $areaArByCity[$ck][$ar] = $true }
    if ($areaEnByCity[$ck].ContainsKey($en)) { Add-Failure "$label area collision under city ${ck}: English '$en'" }
    else { $areaEnByCity[$ck][$en] = $true }
  }

  # Every city must own at least one area (uncovered branch = failure).
  $areaCountByCity = @{}
  foreach ($a in $areas) {
    $ck = Trim-Text $a.cityKey
    if (-not $areaCountByCity.ContainsKey($ck)) { $areaCountByCity[$ck] = 0 }
    $areaCountByCity[$ck]++
  }
  foreach ($c in $cities) {
    if (-not $areaCountByCity.ContainsKey((Trim-Text $c.key))) {
      Add-Failure "$label city $($c.key): zero areas (uncovered branch)"
    }
  }
}

# --- main candidate ---
$bytes = [System.IO.File]::ReadAllBytes($CandidatePath)
$hash = ([System.Security.Cryptography.SHA256]::Create().ComputeHash($bytes) | ForEach-Object { $_.ToString('X2') }) -join ''
$data = Get-Content -LiteralPath $CandidatePath -Raw | ConvertFrom-Json
$before = $failures.Count
Test-Candidate $data 'candidate'
$candidateFailures = $failures.Count - $before
$govCount = @($data.governorates).Count
$cityCount = @($data.cities).Count
$areaCount = @($data.areas).Count
$provCount = @(@($data.areas) | Where-Object { $_.verification -eq 'provisional-census' }).Count
$disCount = @(@($data.cities) | Where-Object { $_.disambiguated -eq $true }).Count

Write-Output "candidate sha256: $hash"
Write-Output "counts: governorates=$govCount cities=$cityCount areas=$areaCount provisional-areas=$provCount disambiguated-cities=$disCount"
Write-Output "candidate failures: $candidateFailures"

# --- synthetic fixtures (each must be rejected iff expectValid is false) ---
# Expected rejections are evidence, not failures: collect them separately so
# only candidate failures and fixture-expectation mismatches fail the run.
$fx = Get-Content -LiteralPath $FixturesPath -Raw | ConvertFrom-Json
$fxFailed = 0
foreach ($case in $fx.cases) {
  $saved = $failures
  $failures = New-Object System.Collections.Generic.List[string]
  Test-Candidate $case.candidate ("fixture '$($case.name)'")
  $rejected = ($failures.Count -gt 0)
  $expected = -not [bool]$case.expectValid
  if ($rejected -eq $expected) {
    Write-Output "fixture $($case.name): PASS (rejected=$rejected as expected)"
  } else {
    $fxFailed++
    Write-Output "fixture $($case.name): FAIL (rejected=$rejected, expected rejection=$expected)"
    foreach ($f in $failures) { Write-Output "FIXTURE-FAIL: $f" }
  }
  $failures = $saved
}

Write-Output "total failures: $($candidateFailures + $fxFailed) (candidate=$candidateFailures, fixture-mismatches=$fxFailed)"
if (($candidateFailures + $fxFailed) -gt 0) {
  foreach ($f in $failures) { Write-Output "FAIL: $f" }
  exit 1
}
Write-Output 'VALIDATION PASSED'
exit 0
