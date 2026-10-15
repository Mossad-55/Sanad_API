<#
.SYNOPSIS
  Offline validation for the All-Egypt catalogue candidate (PORTAL-06A2).
.DESCRIPTION
  Reads candidate.json, boundary-checks.json and validation-fixtures.json as
  data only. No network, API, database, credential, mutation, or import.
  Built-in PowerShell/.NET only; no package required.

  Default mode validates structure only: shape/relationships are valid, which
  is NOT release readiness. -Readiness runs the separate fail-closed release
  gate afterwards and its exit code reflects readiness, not structure.
.PARAMETER CandidatePath
  Defaults to sibling candidate.json.
.PARAMETER ManifestPath
  Defaults to sibling boundary-checks.json (national checks only).
.PARAMETER FixturesPath
  Defaults to sibling validation-fixtures.json.
.PARAMETER Readiness
  Run the release-readiness gate after structural validation.
#>
[CmdletBinding()]
param(
  [string]$CandidatePath = '',
  [string]$ManifestPath = '',
  [string]$FixturesPath = '',
  [switch]$Readiness
)

$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptDir)) { $scriptDir = (Get-Location).Path }
if ([string]::IsNullOrWhiteSpace($CandidatePath)) { $CandidatePath = (Join-Path $scriptDir 'candidate.json') }
if ([string]::IsNullOrWhiteSpace($ManifestPath)) { $ManifestPath = (Join-Path $scriptDir 'boundary-checks.json') }
if ([string]::IsNullOrWhiteSpace($FixturesPath)) { $FixturesPath = (Join-Path $scriptDir 'validation-fixtures.json') }

$failures = New-Object System.Collections.Generic.List[string]

function Add-Failure([string]$message) {
  $failures.Add($message) | Out-Null
}

function Trim-Text($value) {
  return "$value".Trim()
}

function New-OrdinalMap() {
  return New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
}

function Test-IsRowObject($value) {
  return ($value -is [System.Management.Automation.PSCustomObject]) -or ($value -is [System.Collections.Hashtable])
}

function Test-RowTypes($rows, [string]$level, [string]$label) {
  foreach ($r in $rows) {
    if (-not (Test-IsRowObject $r)) { Add-Failure "$label $level row is not an object"; continue }
    foreach ($f in @('key', 'arabicName', 'englishName')) {
      if (($null -ne $r.$f) -and (-not ($r.$f -is [string]))) {
        Add-Failure "$label $level row: field '$f' must be a string"
      }
    }
    if ($level -eq 'city' -and ($null -ne $r.governorateKey) -and (-not ($r.governorateKey -is [string]))) {
      Add-Failure "$label city row: field 'governorateKey' must be a string"
    }
    if ($level -eq 'area') {
      if (($null -ne $r.cityKey) -and (-not ($r.cityKey -is [string]))) {
        Add-Failure "$label area row: field 'cityKey' must be a string"
      }
      if (($null -ne $r.verification) -and (-not ($r.verification -is [string]))) {
        Add-Failure "$label area row: field 'verification' must be a string"
      }
    }
    if (($null -ne $r.disambiguated) -and (-not ($r.disambiguated -is [bool]))) {
      Add-Failure "$label $level row: field 'disambiguated' must be a boolean"
    }
  }
}

function Test-Names($rows, [string]$level, [string]$label) {
  foreach ($r in $rows) {
    $ar = Trim-Text $r.arabicName
    $en = Trim-Text $r.englishName
    if ($ar -eq '') { Add-Failure "$label $level $($r.key): blank Arabic name" }
    if ($en -eq '') { Add-Failure "$label $level $($r.key): blank English name" }
    if ($ar.Length -gt 150) { Add-Failure "$label $level $($r.key): Arabic name over 150 chars" }
    if ($en.Length -gt 150) { Add-Failure "$label $level $($r.key): English name over 150 chars" }
    # Language presence: Arabic script required in Arabic names, Latin in
    # English names. Digits/punctuation/diacritics are allowed alongside.
    if (($ar -ne '') -and ($ar -notmatch '[\u0600-\u06FF]')) {
      Add-Failure "$label $level $($r.key): Arabic name carries no Arabic script (untranslated?)"
    }
    if (($en -ne '') -and ($en -notmatch '[A-Za-z]')) {
      Add-Failure "$label $level $($r.key): English name carries no Latin script (untranslated?)"
    }
  }
}

function Test-NoDupKeys($rows, [string]$level, [string]$label) {
  $seen = New-OrdinalMap
  foreach ($r in $rows) {
    $k = Trim-Text $r.key
    if ($k -eq '') { Add-Failure "$label $level row with blank key"; continue }
    if ($seen.ContainsKey($k)) { Add-Failure "$label $level duplicate key: $k" } else { $seen[$k] = $true }
  }
}

function Test-Candidate($data, [string]$label, $manifest) {
  if (-not (Test-IsRowObject $data)) { Add-Failure "$label root must be an object"; return }
  $govs = @($data.governorates)
  $cities = @($data.cities)
  $areas = @($data.areas)
  if ($govs.Count -eq 0) { Add-Failure "${label}: governorates collection is empty" }
  if ($cities.Count -eq 0) { Add-Failure "${label}: cities collection is empty" }
  if ($areas.Count -eq 0) { Add-Failure "${label}: areas collection is empty" }

  Test-RowTypes $govs 'governorate' $label
  Test-RowTypes $cities 'city' $label
  Test-RowTypes $areas 'area' $label
  Test-NoDupKeys $govs 'governorate' $label
  Test-NoDupKeys $cities 'city' $label
  Test-NoDupKeys $areas 'area' $label
  Test-Names $govs 'governorate' $label
  Test-Names $cities 'city' $label
  Test-Names $areas 'area' $label

  # Governorate names collide across the whole country (exact semantics).
  $govAr = New-OrdinalMap
  $govEn = New-OrdinalMap
  foreach ($g in $govs) {
    $ar = Trim-Text $g.arabicName
    $en = Trim-Text $g.englishName
    if (($ar -ne '') -and $govAr.ContainsKey($ar)) { Add-Failure "$label governorate name collision: Arabic '$ar'" }
    else { if ($ar -ne '') { $govAr[$ar] = $true } }
    if (($en -ne '') -and $govEn.ContainsKey($en)) { Add-Failure "$label governorate name collision: English '$en'" }
    else { if ($en -ne '') { $govEn[$en] = $true } }
  }

  $gkeys = New-OrdinalMap
  foreach ($g in $govs) { $k = Trim-Text $g.key; if (($k -ne '') -and (-not $gkeys.ContainsKey($k))) { $gkeys[$k] = $true } }
  $ckeys = New-OrdinalMap
  foreach ($c in $cities) { $k = Trim-Text $c.key; if (($k -ne '') -and (-not $ckeys.ContainsKey($k))) { $ckeys[$k] = $true } }

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

  # Same-parent exact collisions, ordinal (case-sensitive) like PostgreSQL ==.
  $cityArByGov = @{}
  $cityEnByGov = @{}
  foreach ($c in $cities) {
    $gk = Trim-Text $c.governorateKey
    if (-not $cityArByGov.ContainsKey($gk)) {
      $cityArByGov[$gk] = New-OrdinalMap
      $cityEnByGov[$gk] = New-OrdinalMap
    }
    $ar = Trim-Text $c.arabicName
    $en = Trim-Text $c.englishName
    if (($ar -ne '') -and $cityArByGov[$gk].ContainsKey($ar)) { Add-Failure "$label city collision under $gk : Arabic '$ar'" }
    elseif ($ar -ne '') { $cityArByGov[$gk][$ar] = $true }
    if (($en -ne '') -and $cityEnByGov[$gk].ContainsKey($en)) { Add-Failure "$label city collision under $gk : English '$en'" }
    elseif ($en -ne '') { $cityEnByGov[$gk][$en] = $true }
  }
  $areaArByCity = @{}
  $areaEnByCity = @{}
  foreach ($a in $areas) {
    $ck = Trim-Text $a.cityKey
    if (-not $areaArByCity.ContainsKey($ck)) {
      $areaArByCity[$ck] = New-OrdinalMap
      $areaEnByCity[$ck] = New-OrdinalMap
    }
    $ar = Trim-Text $a.arabicName
    $en = Trim-Text $a.englishName
    if (($ar -ne '') -and $areaArByCity[$ck].ContainsKey($ar)) { Add-Failure "$label area collision under city ${ck}: Arabic '$ar'" }
    elseif ($ar -ne '') { $areaArByCity[$ck][$ar] = $true }
    if (($en -ne '') -and $areaEnByCity[$ck].ContainsKey($en)) { Add-Failure "$label area collision under city ${ck}: English '$en'" }
    elseif ($en -ne '') { $areaEnByCity[$ck][$en] = $true }
  }

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

  # National checks (approved set, coverage manifest, known parents, snapshot).
  if ($null -ne $manifest) {
    $approved = @($manifest.approvedGovernorates)
    $actualGovSet = New-OrdinalMap
    foreach ($g in $govs) { $k = Trim-Text $g.key; if (($k -ne '') -and (-not $actualGovSet.ContainsKey($k))) { $actualGovSet[$k] = $true } }
    foreach ($k in $approved) {
      if (-not $actualGovSet.ContainsKey($k)) { Add-Failure "$label national set: approved governorate '$k' missing" }
    }
    foreach ($k in @($actualGovSet.Keys)) {
      if ($approved -cnotcontains $k) { Add-Failure "$label national set: unexpected governorate '$k'" }
    }
    $cityCountByGov = @{}
    foreach ($c in $cities) {
      $gk = Trim-Text $c.governorateKey
      if (-not $cityCountByGov.ContainsKey($gk)) { $cityCountByGov[$gk] = 0 }
      $cityCountByGov[$gk]++
    }
    $areaCountByGov = @{}
    $cityGovOf = @{}
    foreach ($c in $cities) { $cityGovOf[(Trim-Text $c.key)] = (Trim-Text $c.governorateKey) }
    foreach ($a in $areas) {
      $gk = $cityGovOf[(Trim-Text $a.cityKey)]
      if ($null -ne $gk) {
        if (-not $areaCountByGov.ContainsKey($gk)) { $areaCountByGov[$gk] = 0 }
        $areaCountByGov[$gk]++
      }
    }
    foreach ($prop in @($manifest.expectedCities.PSObject.Properties)) {
      $actual = 0
      if ($cityCountByGov.ContainsKey($prop.Name)) { $actual = $cityCountByGov[$prop.Name] }
      if ($actual -ne [int]$prop.Value) { Add-Failure "$label coverage manifest: governorate $($prop.Name) cities $actual, expected $($prop.Value)" }
    }
    foreach ($prop in @($manifest.expectedAreas.PSObject.Properties)) {
      $actual = 0
      if ($areaCountByGov.ContainsKey($prop.Name)) { $actual = $areaCountByGov[$prop.Name] }
      if ($actual -ne [int]$prop.Value) { Add-Failure "$label coverage manifest: governorate $($prop.Name) areas $actual, expected $($prop.Value)" }
    }
    $cityByKey = @{}
    foreach ($c in $cities) { $cityByKey[(Trim-Text $c.key)] = $c }
    foreach ($prop in @($manifest.knownParents.PSObject.Properties)) {
      if (-not $cityByKey.ContainsKey($prop.Name)) {
        Add-Failure "$label known parent: city '$($prop.Name)' missing from candidate"
      } elseif ((Trim-Text $cityByKey[$prop.Name].governorateKey) -cne (Trim-Text $prop.Value.governorateKey)) {
        Add-Failure "$label known parent: city '$($prop.Name)' under '$($cityByKey[$prop.Name].governorateKey)', expected '$($prop.Value.governorateKey)'"
      }
    }
    if (($null -eq $data.snapshot) -or [string]::IsNullOrWhiteSpace($data.snapshot.source) -or [string]::IsNullOrWhiteSpace($data.snapshot.revision)) {
      Add-Failure "$label national set: snapshot source/revision metadata required"
    }
  }
}

# --- main candidate (national mode) ---
$bytes = [System.IO.File]::ReadAllBytes($CandidatePath)
$hash = ([System.Security.Cryptography.SHA256]::Create().ComputeHash($bytes) | ForEach-Object { $_.ToString('X2') }) -join ''
$data = Get-Content -LiteralPath $CandidatePath -Raw -Encoding utf8 | ConvertFrom-Json
$manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding utf8 | ConvertFrom-Json
$before = $failures.Count
Test-Candidate $data 'candidate' $manifest
$candidateFailures = $failures.Count - $before
$govCount = @($data.governorates).Count
$cityCount = @($data.cities).Count
$areaCount = @($data.areas).Count
$provCount = @(@($data.areas) | Where-Object { $_.verification -eq 'provisional-census' }).Count
$disCount = @(@($data.cities) | Where-Object { $_.disambiguated -eq $true }).Count

Write-Output "candidate sha256: $hash"
Write-Output "counts: governorates=$govCount cities=$cityCount areas=$areaCount provisional-areas=$provCount disambiguated-cities=$disCount"
Write-Output "structural failures: $candidateFailures"

# --- synthetic fixtures (shape mode, unless a case carries its own manifest) ---
$fx = Get-Content -LiteralPath $FixturesPath -Raw -Encoding utf8 | ConvertFrom-Json
$fxFailed = 0
foreach ($case in $fx.cases) {
  $saved = $failures
  $failures = New-Object System.Collections.Generic.List[string]
  $caseManifest = $null
  if ($null -ne $case.manifest) { $caseManifest = $case.manifest }
  Test-Candidate $case.candidate ("fixture '$($case.name)'") $caseManifest
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

Write-Output "structural total: $($candidateFailures + $fxFailed) (candidate=$candidateFailures, fixture-mismatches=$fxFailed)"
$structuralExit = 0
if (($candidateFailures + $fxFailed) -gt 0) {
  foreach ($f in $failures) { Write-Output "FAIL: $f" }
  $structuralExit = 1
} else {
  Write-Output 'STRUCTURAL PASSED (shape only - not release readiness)'
}

# --- release-readiness gate (fail closed; separate from structure) ---
if ($Readiness) {
  $rFails = New-Object System.Collections.Generic.List[string]
  $unresolved = @($manifest.unresolvedBranches)
  if ($unresolved.Count -gt 0) {
    $rFails.Add("readiness: $($unresolved.Count) unresolved branch(es)") | Out-Null
    foreach ($u in $unresolved) { $rFails.Add("readiness unresolved: $($u.scope) - $($u.reason)") | Out-Null }
  }
  if ($provCount -gt 0) {
    $rFails.Add("readiness: $provCount provisional area(s) without consumer verification") | Out-Null
  }
  foreach ($flag in @('provenanceAccepted', 'mappingDecisionsAccepted', 'consumerVerificationComplete')) {
    if ($manifest.readiness.$flag -ne $true) {
      $rFails.Add("readiness: manifest flag '$flag' is not accepted") | Out-Null
    }
  }
  $gaps = @($data.gaps)
  if ($gaps.Count -gt 0) {
    $rFails.Add("readiness: $($gaps.Count) open coverage gap(s)") | Out-Null
  }
  Write-Output "readiness failures: $($rFails.Count)"
  foreach ($f in $rFails) { Write-Output "NOT-READY: $f" }
  if ($structuralExit -ne 0) { Write-Output 'readiness: structural gate also failing (see above)' }
  if ($rFails.Count -gt 0) { exit 2 }
  if ($structuralExit -ne 0) { exit 1 }
  Write-Output 'READINESS PASSED'
  exit 0
}

exit $structuralExit
