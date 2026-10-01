[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $repoRoot 'src/API/Sanad.API/Sanad.API.csproj'
$localEnvironment = Join-Path $repoRoot 'tests/Bruno/environments/local.bru'
$fixtureEnvironment = Join-Path $repoRoot 'tests/Bruno/environments/local-fixtures.bru'

if (-not (Test-Path -LiteralPath $apiProject)) {
    throw 'Run this script from the Sanad API repository checkout.'
}
if (-not (Test-Path -LiteralPath $localEnvironment)) {
    throw 'The Bruno local environment file was not found; fixture environment was not created.'
}

Write-Host 'This starts the Development API and seeds only localhost:5432/SanadBrunoTestDb.'
Write-Host 'The API guard validates every configured database before creating that database or applying migrations.'
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
if (Test-Path -LiteralPath (Join-Path $repoRoot 'tests/Bruno/service-icon-fixture.png')) {
    throw 'The reserved Bruno service-icon fixture path already exists; refusing to overwrite it.'
}
$serviceIconFile = Join-Path $repoRoot 'tests/Bruno/service-icon-fixture.png'
[System.IO.File]::WriteAllBytes(
    $serviceIconFile,
    [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/F5sAAAAASUVORK5CYII='))

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
"@
$fixtureText = [regex]::Replace(
    $localBrunoText,
    '(?m)^vars\s*\{',
    [System.Text.RegularExpressions.MatchEvaluator]{ param($match) $match.Value + "`r`n" + $fixtureVariables.TrimEnd() },
    1)
$fixtureText = [regex]::Replace(
    $fixtureText,
    '(?m)^\s*baseUrl\s*:\s*.*$',
    '  baseUrl: http://localhost:5235',
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
    'App__TestUserSeed__Enabled',
    'App__TestUserSeed__Password',
    'App__TestUserSeed__ElderlyPassword',
    'Identity__Sms__SmsMisr__Username',
    'Identity__Sms__SmsMisr__Password',
    'Identity__Sms__SmsMisr__Sender'
)
$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

try {
    [Environment]::SetEnvironmentVariable('DOTNET_ENVIRONMENT', 'Development', 'Process')
    [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Development', 'Process')
    [Environment]::SetEnvironmentVariable('App__TestUserSeed__Enabled', 'true', 'Process')
    [Environment]::SetEnvironmentVariable('App__TestUserSeed__Password', 'Test-1234!', 'Process')
    [Environment]::SetEnvironmentVariable('Identity__AdminSeed__ArabicFullName', 'Ù…Ø³Ø¤ÙˆÙ„ Ø§Ù„Ø§Ø®ØªØ¨Ø§Ø±', 'Process')
    [Environment]::SetEnvironmentVariable('Identity__AdminSeed__EnglishFullName', 'Elderly Welcome Test Admin', 'Process')
    [Environment]::SetEnvironmentVariable('Identity__AdminSeed__Email', 'elderly.welcome.admin@test.sanad.local', 'Process')
    [Environment]::SetEnvironmentVariable('Identity__AdminSeed__PhoneNumber', '+201000000008', 'Process')
    [Environment]::SetEnvironmentVariable('Identity__AdminSeed__Password', 'Welcome-Admin-1234!', 'Process')
    [Environment]::SetEnvironmentVariable('Identity__Sms__SmsMisr__Username', '', 'Process')
    [Environment]::SetEnvironmentVariable('Identity__Sms__SmsMisr__Password', '', 'Process')
    [Environment]::SetEnvironmentVariable('Identity__Sms__SmsMisr__Sender', '', 'Process')
    [Environment]::SetEnvironmentVariable(
        'App__TestUserSeed__ElderlyPassword',
        $elderlyPassword,
        'Process')

    Push-Location $repoRoot
    try {
        dotnet run --no-build --launch-profile http --project $apiProject
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
    if (Test-Path -LiteralPath $serviceIconFile) {
        Remove-Item -LiteralPath $serviceIconFile -Force
    }
}
$elderlyPassword = $null
$passwordCharacters.Clear()
