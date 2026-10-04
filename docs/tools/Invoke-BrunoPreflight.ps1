[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string[]]$RequestPaths,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ExpectedWorktreeRoot,

    [string]$CliEntryPath,

    [ValidatePattern('^https?://')]
    [string]$BaseUrl = 'http://localhost:55819',

    [ValidateSet('local', 'local-fixtures')]
    [string]$EnvironmentName = 'local'
)

$ErrorActionPreference = 'Stop'

function Resolve-FullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath($Path).TrimEnd([char[]]@('\', '/'))
}

function Test-PathWithin([string]$Candidate, [string]$Root) {
    $candidateFull = Resolve-FullPath $Candidate
    $rootFull = Resolve-FullPath $Root
    return $candidateFull.Equals($rootFull, [System.StringComparison]::OrdinalIgnoreCase) -or
        $candidateFull.StartsWith($rootFull + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -or
        $candidateFull.StartsWith($rootFull + [System.IO.Path]::AltDirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-RelativeDisplayPath([string]$Path, [string]$Root) {
    $rootUri = New-Object System.Uri (($Root.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar))
    $pathUri = New-Object System.Uri ($Path)
    return [System.Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()).Replace('\', '/')
}

function Assert-NoReparsePointBetween([string]$Path, [string]$Root) {
    $rootFull = Resolve-FullPath $Root
    $currentPath = Resolve-FullPath $Path
    while ($currentPath) {
        $current = Get-Item -LiteralPath $currentPath -Force
        if ($current.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
            throw "Symlink/reparse-point path is not allowed: $Path"
        }
        if ($currentPath.Equals($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) { return }
        $parent = [System.IO.Directory]::GetParent($currentPath)
        $currentPath = if ($null -eq $parent) { $null } else { $parent.FullName }
    }
    throw "Request path is not rooted beneath tests/Bruno/collections: $Path"
}

$scriptRoot = Resolve-FullPath (Join-Path $PSScriptRoot '../..')
$expectedRoot = Resolve-FullPath $ExpectedWorktreeRoot
if (-not $scriptRoot.Equals($expectedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "ExpectedWorktreeRoot does not match this script's worktree root."
}

$collectionsRoot = Resolve-FullPath (Join-Path $scriptRoot 'tests/Bruno/collections')
$environmentPath = Resolve-FullPath (Join-Path $scriptRoot ("tests/Bruno/environments/{0}.bru" -f $EnvironmentName))
if (-not (Test-Path -LiteralPath $environmentPath -PathType Leaf)) {
    throw "The Bruno environment file for '$EnvironmentName' was not found."
}

try {
    $uri = [System.Uri]$BaseUrl
}
catch {
    throw 'BaseUrl must be a valid HTTP(S) URL.'
}
if (-not $uri.IsAbsoluteUri -or $uri.Scheme -notin @('http', 'https') -or
    $uri.Host -notin @('localhost', '127.0.0.1', '::1', '[::1]') -or $uri.UserInfo -or
    $uri.AbsolutePath -ne '/' -or $uri.Query -or $uri.Fragment) {
    throw 'BaseUrl must target a loopback HTTP(S) listener.'
}

$brunoRoot = Resolve-FullPath (Join-Path $scriptRoot 'tests/Bruno')
$selected = [System.Collections.Generic.List[string]]::new()
foreach ($requestPath in $RequestPaths) {
    if ([string]::IsNullOrWhiteSpace($requestPath) -or [System.IO.Path]::IsPathRooted($requestPath)) {
        throw "RequestPaths must be non-empty relative paths under tests/Bruno/collections: $requestPath"
    }
    $candidate = Resolve-FullPath (Join-Path $scriptRoot $requestPath)
    if (-not (Test-PathWithin $candidate $collectionsRoot) -or
        -not $candidate.EndsWith('.bru', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Request path is outside tests/Bruno/collections or is not a .bru file: $requestPath"
    }
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Request file was not found: $requestPath"
    }
    Assert-NoReparsePointBetween $candidate $scriptRoot
    $selected.Add((Get-RelativeDisplayPath $candidate $brunoRoot))
}

$gitSafeRoot = $scriptRoot.Replace('\', '/')
$gitConfigArg = "safe.directory=$gitSafeRoot"
$gitRoot = (& git '-c' $gitConfigArg '-C' $gitSafeRoot 'rev-parse' '--show-toplevel' 2>$null).Trim()
if ($LASTEXITCODE -ne 0 -or -not $gitRoot) {
    throw 'Unable to resolve the worktree root with git.'
}
if (-not (Resolve-FullPath $gitRoot).Equals($scriptRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Git resolved a different worktree root.'
}
$head = (& git '-c' $gitConfigArg '-C' $gitSafeRoot 'rev-parse' 'HEAD' 2>$null).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-fA-F]{40}$') {
    throw 'Unable to resolve the pinned git HEAD.'
}
$statusLines = @(& git '-c' $gitConfigArg '-c' 'core.excludesFile=' '-C' $gitSafeRoot 'status' '--short' 2>$null)
if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect the worktree status.' }
$dirtyPaths = @($statusLines | ForEach-Object { ($_ -replace '^..\s+', '') -replace '\\', '/' })

$nodeCommand = Get-Command node.exe -ErrorAction SilentlyContinue
if (-not $nodeCommand) { $nodeCommand = Get-Command node -ErrorAction SilentlyContinue }
if (-not $nodeCommand) { throw 'Node executable was not found on PATH.' }
$nodePath = Resolve-FullPath $nodeCommand.Source

if ($CliEntryPath) {
    $cliPath = Resolve-FullPath $CliEntryPath
    if (-not (Test-Path -LiteralPath $cliPath -PathType Leaf)) { throw 'CliEntryPath was not found.' }
} else {
    $bruCommand = Get-Command bru.cmd -ErrorAction SilentlyContinue
    if (-not $bruCommand) { throw 'bru.cmd was not found; provide CliEntryPath explicitly.' }
    $cliPath = Resolve-FullPath (Join-Path (Split-Path -Parent $bruCommand.Source) 'node_modules/@usebruno/cli/bin/bru.js')
    if (-not (Test-Path -LiteralPath $cliPath -PathType Leaf)) { throw 'The Bruno CLI entry point was not found beside bru.cmd.' }
}
$cliPackage = Join-Path (Split-Path -Parent (Split-Path -Parent $cliPath)) 'package.json'
if (-not (Test-Path -LiteralPath $cliPackage -PathType Leaf)) { throw 'The Bruno CLI package.json was not found.' }
$cliVersion = ([System.IO.File]::ReadAllText($cliPackage) | ConvertFrom-Json).version
if (-not $cliVersion) { throw 'The Bruno CLI package version is missing.' }
$cliPackageData = [System.IO.File]::ReadAllText($cliPackage) | ConvertFrom-Json
if ($cliPackageData.name -ne '@usebruno/cli') { throw 'The resolved CLI package is not @usebruno/cli.' }

$multipartTrap = @()
foreach ($relativePath in $selected) {
    $filePath = Join-Path $brunoRoot $relativePath
    $text = [System.IO.File]::ReadAllText($filePath)
    $methodBlocks = [regex]::Matches($text, '(?ms)^\s*(?:get|post|put|patch|delete|head|options)\s*\{(.*?)^\s*\}')
    if ($methodBlocks.Count -ne 1) {
        throw "Selected file must contain one HTTP request method block: $relativePath"
    }
    if ($cliVersion -match '^4\.2\.' -and $methodBlocks[0].Groups[1].Value -match '(?m)^\s*body:\s*multipart-form\s*$') {
        $multipartTrap += $relativePath
    }
    foreach ($urlMatch in [regex]::Matches($text, '(?im)^\s*url:\s*(\S+)')) {
        $literalUrl = $urlMatch.Groups[1].Value.Trim('"''')
        if ($literalUrl -match '^https?://') {
            $requestUri = [System.Uri]$literalUrl
            if ($requestUri.Scheme -ne $uri.Scheme -or $requestUri.Host -ne $uri.Host -or $requestUri.Port -ne $uri.Port) {
                throw "Selected request contains a hardcoded URL incompatible with BaseUrl: $relativePath"
            }
        }
    }
    foreach ($guardMatch in [regex]::Matches($text, '(?i)(?:baseUrl|baseURL)[^\r\n]{0,160}?[=!]==?\s*["''](https?://[^"'']+)["'']')) {
        if ($guardMatch.Groups[1].Value -ne $BaseUrl) {
            throw "Selected request contains a hardcoded baseUrl guard incompatible with BaseUrl: $relativePath"
        }
    }
}
if ($multipartTrap.Count -gt 0) {
    throw "Bruno CLI $cliVersion has the multipart-form method trap in: $($multipartTrap -join ', '). Use body: multipartForm; retain body:multipart-form field blocks."
}

function ConvertTo-SingleQuotedArgument([string]$Value) { return "'" + $Value.Replace("'", "''") + "'" }
$safeArgs = @('run') + $selected + @('--env', $EnvironmentName, '--env-var', ("baseUrl={0}" -f $BaseUrl), '--env-var', 'appEnvironment=Development', '--insecure', '--bail', '--reporter-skip-body')
$safeCommand = "Set-Location -LiteralPath $(ConvertTo-SingleQuotedArgument $brunoRoot); & $(ConvertTo-SingleQuotedArgument $nodePath) $(ConvertTo-SingleQuotedArgument $cliPath) " + (($safeArgs | ForEach-Object { ConvertTo-SingleQuotedArgument $_ }) -join ' ')
$result = [ordered]@{
    Worktree = $scriptRoot
    Head = $head
    Dirty = ($dirtyPaths.Count -gt 0)
    DirtyPaths = $dirtyPaths
    CliPath = $cliPath
    CliVersion = [string]$cliVersion
    NodePath = $nodePath
    Environment = $EnvironmentName
    BaseUrl = $BaseUrl
    RequestPaths = @($selected)
    BrunoWorkingDirectory = $brunoRoot
    ReadOnlyCaveat = 'Offline preflight only. It does not verify a listener, API, database, migrations, credentials, or runtime target.'
    RuntimeTargetVerified = $false
    RequiredBeforeRun = 'Verify the matching listener/worktree/database and that existing authorization covers the disposable target and startup effects. Request new approval only if required authority is missing.'
    SafeDirectNodeCommand = $safeCommand
}
$result | ConvertTo-Json -Depth 5
