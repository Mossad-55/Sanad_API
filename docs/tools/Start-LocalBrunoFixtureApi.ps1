[CmdletBinding()]
param(
    [ValidateRange(1, 65535)]
    [int]$Port = 55819,
    [switch]$SkipTestUserSeed,
    [switch]$SkipFinanceMigrations,
    [switch]$ReuseServiceIconFixture
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$apiProject = Join-Path $repoRoot 'src/API/Sanad.API/Sanad.API.csproj'
$localEnvironment = Join-Path $repoRoot 'tests/Bruno/environments/local.bru'
$fixtureEnvironment = Join-Path $repoRoot 'tests/Bruno/environments/local-fixtures.bru'

if (-not (Test-Path -LiteralPath $apiProject)) {
    throw 'Run this script from the Sanad API repository checkout.'
}
if (-not (Test-Path -LiteralPath $localEnvironment)) {
    throw 'The Bruno local environment file was not found; fixture environment was not created.'
}

function Assert-FixturePortAvailable([int]$ListenPort) {
    $listeners = @(
        [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $ListenPort),
        [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::IPv6Loopback, $ListenPort)
    )
    try {
        foreach ($listener in $listeners) {
            try {
                if ($listener.Server.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetworkV6) {
                    $listener.Server.DualMode = $false
                }
                $listener.Start()
            }
            catch {
                throw "Fixture API port $ListenPort is already listening or unavailable; select a different localhost port before starting the fixture API."
            }
        }
    }
    finally {
        foreach ($listener in $listeners) {
            $listener.Stop()
        }
    }
}

Assert-FixturePortAvailable $Port

$fixtureRunId = [Guid]::NewGuid().ToString('N').Substring(0, 12)
$fixtureHmacSecret = 'sanad-local-fixture-hmac-v1-never-live'

$testUserSeedEnabled = -not $SkipTestUserSeed
$financeMigrationsEnabled = -not $SkipFinanceMigrations
Write-Host 'This starts the Development API against the guarded local fixture database.'
Write-Host "Test-user seeding: $testUserSeedEnabled; Finance migrations: $financeMigrationsEnabled."
Write-Host 'The API guard validates the exact localhost:5432/SanadBrunoTestDb target before creating that database or applying migrations.'
$uppercase = 'ABCDEFGHJKLMNPQRSTUVWXYZ'
$lowercase = 'abcdefghijkmnopqrstuvwxyz'
$digits = '23456789'
$alphabet = $uppercase + $lowercase + $digits
$random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
function Get-FixtureRandomIndex($generator, [int]$maximum) {
    $bytes = [byte[]]::new(4)
    $generator.GetBytes($bytes)
    return [int]([BitConverter]::ToUInt32($bytes, 0) % [uint32]$maximum)
}
$passwordCharacters = [System.Collections.Generic.List[char]]::new()
$passwordCharacters.Add($uppercase[(Get-FixtureRandomIndex $random $uppercase.Length)])
$passwordCharacters.Add($lowercase[(Get-FixtureRandomIndex $random $lowercase.Length)])
$passwordCharacters.Add($digits[(Get-FixtureRandomIndex $random $digits.Length)])
for ($index = 0; $index -lt 21; $index++) {
    $passwordCharacters.Add($alphabet[(Get-FixtureRandomIndex $random $alphabet.Length)])
}
for ($index = $passwordCharacters.Count - 1; $index -gt 0; $index--) {
    $swapIndex = Get-FixtureRandomIndex $random ($index + 1)
    $swap = $passwordCharacters[$index]
    $passwordCharacters[$index] = $passwordCharacters[$swapIndex]
    $passwordCharacters[$swapIndex] = $swap
}
$elderlyPassword = -join $passwordCharacters
$random.Dispose()
$serviceIconFile = Join-Path $repoRoot 'tests/Bruno/service-icon-fixture.png'
$serviceIconBase64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/F5sAAAAASUVORK5CYII='
$serviceIconCreated = $false
if (Test-Path -LiteralPath $serviceIconFile) {
    if (-not $ReuseServiceIconFixture) {
        throw 'The reserved Bruno service-icon fixture path already exists; pass -ReuseServiceIconFixture only to reuse the exact generated disposable image.'
    }
    $existingServiceIconBase64 = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($serviceIconFile))
    if ($existingServiceIconBase64 -cne $serviceIconBase64) {
        throw 'The existing protected Bruno image is not the exact generated disposable fixture; refusing to use or overwrite it.'
    }
}
else {
    [System.IO.File]::WriteAllBytes($serviceIconFile, [Convert]::FromBase64String($serviceIconBase64))
    $serviceIconCreated = $true
}

$localBrunoText = Get-Content -LiteralPath $localEnvironment -Raw
$fixtureVariables = @"
  appEnvironment: Development
  smsSenderMode: development-noop
  elderlyPhone: +201000000005
  elderlyPassword: $elderlyPassword
  companionCaregiverEmail: companion.caregiver@test.sanad.local
  companionCaregiverPassword: Test-1234!
  supportAdminEmail: support.admin@test.sanad.local
  supportAdminPassword: Test-1234!
  adminEmail: content.admin@test.sanad.local
  adminPassword: Test-1234!
  superAdminEmail: elderly.welcome.admin@test.sanad.local
  superAdminPassword: Welcome-Admin-1234!
  subscriptionFreeEmail: family.subscription-free.$fixtureRunId@test.sanad.local
  renewalEmail: family.renewal.$fixtureRunId@test.sanad.local
  planChangeEmail: family.plan-change.$fixtureRunId@test.sanad.local
  subscriptionFreePhone: +201000000012
  renewalPhone: +201000000013
  planChangePhone: +201000000014
  fixtureRunId: $fixtureRunId
  paymobFixtureHmacSecret: $fixtureHmacSecret
"@
$fixtureText = [regex]::Replace(
    $localBrunoText,
    '(?m)^vars\s*\{',
    [System.Text.RegularExpressions.MatchEvaluator]{ param($match) $match.Value + "`r`n" + $fixtureVariables.TrimEnd() },
    1)
$fixtureText = [regex]::Replace(
    $fixtureText,
    '(?m)^\s*baseUrl\s*:\s*.*$',
    "  baseUrl: http://localhost:$Port",
    1)
$fixtureText = [regex]::Replace(
    $fixtureText,
    '(?m)^\s*adminEmail\s*:\s*.*$',
    '  adminEmail: content.admin@test.sanad.local')
$fixtureText = [regex]::Replace(
    $fixtureText,
    '(?m)^\s*adminPassword\s*:\s*.*$',
    '  adminPassword: Test-1234!')
if ($fixtureText -eq $localBrunoText) {
    throw 'Could not locate the Bruno vars block; no fixture environment was written.'
}
[System.IO.File]::WriteAllText(
    $fixtureEnvironment,
    $fixtureText,
    [System.Text.UTF8Encoding]::new($false))

$environmentNames = @(
    'DOTNET_ENVIRONMENT',
    'ASPNETCORE_ENVIRONMENT',
    'ASPNETCORE_URLS',
    'App__TestUserSeed__Enabled',
    'App__TestUserSeed__RunId',
    'App__TestUserSeed__Password',
    'App__TestUserSeed__ElderlyPassword',
    'Identity__AdminSeed__ArabicFullName',
    'Identity__AdminSeed__EnglishFullName',
    'Identity__AdminSeed__Email',
    'Identity__AdminSeed__PhoneNumber',
    'Identity__AdminSeed__Password',
    'Identity__Sms__SmsMisr__Username',
    'Identity__Sms__SmsMisr__Password',
    'Identity__Sms__SmsMisr__Sender',
    'Paymob__HmacSecret',
    'Paymob__SecretKey',
    'FinanceMigrations__ApplyOnStartup'
)
$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

try {
    [Environment]::SetEnvironmentVariable('DOTNET_ENVIRONMENT', 'Development', 'Process')
    [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Development', 'Process')
    [Environment]::SetEnvironmentVariable('ASPNETCORE_URLS', "http://localhost:$Port", 'Process')
    [Environment]::SetEnvironmentVariable('App__TestUserSeed__Enabled', $testUserSeedEnabled.ToString().ToLowerInvariant(), 'Process')
    [Environment]::SetEnvironmentVariable('App__TestUserSeed__RunId', $fixtureRunId, 'Process')
    [Environment]::SetEnvironmentVariable('App__TestUserSeed__Password', 'Test-1234!', 'Process')
    [Environment]::SetEnvironmentVariable('Identity__AdminSeed__ArabicFullName', 'Ù…Ø³Ø¤ÙˆÙ„ Ø§Ù„Ø§Ø®ØªØ¨Ø§Ø±', 'Process')
    [Environment]::SetEnvironmentVariable('Identity__AdminSeed__EnglishFullName', 'Elderly Welcome Test Admin', 'Process')
    [Environment]::SetEnvironmentVariable('Identity__AdminSeed__Email', 'elderly.welcome.admin@test.sanad.local', 'Process')
    [Environment]::SetEnvironmentVariable('Identity__AdminSeed__PhoneNumber', '+201000000008', 'Process')
    [Environment]::SetEnvironmentVariable('Identity__AdminSeed__Password', 'Welcome-Admin-1234!', 'Process')
    [Environment]::SetEnvironmentVariable('Identity__Sms__SmsMisr__Username', '', 'Process')
    [Environment]::SetEnvironmentVariable('Identity__Sms__SmsMisr__Password', '', 'Process')
    [Environment]::SetEnvironmentVariable('Identity__Sms__SmsMisr__Sender', '', 'Process')
    [Environment]::SetEnvironmentVariable('Paymob__HmacSecret', $fixtureHmacSecret, 'Process')
    [Environment]::SetEnvironmentVariable('Paymob__SecretKey', '', 'Process')
    [Environment]::SetEnvironmentVariable('FinanceMigrations__ApplyOnStartup', $financeMigrationsEnabled.ToString().ToLowerInvariant(), 'Process')
    [Environment]::SetEnvironmentVariable(
        'App__TestUserSeed__ElderlyPassword',
        $elderlyPassword,
        'Process')

    Push-Location $repoRoot
    try {
        dotnet run --no-build --no-launch-profile --project $apiProject
        if ($LASTEXITCODE -ne 0) {
            throw "The local fixture API exited with code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    foreach ($name in $environmentNames) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
    if ($serviceIconCreated -and (Test-Path -LiteralPath $serviceIconFile)) {
        Remove-Item -LiteralPath $serviceIconFile -Force
    }
}
$elderlyPassword = $null
$passwordCharacters.Clear()
