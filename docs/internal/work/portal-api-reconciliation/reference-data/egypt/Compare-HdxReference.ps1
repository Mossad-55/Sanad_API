<#
.SYNOPSIS
  Explicit online, read-only comparison with the pinned OCHA/CAPMAS reference.
.DESCRIPTION
  Downloads the public ZIP into memory only; executes no downloaded content.
  Does not access databases, credentials, APIs under test, or local configuration.
  Exact names/parents are compared, not fuzzy-matched. A match establishes
  correspondence to this historical reference, NOT current release readiness.
  Exit 0 means the comparison executed; it is not a catalogue acceptance gate.
#>
[CmdletBinding()]
param([switch]$AllowNetwork, [string]$CandidatePath = '')
$ErrorActionPreference = 'Stop'
if (-not $AllowNetwork) { throw 'Public network access requires -AllowNetwork.' }
if ([string]::IsNullOrWhiteSpace($CandidatePath)) {
  $CandidatePath = Join-Path $PSScriptRoot 'candidate.json'
}
$candidate = Get-Content -LiteralPath $CandidatePath -Raw -Encoding utf8 | ConvertFrom-Json
$resourceUrl = 'https://data.humdata.org/dataset/b90d81ba-7c7a-4283-9899-827480d80a79/resource/2080e95f-51c7-45b6-b5ab-613ff1cfc041/download/egy_admin_boundaries.geojson.zip'
$expectedHash = '259987BE0B7C5E367D52EADF3DFEA39802C7750CACE8498833E3212D61B9DBA9'
Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.IO.Compression
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(60)
$archive = $null
$memory = $null
$sha = [Security.Cryptography.SHA256]::Create()
try {
  $metadata = Invoke-RestMethod 'https://data.humdata.org/api/3/action/package_show?id=cod-ab-egy' -TimeoutSec 30
  if (-not $metadata.success) { throw 'HDX metadata request was not successful.' }
  $bytes = $client.GetByteArrayAsync($resourceUrl).GetAwaiter().GetResult()
  $hash = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '')
  if ($hash -cne $expectedHash) { throw "Reference changed: expected $expectedHash, got $hash. Requalify before use." }
  $memory = New-Object System.IO.MemoryStream(,$bytes)
  $archive = New-Object System.IO.Compression.ZipArchive($memory)
  $summaries = @()
  foreach ($level in @(1, 2, 3)) {
    $entry = $archive.GetEntry("egy_admin$level.geojson")
    if ($null -eq $entry) { throw "Missing reference level $level." }
    $reader = New-Object System.IO.StreamReader($entry.Open())
    try { $reference = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    $keyField = "adm${level}_pcode"
    $arabicField = "adm${level}_name1"
    $englishField = "adm${level}_name"
    $parentField = "adm$($level - 1)_pcode"
    $referenceMap = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($feature in $reference.features) {
      $row = $feature.properties
      if ($referenceMap.ContainsKey($row.$keyField)) { throw "Duplicate reference key $($row.$keyField)." }
      $referenceMap.Add($row.$keyField, $row)
    }
    $rows = switch ($level) { 1 { @($candidate.governorates) }; 2 { @($candidate.cities) }; 3 { @($candidate.areas) } }
    $candidateMap = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    $missing = @()
    $arabicMismatch = @()
    $englishMismatch = @()
    $parentMismatch = @()
    $blankReferenceEnglish = 0
    foreach ($row in $rows) {
      $candidateMap.Add($row.key, $row)
      if (-not $referenceMap.ContainsKey($row.key)) { $missing += $row.key; continue }
      $refRow = $referenceMap[$row.key]
      if ($row.arabicName -cne $refRow.$arabicField) { $arabicMismatch += $row.key }
      if ([string]::IsNullOrWhiteSpace($refRow.$englishField)) { $blankReferenceEnglish++ }
      elseif ($row.englishName -cne $refRow.$englishField) { $englishMismatch += $row.key }
      if ($level -gt 1) {
        $parent = if ($level -eq 2) { $row.governorateKey } else { $row.cityKey }
        if ($parent -cne $refRow.$parentField) { $parentMismatch += $row.key }
      }
    }
    $notInCandidate = @($referenceMap.Keys | Where-Object { -not $candidateMap.ContainsKey($_) } | Sort-Object)
    $summaries += [pscustomobject]@{
      level = $level; referenceCount = $referenceMap.Count; candidateCount = $rows.Count
      missingReferenceKeys = @($missing); referenceKeysNotInCandidate = $notInCandidate
      arabicMismatchKeys = @($arabicMismatch | Sort-Object)
      englishMismatchKeys = @($englishMismatch | Sort-Object)
      referenceEnglishBlankCount = $blankReferenceEnglish
      parentMismatchKeys = @($parentMismatch | Sort-Object)
      referenceValidOn = @($reference.features.properties.valid_on | Sort-Object -Unique)
    }
  }
  [pscustomobject]@{
    checkedOnUtc = [DateTime]::UtcNow.ToString('o')
    metadataUrl = 'https://data.humdata.org/api/3/action/package_show?id=cod-ab-egy'
    metadataModified = $metadata.result.metadata_modified
    datasetSource = $metadata.result.dataset_source
    licenseId = $metadata.result.license_id
    licenseUrl = $metadata.result.license_url
    datasetNotes = $metadata.result.notes
    resourceUrl = $resourceUrl; referenceSha256 = $hash; bytes = $bytes.Length
    candidateSha256 = (Get-FileHash -LiteralPath $CandidatePath -Algorithm SHA256).Hash
    levels = $summaries
    releaseReadinessEstablished = $false
  } | ConvertTo-Json -Depth 8
} finally {
  if ($null -ne $archive) { $archive.Dispose() }
  if ($null -ne $memory) { $memory.Dispose() }
  $sha.Dispose()
  $client.Dispose()
}
