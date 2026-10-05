[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('sanad-bruno-preflight-' + [Guid]::NewGuid().ToString('N'))
$testRoot = [System.IO.Path]::GetFullPath($testRoot)
$checks = 0

function Write-Fixture([string]$Relative, [string]$Content) {
    $target = Join-Path $testRoot $Relative
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
    [System.IO.File]::WriteAllText($target, $Content)
}
function Assert-Check([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "Failed: $Name" }
    $script:checks++
}
# Only Git discovery is mocked. The production preflight never invokes Node/Bruno.
function git {
    $global:LASTEXITCODE = 0
    if ($args -contains '--show-toplevel') { return $testRoot }
    if ($args -contains 'HEAD') { return ('a' * 40) }
    if ($args -contains 'status') { return }
    throw 'Unexpected Git command in offline preflight.'
}
function Invoke-Check([hashtable]$Overrides = @{}) {
    $parameters = @{
        ExpectedWorktreeRoot = $testRoot
        RequestPaths = @('tests/Bruno/collections/slice/read.bru')
        CliEntryPath = (Join-Path $testRoot 'cli/bin/bru.js')
    }
    foreach ($key in $Overrides.Keys) { $parameters[$key] = $Overrides[$key] }
    & (Join-Path $testRoot 'docs/tools/Invoke-BrunoPreflight.ps1') @parameters
}
function Assert-Rejected([string]$Name, [hashtable]$Overrides, [string]$Message) {
    $caught = $null
    try { Invoke-Check $Overrides | Out-Null } catch { $caught = $_.Exception.Message }
    Assert-Check ($null -ne $caught -and $caught -match $Message) $Name
}

try {
    Write-Fixture 'docs/tools/Invoke-BrunoPreflight.ps1' ([System.IO.File]::ReadAllText((Join-Path $sourceRoot 'docs/tools/Invoke-BrunoPreflight.ps1')))
    Write-Fixture 'tests/Bruno/environments/local.bru' "vars {`n secret: NEVER_PRINT_THIS_SECRET`n}"
    Write-Fixture 'cli/package.json' '{"name":"@usebruno/cli","version":"4.2.0"}'
    Write-Fixture 'cli/bin/bru.js' 'throw new Error("CLI MUST NOT BE EXECUTED");'
    $request = @'
get {
  url: {{baseUrl}}/api/v1/example
  body: none
  auth: none
}
script:pre-request {
  if (bru.getEnvVar("baseUrl") !== "http://localhost:55819") throw new Error("wrong target");
}
'@
    Write-Fixture 'tests/Bruno/collections/slice/read.bru' $request
    $json = Invoke-Check
    $manifest = $json | ConvertFrom-Json
    Assert-Check ($manifest.Head -eq ('a' * 40)) 'Pinned revision'
    Assert-Check ($manifest.RuntimeTargetVerified -eq $false) 'Does not claim runtime verification'
    Assert-Check ($manifest.CliVersion -eq '4.2.0') 'Installed package version'
    Assert-Check ($manifest.RequestPaths[0] -eq 'collections/slice/read.bru') 'Bruno-relative request'
    Assert-Check ($manifest.BrunoWorkingDirectory -eq (Join-Path $testRoot 'tests/Bruno')) 'Pinned Bruno working directory'
    Assert-Check ($manifest.SafeDirectNodeCommand -match '--bail' -and $manifest.SafeDirectNodeCommand -match 'appEnvironment=Development') 'Explicit fail-fast command'
    Assert-Check ($json -notmatch 'NEVER_PRINT_THIS_SECRET') 'Environment values not emitted'
    Assert-Rejected 'Wrong checkout' @{ ExpectedWorktreeRoot = $sourceRoot } 'worktree root'
    Assert-Rejected 'Different guarded port' @{ BaseUrl = 'http://localhost:55820' } 'guard incompatible'
    Assert-Rejected 'Remote target' @{ BaseUrl = 'https://example.com' } 'loopback'
    Assert-Rejected 'Embedded credentials' @{ BaseUrl = 'http://name:secret@localhost:55819' } 'loopback'
    Assert-Rejected 'Query target' @{ BaseUrl = 'http://localhost:55819/?token=secret' } 'loopback'
    Assert-Rejected 'Directory selection' @{ RequestPaths = @('tests/Bruno/collections/slice') } 'not a .bru'
    Assert-Rejected 'Traversal outside collection' @{ RequestPaths = @('tests/Bruno/collections/../../../AGENTS.md') } 'outside'
    Assert-Rejected 'Absent request' @{ RequestPaths = @('tests/Bruno/collections/slice/missing.bru') } 'not found'
    Assert-Rejected 'Absent environment' @{ EnvironmentName = 'local-fixtures' } 'environment file'
    Write-Fixture 'tests/Bruno/collections/slice/meta.bru' "meta {`n name: folder`n}"
    Assert-Rejected 'Metadata is not a request' @{ RequestPaths = @('tests/Bruno/collections/slice/meta.bru') } 'one HTTP request'

    Write-Fixture 'tests/Bruno/collections/slice/upload.bru' "post {`n url: {{baseUrl}}/upload`n body: multipart-form`n}`nbody:multipart-form {`n file: @fixture.png`n}"
    Assert-Rejected 'Known multipart trap' @{ RequestPaths = @('tests/Bruno/collections/slice/upload.bru') } 'multipart-form method trap'
    Write-Fixture 'tests/Bruno/collections/slice/upload.bru' "post {`n url: {{baseUrl}}/upload`n body: multipartForm`n}`nbody:multipart-form {`n file: @fixture.png`n}"
    $multipart = Invoke-Check @{ RequestPaths = @('tests/Bruno/collections/slice/upload.bru') } | ConvertFrom-Json
    Assert-Check ($multipart.RequestPaths.Count -eq 1) 'Correct multipart selector and field block accepted'
    Write-Fixture 'tests/Bruno/collections/slice/literal.bru' "get {`n url: http://localhost:5235/api/v1/example`n body: none`n}"
    Assert-Rejected 'Hardcoded other listener' @{ RequestPaths = @('tests/Bruno/collections/slice/literal.bru') } 'hardcoded URL incompatible'

    $quotedPath = 'tests/Bruno/collections/slice/owner''s $request.bru'
    Write-Fixture $quotedPath $request
    $quoted = Invoke-Check @{ RequestPaths = @($quotedPath, 'tests/Bruno/collections/slice/read.bru') } | ConvertFrom-Json
    Assert-Check ($quoted.RequestPaths[0] -eq 'collections/slice/owner''s $request.bru' -and $quoted.RequestPaths[1] -eq 'collections/slice/read.bru') 'Order and literal special characters preserved'
    Assert-Check ($quoted.SafeDirectNodeCommand.Contains("'collections/slice/owner''s `$request.bru'")) 'Single-quoted generated argument'
    $parseTokens = $null; $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseInput($quoted.SafeDirectNodeCommand, [ref]$parseTokens, [ref]$parseErrors) | Out-Null
    Assert-Check ($parseErrors.Count -eq 0) 'Generated command parses without execution'
    Write-Fixture 'cli/package.json' '{"name":"not-bruno","version":"4.2.0"}'
    Assert-Rejected 'Wrong CLI package' @{} 'not @usebruno/cli'
    Write-Output "Bruno preflight: $checks checks passed; no API, database or Bruno CLI invoked."
}
finally {
    $tempParent = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($testRoot.StartsWith($tempParent, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $testRoot) -like 'sanad-bruno-preflight-*' -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
