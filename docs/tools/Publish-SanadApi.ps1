[CmdletBinding()]
param(
    [string]$OutputPath,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$projectPath = Join-Path $repositoryRoot 'src\API\Sanad.API\Sanad.API.csproj'
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

New-Item -ItemType Directory -Path $resolvedOutputPath -Force | Out-Null

$publishArguments = @(
    'publish', $projectPath,
    '--configuration', $Configuration,
    '--output', $resolvedOutputPath,
    '--nologo'
)
if ($SkipBuild) {
    $publishArguments += '--no-build'
}

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$manifest = [ordered]@{
    application = 'Sanad.API'
    revision = $revision
    configuration = $Configuration
    publishedOnUtc = [DateTime]::UtcNow.ToString('O')
    requiredMigrations = [ordered]@{
        Identity = '20261005101531_AddFeedback'
        Cms = '20260928052717_AddElderlyWelcome'
        Caregivers = '20261002003508_AddCaregiverRatings'
        Families = '20261005111105_AddMedicalAccessGrantGrantee'
        CareHomes = '20261004150000_AddFamilyCheckInDisputeReason'
        Notifications = '20261002134931_AddEmailOutbox'
        Community = '20261005101553_InitialCommunity'
        Finance = '20261002210606_AddPlatformChargeRulesEfMetadata'
    }
    startupMigrationPolicy = 'All registered module migrations run before API startup outside Development; production cannot disable them.'
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $resolvedOutputPath 'sanad-deployment-manifest.json') -Encoding utf8

Write-Output "Published Sanad.API revision $revision to $resolvedOutputPath"
