<#
.SYNOPSIS
  Offline full-catalogue text hygiene and conservative near-duplicate scan.
.DESCRIPTION
  No data is changed. Run Validate-Catalogue.ps1 separately for structural rules.
  Name warnings are review findings, never permission to merge/rename records.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$path = Join-Path $PSScriptRoot 'candidate.json'
$catalogue = Get-Content -LiteralPath $path -Raw -Encoding utf8 | ConvertFrom-Json
$failures = New-Object System.Collections.Generic.List[string]
$warnings = @()
$nearDuplicates = @()
$maximumArabic = 0
$maximumEnglish = 0
foreach ($level in @('governorates', 'cities', 'areas')) {
  $names = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
  foreach ($row in $catalogue.$level) {
    $parent = if ($level -ceq 'cities') { $row.governorateKey } elseif ($level -ceq 'areas') { $row.cityKey } else { 'country' }
    foreach ($field in @('arabicName', 'englishName')) {
      $name = $row.$field
      if (-not ($name -is [string]) -or [string]::IsNullOrWhiteSpace($name)) {
        $failures.Add("Invalid name $($row.key)/$field"); continue
      }
      if ($name.Length -gt 150 -or $name -cne $name.Trim()) { $failures.Add("Length/trim $($row.key)/$field") }
      if ($name -match '[\x00-\x1F\x7F\uFFFD\u200B-\u200F\u202A-\u202E\u2066-\u2069]') { $failures.Add("Control/replacement character $($row.key)/$field") }
      if ($name -match '(?i)^(null|undefined|unknown|test|n/?a|none|nan)$') { $failures.Add("Placeholder $($row.key)/$field") }
      if ($field -ceq 'arabicName') { $maximumArabic = [Math]::Max($maximumArabic, $name.Length) }
      else { $maximumEnglish = [Math]::Max($maximumEnglish, $name.Length) }
      # Deliberately not aggressive Arabic spelling folding or fuzzy matching.
      $normal = $name.Normalize([Text.NormalizationForm]::FormKC).ToLowerInvariant() -replace '[\s\p{P}\p{M}\u0640]', ''
      $index = "$parent|$field|$normal"
      if (-not $names.ContainsKey($index)) { $names[$index] = New-Object System.Collections.Generic.List[object] }
      $names[$index].Add([pscustomobject]@{key=$row.key; name=$name})
    }
    if ($row.arabicName -match '[?؟_]' -or $row.englishName -match '[?؟_]') {
      $warnings += [pscustomobject]@{
        key = $row.key; arabicName = $row.arabicName; englishName = $row.englishName
        reason = if ($row.arabicName -match '[?؟]' -or $row.englishName -match '[?؟]') { 'question-mark-source-label' } else { 'underscore-delimiter-review' }
      }
    }
  }
  foreach ($group in $names.GetEnumerator() | Where-Object { $_.Value.Count -gt 1 } | Sort-Object Key) {
    $nearDuplicates += [pscustomobject]@{level=$level; match=$group.Key; rows=@($group.Value.ToArray())}
  }
}
[pscustomobject]@{
  candidateSha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
  records = @($catalogue.governorates).Count + @($catalogue.cities).Count + @($catalogue.areas).Count
  maximumArabicLength = $maximumArabic; maximumEnglishLength = $maximumEnglish
  failures = @($failures.ToArray()); normalizedSameParentCollisions = @($nearDuplicates)
  questionMarkRows = @($warnings | Where-Object reason -eq 'question-mark-source-label').Count
  delimiterReviewRows = @($warnings | Where-Object reason -eq 'underscore-delimiter-review').Count
  displayReview = @($warnings); linguisticCertification = $false
} | ConvertTo-Json -Depth 8
if ($failures.Count -gt 0) { exit 1 }
