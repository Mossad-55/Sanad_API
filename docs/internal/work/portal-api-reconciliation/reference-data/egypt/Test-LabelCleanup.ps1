<#
.SYNOPSIS
  Offline proof that only the 16 owner-approved label pairs differ from the base.
.DESCRIPTION
  Read-only Git/JSON comparison. No network, database, host or config access.
  Can run from an uncommitted checkout. Whole-document comparison protects all
  IDs, parents, verification flags, metadata and non-owned records.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$candidatePath = Join-Path $PSScriptRoot 'candidate.json'
$manifestPath = Join-Path $PSScriptRoot 'label-corrections.json'
$proposalPath = Join-Path $PSScriptRoot '../../reports/phase-06-label-cleanup-proposal.md'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
$candidate = Get-Content -LiteralPath $candidatePath -Raw -Encoding utf8 | ConvertFrom-Json
$baseRevision = 'f932c46d826f758837398242ea370b6521c75e0e'
if ($manifest.baseRevision -cne $baseRevision) { throw 'Unexpected baseline revision.' }
$allowed = @('EG190806','EG190809','EG190810','EG310205','EG020507','EG170410','EG150305','EG150306',
  'EG150307','EG020105','EG020110','EG030601','EG150721','EG181350','EG170227','EG122119')
$changes = @($manifest.changes)
if ($changes.Count -ne 16) { throw 'Expected 16 corrections.' }
$seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
foreach ($change in $changes) {
  if (($allowed -cnotcontains $change.key) -or (-not $seen.Add($change.key))) { throw 'Unexpected or duplicate correction key.' }
}

# Pin the accepted table as well as the manifest; arbitrary new after-values
# cannot be approved merely by adding an exception to the manifest.
$proposalRows = @{}
foreach ($line in Get-Content -LiteralPath $proposalPath -Encoding utf8) {
  if ($line -match '^\| EG') {
    $cells = $line.Split('|') | ForEach-Object { $_.Trim() }
    $proposalRows.Add($cells[1], @{ beforeArabic=$cells[2]; afterArabic=$cells[3]; afterEnglish=$cells[4] })
  }
}
if ($proposalRows.Count -ne 16) { throw 'Expected 16 approved proposal rows.' }

$gitStart = New-Object Diagnostics.ProcessStartInfo
$gitStart.FileName = 'git'
$gitStart.Arguments = "show ${baseRevision}:docs/internal/work/portal-api-reconciliation/reference-data/egypt/candidate.json"
$gitStart.WorkingDirectory = $PSScriptRoot
$gitStart.UseShellExecute = $false
$gitStart.CreateNoWindow = $true
$gitStart.RedirectStandardOutput = $true
$gitStart.RedirectStandardError = $true
$gitStart.StandardOutputEncoding = [Text.Encoding]::UTF8
$gitProcess = [Diagnostics.Process]::Start($gitStart)
try {
  $baseOutput = $gitProcess.StandardOutput.ReadToEndAsync()
  $baseError = $gitProcess.StandardError.ReadToEndAsync()
  $gitProcess.WaitForExit()
  if ($gitProcess.ExitCode -ne 0) { throw ('Unable to read pinned baseline: ' + $baseError.GetAwaiter().GetResult()) }
  $expected = $baseOutput.GetAwaiter().GetResult() | ConvertFrom-Json
} finally { $gitProcess.Dispose() }

$areaIndex = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
foreach ($row in $expected.areas) { $areaIndex.Add($row.key, $row) }
foreach ($change in $changes) {
  if (-not $areaIndex.ContainsKey($change.key)) { throw "Missing original row $($change.key)." }
  $original = $areaIndex[$change.key]
  $proposal = $proposalRows[$change.key]
  if (($original.cityKey -cne $change.cityKey) -or
      ($original.arabicName -cne $change.before.arabicName) -or
      ($original.englishName -cne $change.before.englishName) -or
      ($proposal.beforeArabic -cne $change.before.arabicName) -or
      ($proposal.afterArabic -cne $change.after.arabicName) -or
      ($proposal.afterEnglish -cne $change.after.englishName)) { throw "Approval/baseline mismatch $($change.key)." }
  if ($original.arabicName -ceq $change.after.arabicName) { throw "Unchanged correction $($change.key)." }
  $original.arabicName = $change.after.arabicName
  $original.englishName = $change.after.englishName
}
if (($expected | ConvertTo-Json -Depth 30 -Compress) -cne ($candidate | ConvertTo-Json -Depth 30 -Compress)) {
  throw 'Candidate differs from the pinned baseline plus ONLY the approved label pairs.'
}
Write-Output 'PASS: exactly 16 approved label rows; every other row/property and every key, parent, count and verification flag unchanged.'
Write-Output ('candidate sha256: ' + (Get-FileHash -LiteralPath $candidatePath -Algorithm SHA256).Hash)
