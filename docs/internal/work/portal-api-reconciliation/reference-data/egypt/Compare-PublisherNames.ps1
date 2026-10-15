<#
.SYNOPSIS
  Explicit online exact name comparison to the hash-pinned bilingual publisher.
.DESCRIPTION
  Reads public data into memory only. No database/API/import/configuration access.
  Proves source correspondence, not independent linguistic correctness.
#>
[CmdletBinding()]
param([switch]$AllowNetwork)
$ErrorActionPreference = 'Stop'
if (-not $AllowNetwork) { throw 'Public network access requires -AllowNetwork.' }
$candidatePath = Join-Path $PSScriptRoot 'candidate.json'
$candidate = Get-Content -LiteralPath $candidatePath -Raw -Encoding utf8 | ConvertFrom-Json
$revision = '76b47b22872288c0d8b0c757975980e6986945e8'
$sources = @(
  @{ name = 'all-governorate.json'; collection = 'governorates'; hash = 'EA86B0A7C7219D1DC7C79D6FAECF1C66325F78443277CC491D8D39A94CC9CA27' },
  @{ name = 'all-district.json'; collection = 'cities'; hash = 'F265357BDF7248FD162F1CC23B7C8A68F966CA4BC2988FED408AEF078C1C01D8' },
  @{ name = 'all-shiyakha.json'; collection = 'areas'; hash = 'A4458361D556ADB1617C836D804FDE399757FA849D0ED7351A5C428E21A51002' }
)
Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(45)
$sha = [Security.Cryptography.SHA256]::Create()
$failures = New-Object System.Collections.Generic.List[string]
$results = @()
try {
  foreach ($source in $sources) {
    $url = "https://raw.githubusercontent.com/open-admin-data/egypt-administrative-divisions/$revision/data/$($source.name)"
    $bytes = $client.GetByteArrayAsync($url).GetAwaiter().GetResult()
    $hash = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '')
    if ($hash -cne $source.hash) { throw "Source drift: $($source.name). Requalify before use." }
    $rows = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
    $index = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($row in $rows) { $index.Add($row.id, $row) }
    $exactArabic = 0
    $exactEnglish = 0
    $documentedSuffix = 0
    foreach ($row in $candidate.($source.collection)) {
      if (-not $index.ContainsKey($row.key)) { $failures.Add("Missing key $($row.key)"); continue }
      $original = $index[$row.key]
      if ($row.arabicName -ceq $original.name.local) { $exactArabic++ }
      else { $failures.Add("Arabic mismatch $($row.key)") }
      if ($row.englishName -ceq $original.name.en) { $exactEnglish++ }
      elseif (($source.collection -ceq 'cities') -and ($row.disambiguated -eq $true) -and
              ($row.englishName -ceq ($original.name.en + ' Markaz'))) { $documentedSuffix++ }
      else { $failures.Add("Undocumented English mismatch $($row.key)") }
    }
    $results += [pscustomobject]@{
      collection = $source.collection; inputSha256 = $hash
      candidateCount = @($candidate.($source.collection)).Count
      exactArabic = $exactArabic; exactEnglish = $exactEnglish
      documentedEnglishSuffix = $documentedSuffix
    }
  }
  [pscustomobject]@{
    candidateSha256 = (Get-FileHash -LiteralPath $candidatePath -Algorithm SHA256).Hash
    sourceRevision = $revision; results = $results
    failures = @($failures.ToArray()); linguisticCertification = $false
  } | ConvertTo-Json -Depth 6
  if ($failures.Count -gt 0) { exit 1 }
} finally { $sha.Dispose(); $client.Dispose() }
