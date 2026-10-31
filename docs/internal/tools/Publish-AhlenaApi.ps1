[CmdletBinding()]
param(
    [string]$OutputPath,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$projectPath = Join-Path $repositoryRoot 'src\API\Ahlena.API\Ahlena.API.csproj'
$OutputPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) { 'deploy\publish-out' } else { $OutputPath }
$resolvedOutputPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "API project was not found: $projectPath"
}

$safeRepositoryRoot = $repositoryRoot.Replace('\', '/')
$revision = (& git -c "safe.directory=$safeRepositoryRoot" -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($revision)) {
    throw 'The current Git revision could not be resolved.'
}

# Derive release heads from the checked-out migration sources. A hard-coded
# manifest can silently lag an accepted phase migration and make deployment
# verification compare the running database against the wrong requirement.
$requiredMigrations = [ordered]@{}
foreach ($module in @('Identity', 'Cms', 'Caregivers', 'Families', 'CareHomes', 'Notifications', 'Community', 'Finance', 'Chat')) {
    $migrationDirectory = Join-Path $repositoryRoot "src\Modules\$module\Infrastructure\Ahlena.Modules.$module.Infrastructure\Persistence\Migrations"
    if (-not (Test-Path -LiteralPath $migrationDirectory -PathType Container)) {
        throw "Migration directory was not found for $module."
    }

    $head = Get-ChildItem -LiteralPath $migrationDirectory -File |
        Where-Object { $_.Name -match '^\d{14}_[^.]+\.cs$' } |
        Sort-Object Name -Descending |
        Select-Object -First 1
    if ($null -eq $head) {
        throw "No migration source was found for $module."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $migrationDirectory "$($head.BaseName).Designer.cs") -PathType Leaf)) {
        throw "Latest migration designer was not found for $module."
    }
    $requiredMigrations[$module] = $head.BaseName
}

# Reusing an output directory can leave stale assemblies in the package.
# Restrict cleanup to a child of deploy/ and wait until manifest inputs validate.
$deployRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'deploy'))
$resolvedOutputParent = [System.IO.Path]::GetFullPath((Split-Path -Parent $resolvedOutputPath))
if (-not [string]::Equals($resolvedOutputParent, $deployRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Publish output must be a direct child of the repository deploy directory: $resolvedOutputPath"
}
if (Test-Path -LiteralPath $resolvedOutputPath) {
    $existingOutput = Get-Item -LiteralPath $resolvedOutputPath -Force
    if (($existingOutput.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Publish output cannot be a reparse point: $resolvedOutputPath"
    }
    Remove-Item -LiteralPath $resolvedOutputPath -Recurse -Force
}

New-Item -ItemType Directory -Path $resolvedOutputPath -Force | Out-Null

$publishArguments = @(
    'publish', $projectPath,
    '--configuration', $Configuration,
    '--output', $resolvedOutputPath,
    '--nologo',
    '/p:TreatWarningsAsErrors=true'
)
if ($SkipBuild) {
    $publishArguments += '--no-build'
}

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$developmentSettingsPath = Join-Path $resolvedOutputPath 'appsettings.Development.json'
if (Test-Path -LiteralPath $developmentSettingsPath -PathType Leaf) {
    throw 'Development settings were found in the publish output; refusing to create a deployment manifest.'
}

$manifest = [ordered]@{
    application = 'Ahlena.API'
    revision = $revision
    configuration = $Configuration
    publishedOnUtc = [DateTime]::UtcNow.ToString('O')
    requiredMigrations = $requiredMigrations
    startupMigrationPolicy = 'All registered module migrations run before API startup outside Development; production cannot disable them.'
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $resolvedOutputPath 'ahlena-deployment-manifest.json') -Encoding utf8

Write-Output "Published Ahlena.API revision $revision to $resolvedOutputPath"
