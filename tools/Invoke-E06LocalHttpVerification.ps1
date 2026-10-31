#requires -Version 7.0
<#+
Persistent, INSERT-only E06 fixtures; localhost HTTP checks. No migrations or resets.
Reuses the L1 HTTP/runtime library and F6's connection/psql guards as libraries.
Dependency failures and exceptional DST boundaries are proved by focused unit tests;
this script does not sabotage a shared database to manufacture a 503 response.
#>
[CmdletBinding()]
param([string]$BaseUrl = 'http://localhost:5249', [string]$FixturePassword = 'Test-1234!')
$ErrorActionPreference = 'Stop'
$e06Url = $BaseUrl
$e06Password = $FixturePassword
. "$PSScriptRoot/Invoke-F6LocalHttpVerification.ps1" -LibraryOnly
$baseUrl = $e06Url
$null = Assert-LoopbackBaseUrl $baseUrl
$root = Get-RepositoryRoot
$apiDirectory = Join-Path $root 'src/API/Ahlena.API'
$assembly = Join-Path $apiDirectory 'bin/Release/net10.0/Ahlena.API.dll'
$environment = Get-LocalFixtureEnvironment -Password $e06Password
$verificationEnvironment = Get-LocalFixtureEnvironment -Password $e06Password -EnableSeed
$preflight = Invoke-TargetVerification -RepoRoot $root -AssemblyPath $assembly -Environment $verificationEnvironment -ApiDirectory $apiDirectory
$connection = ConvertFrom-F6PostgresConnectionString (Resolve-F6ConnectionStringSource -RepoRoot $root).Value
$psql = Get-F6PsqlPath
$guard = Test-F6FixtureTarget -VerificationPassed $preflight.passed -Report $preflight.report -Connection $connection -PsqlPath $psql
if (-not $preflight.passed -or -not $guard.Ok) { throw ('E06 target verification failed; nothing written: ' + ($guard.Problems -join '; ')) }
if (Test-BaseUrlReachable -Url $baseUrl) { throw 'E06 port is occupied; stop its prior owned process before running.' }

function Invoke-E06Sql([string]$Sql) {
    $result = Invoke-F6Psql -PsqlPath $psql -Connection $connection -Sql $Sql -Tag 'e06' -ExpectJson
    if (-not $result.Ok) { throw $result.ErrorSummary }
    return $result.Value
}

$admin = '019a2e06-0001-7000-8000-000000000001'
$denied = '019a2e06-0001-7000-8000-000000000002'
$identity = '019a2e06-0001-7000-8000-000000000003'
$family = '019a2e06-0002-7000-8000-000000000001'
$elderly = '019a2e06-0003-7000-8000-000000000001'
$unresolved = '019a2e06-0003-7000-8000-000000000002'
$sql = @"
BEGIN;
INSERT INTO identity.users (id, arabic_full_name, english_full_name, phone_number, email,
 email_verified, phone_verified, status, created_on_utc, updated_on_utc, password_hash)
SELECT f.id::uuid, 'E06 synthetic', 'E06 synthetic', f.phone, f.email,
 true, true, 2, now(), now(), source.password_hash
FROM (VALUES ('$admin', '+201000000081', 'support.e06@test.sanad.local'),
 ('$denied', '+201000000082', 'content.e06@test.sanad.local'),
 ('$identity', '+201000000083', 'elderly.e06@test.sanad.local')) f(id,phone,email)
CROSS JOIN identity.users source WHERE source.email='content.admin@test.sanad.local'
AND NOT EXISTS (SELECT 1 FROM identity.users u WHERE u.id=f.id::uuid);
INSERT INTO identity.user_accounts (id,user_id,account_type,created_on_utc)
SELECT f.id::uuid, f.user_id::uuid, f.role, now() FROM (VALUES
 ('019a2e06-0004-7000-8000-000000000001','$admin',7),
 ('019a2e06-0004-7000-8000-000000000002','$denied',6),
 ('019a2e06-0004-7000-8000-000000000003','$identity',4)) f(id,user_id,role)
WHERE NOT EXISTS (SELECT 1 FROM identity.user_accounts a WHERE a.id=f.id::uuid);
INSERT INTO families.families (id,owner_user_id,name,created_on_utc,updated_on_utc)
SELECT '$family','$admin','E06 synthetic','2026-01-01Z','2026-01-01Z'
WHERE NOT EXISTS (SELECT 1 FROM families.families WHERE id='$family');
INSERT INTO families.elderlies (id,owner_user_id,identity_user_id,family_id,
 arabic_full_name,english_full_name,gender,date_of_birth,time_zone_id,relationship_type,created_on_utc,updated_on_utc)
SELECT f.id::uuid,'$admin',f.identity_id::uuid,'$family','E06 synthetic','E06 synthetic',1,
 '1950-01-01','Etc/UTC',1,'2026-01-01Z','2026-01-01Z'
FROM (VALUES ('$elderly','$identity'),('$unresolved','019a2e06-0099-7000-8000-000000000099')) f(id,identity_id)
WHERE NOT EXISTS (SELECT 1 FROM families.elderlies e WHERE e.id=f.id::uuid);
INSERT INTO families.elderly_check_ins (id,elderly_id,local_date,answer,answered_at_local_time,answered_on_utc,answered_by_user_id)
SELECT f.id::uuid,'$elderly',f.day::date,f.answer,'08:00'::time,'2026-01-04T08:00:00Z','$identity'
FROM (VALUES ('019a2e06-0005-7000-8000-000000000001','2026-01-02',true),
 ('019a2e06-0005-7000-8000-000000000002','2026-01-03',false)) f(id,day,answer)
WHERE NOT EXISTS (SELECT 1 FROM families.elderly_check_ins c WHERE c.id=f.id::uuid);
COMMIT;
SELECT json_build_object('identities',(SELECT count(*) FROM identity.users WHERE id IN ('$admin','$denied','$identity')),
 'elderlies',(SELECT count(*) FROM families.elderlies WHERE id IN ('$elderly','$unresolved')));
"@
$state = Invoke-E06Sql $sql
if ($state.identities -ne 3 -or $state.elderlies -ne 2) { throw 'E06 fixture readback failed.' }
$client = $null
$process = $null
$tokens = @{}
$checks = [System.Collections.Generic.List[object]]::new()
$failure = $null
$logs = @()
function Test-E06Http([string]$Name,[string]$Path,[int]$Status,[string]$Actor='admin',[scriptblock]$Assert=$null,[string]$Method='GET') {
    $check = @{Name=$Name;Method=$Method;Path=$Path;ExpectedStatus=$Status;Expected="HTTP $Status";Actor=$Actor;Token=$tokens[$Actor]}
    if ($Assert) { $check.Assert=$Assert }
    $result = @(Invoke-PlannedChecks -Client $client -Url $baseUrl -Plan @($check) -LogPath $logs)[0]
    $checks.Add($result)
    if (-not $result.passed) { throw "E06 check failed: $Name $Method $Path expected=$Status actual=$($result.actual) trace=$($result.traceId)" }
}
try {
    $logDir = Join-Path $env:TEMP ('opencode/e06-' + [guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $logDir
    $logs = @((Join-Path $logDir 'stdout.log'),(Join-Path $logDir 'stderr.log'))
    $process = Start-FixtureApiProcess -AssemblyPath $assembly -WorkingDirectory $apiDirectory -Url $baseUrl -Environment $environment -OutputLogPath $logs[0] -ErrorLogPath $logs[1]
    if (-not (Wait-ForEndpoint -Url $baseUrl -TimeoutSeconds 90 -Process $process)) { throw 'E06 API startup failed.' }
    $client = New-AhlenaHttpClient
    foreach ($actor in @('admin','denied')) {
        $email = if ($actor -eq 'admin') {'support.e06@test.sanad.local'} else {'content.e06@test.sanad.local'}
        $tokens[$actor] = Get-FixtureToken -Client $client -Url $baseUrl -Email $email -Password $e06Password -DeviceName "e06-$actor"
    }
    $route = '/api/v1/admin/elderly/check-ins/missed'
    $query = "?startDate=2026-01-01&endDate=2026-01-04&elderlyId=$elderly"
    Test-E06Http 'auth.anonymous' ($route+$query) 401 'anonymous'
    Test-E06Http 'role.content-denied' ($route+$query) 403 'denied'
    Test-E06Http 'invalid.missing-dates' $route 400
    Test-E06Http 'invalid.missing-start' ($route+'?endDate=2026-01-04') 400
    Test-E06Http 'invalid.reversed' ($route+'?startDate=2026-01-04&endDate=2026-01-01') 400
    Test-E06Http 'invalid.32-days' ($route+'?startDate=2026-01-01&endDate=2026-02-01') 400
    Test-E06Http 'invalid.date-overflow' ($route+'?startDate=9999-12-31&endDate=9999-12-31') 400
    Test-E06Http 'read.missing-day-and-minimal-shape' ($route+$query) 200 'admin' {
        param($status,$body)
        $issues=@()
        if ($body.totalCount -ne 1 -or @($body.items).Count -ne 1) {$issues+='Expected exactly one missing day after excluding creation day and both saved answers.'}
        if ($body.items[0].localDate -ne '2026-01-04') {$issues+='Wrong missing day.'}
        if (($body.items[0].PSObject.Properties.Name | Sort-Object) -join ',' -ne 'dueOnUtc,elderlyId,familyId,localDate') {$issues+='Item projection differs from minimal contract.'}
        if (-not $body.evaluationOnUtc) {$issues+='Missing evaluation instant.'}
        @{Actual="count=$($body.totalCount)";Problems=$issues}
    }
    Test-E06Http 'read.second-page' ($route+$query+'&page=2&pageSize=1') 200 'admin' {
        param($s,$b) @{Actual="count=$($b.totalCount) items=$(@($b.items).Count)";Problems=@(if ($b.totalCount -ne 1 -or @($b.items).Count -ne 0) {'Paging/count mismatch.'})}
    }
    Test-E06Http 'read.large-page' ($route+$query+'&page=2147483647&pageSize=100') 200 'admin' {
        param($s,$b) @{Actual="items=$(@($b.items).Count)";Problems=@(if (@($b.items).Count -ne 0) {'Large page overflowed.'})}
    }
    Test-E06Http 'read.unknown-empty' ($route+'?startDate=2026-01-01&endDate=2026-01-31&elderlyId=019a2e06-9999-7000-8000-000000000099') 200 'admin' {
        param($s,$b) @{Actual="count=$($b.totalCount)";Problems=@(if ($b.totalCount -ne 0) {'Unknown cohort must be empty.'})}
    }
    Test-E06Http 'read.unresolved-identity' ($route+"?startDate=2026-01-01&endDate=2026-01-04&elderlyId=$unresolved") 409 'admin' {
        param($s,$b) @{Actual="code=$($b.code)";Problems=@(if ($b.code -ne 'Families.AdminCheckIn.IndeterminateEvaluation') {'Wrong indeterminate error code.'})}
    }
}
catch { $failure = Get-TrimmedRedactedLine $_.Exception.Message }
finally {
    if ($client) {
        foreach ($actor in @($tokens.Keys)) {
            try { Test-E06Http "session.logout.$actor" '/api/v1/auth/sessions/logout' 204 $actor $null 'POST' }
            catch { if (-not $failure) {$failure = Get-TrimmedRedactedLine $_.Exception.Message} }
        }
        $client.Dispose()
    }
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}
$failed = @($checks | Where-Object {-not $_.passed}).Count
@{phase='E06';passed=($null -eq $failure -and $failed -eq 0);failure=$failure;
 results=@{total=$checks.Count;passed=$checks.Count-$failed;failed=$failed};checks=@($checks.ToArray());
 limitations=@('Audit/source-failure and DST branches use isolated focused tests; no shared-source outage was induced.','Trace correlation depends on captured logging.','Persistent synthetic rows retained; no migration/reset performed.')} | ConvertTo-Json -Depth 10
if ($failure -or $failed) {exit 1}
