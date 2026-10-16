[CmdletBinding()]
param(
    [ValidateSet('Unit', 'Architecture')]
    [string]$Suite = 'Unit'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$project = if ($Suite -eq 'Unit') {
    'tests/Sanad.UnitTests/Sanad.UnitTests.csproj'
} else {
    'tests/Sanad.ArchitectureTests/Sanad.ArchitectureTests.csproj'
}

# Environment changes belong to this exact child, not another shell invocation,
# the calling shell, or the machine/user profile. Never print inherited values.
$gateStart = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
$gateStart.UseShellExecute = $false
$gateStart.CreateNoWindow = $true
$gateStart.RedirectStandardOutput = $true
$gateStart.RedirectStandardError = $true
$gateStart.WorkingDirectory = $repositoryRoot
foreach ($argument in @('test', $project, '--no-build', '--no-restore', '--nologo', '-v', 'minimal')) {
    $gateStart.ArgumentList.Add($argument)
}

$integrationKeys = @(
    'ConnectionStrings__IdentityIntegrationDatabase',
    'ConnectionStrings__FamiliesIntegrationDatabase',
    # FUR-02 disposable-PostgreSQL opt-in key. It does not match the
    # *IntegrationDatabase pattern above, so it is listed explicitly to keep
    # it out of the guarded test child environment (fail-closed below).
    'ConnectionStrings__Fur02VerificationDatabase'
)
foreach ($key in @($gateStart.Environment.Keys)) {
    if ($key -match '(?i)^ConnectionStrings__.*IntegrationDatabase$') {
        $gateStart.Environment.Remove($key) | Out-Null
    }
}
foreach ($key in $integrationKeys) {
    $gateStart.Environment.Remove($key) | Out-Null
}

$dummyConnection = 'Host=127.0.0.1;Port=1;Database=sanad_portal_gate_dummy;Username=test;Password=test;Timeout=1;Command Timeout=1'
$moduleNames = @('Identity', 'Cms', 'Caregivers', 'Families', 'CareHomes', 'Notifications', 'Community', 'Chat', 'Finance')
foreach ($moduleName in $moduleNames) {
    $gateStart.Environment["ConnectionStrings__${moduleName}Database"] = $dummyConnection
}
$gateStart.Environment['Database__ApplyMigrationsOnStartup'] = 'false'
$gateStart.Environment['FinanceMigrations__ApplyOnStartup'] = 'false'
$gateStart.Environment['App__TestUserSeed__Enabled'] = 'false'
$gateStart.Environment['App__Hc034FamilyFixture__Enabled'] = 'false'
$gateStart.Environment['DataProtection__KeyRingPath'] = ''
$gateStart.Environment['Storage__Local__RootPath'] = [System.IO.Path]::Combine(
    [System.IO.Path]::GetTempPath(), 'sanad-portal-gates', [Guid]::NewGuid().ToString('N'))

# Fail closed immediately before Process.Start. A later independent shell cannot
# accidentally undo this setup. Existing tests supply their synthetic providers
# and fixtures; this wrapper does not seed, migrate, reset, or connect to a DB.
foreach ($key in $integrationKeys) {
    if ($gateStart.Environment.ContainsKey($key)) {
        throw "Integration target still present in child environment: $key"
    }
}
foreach ($moduleName in $moduleNames) {
    if ($gateStart.Environment["ConnectionStrings__${moduleName}Database"] -ne $dummyConnection) {
        throw "Unsafe module target in child environment: $moduleName"
    }
}
Write-Output "Portal $Suite gate: integration targets absent; all nine module targets isolated; migrations/seeders disabled (child only)."
$gateProcess = [System.Diagnostics.Process]::Start($gateStart)
if ($null -eq $gateProcess) { throw 'Unable to start the isolated test process.' }
try {
    # Drain both streams concurrently to avoid a full-pipe deadlock and retain
    # the actual test counts in non-interactive shells.
    $gateOutput = $gateProcess.StandardOutput.ReadToEndAsync()
    $gateError = $gateProcess.StandardError.ReadToEndAsync()
    $gateProcess.WaitForExit()
    Write-Output ($gateOutput.GetAwaiter().GetResult())
    $gateErrorText = $gateError.GetAwaiter().GetResult()
    if (-not [string]::IsNullOrWhiteSpace($gateErrorText)) {
        [Console]::Error.Write($gateErrorText)
    }
    $gateExitCode = $gateProcess.ExitCode
} finally {
    $gateProcess.Dispose()
}
exit $gateExitCode
