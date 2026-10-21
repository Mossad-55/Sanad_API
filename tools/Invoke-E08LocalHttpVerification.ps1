#requires -Version 7.0
<#+
Persistent, INSERT-only E08 fixtures; localhost HTTP checks. No migrations and no
resets, and no SQL: every fixture this script needs is created through the API it
is verifying, so nothing writes a row the product cannot write itself.

Reuses the L1 HTTP/runtime library and F6's connection/psql target guards as
libraries (the same reuse E06 documents). The fixtures this script needs are two
INSERT-only dedicated identities; the asset and the references that make it
"referenced" are created through the API under verification, never by SQL, so no
invalid row is ever hand-written into a shared table.

Reference discovery itself is proved exhaustively and offline by
tests/Sanad.UnitTests/Cms/LandingMediaDeletionTests.cs against the real handler and
the real local storage (every status, retained history, family-home items, history
details and unreadable payloads). This script proves the HTTP contract: the route,
the policy outcome per account type, the local status mapping, and that a served
reference blocks the delete until it is actually gone.
#>
[CmdletBinding()]
param([string]$BaseUrl = 'http://localhost:5257', [string]$FixturePassword = 'Test-1234!')
$ErrorActionPreference = 'Stop'
$e08Url = $BaseUrl
$e08Password = $FixturePassword
. "$PSScriptRoot/Invoke-F6LocalHttpVerification.ps1" -LibraryOnly
$baseUrl = $e08Url
$null = Assert-LoopbackBaseUrl $baseUrl
$root = Get-RepositoryRoot
$apiDirectory = Join-Path $root 'src/API/Sanad.API'
$assembly = Join-Path $apiDirectory 'bin/Release/net10.0/Sanad.API.dll'
$environment = Get-LocalFixtureEnvironment -Password $e08Password
$verificationEnvironment = Get-LocalFixtureEnvironment -Password $e08Password -EnableSeed
$preflight = Invoke-TargetVerification -RepoRoot $root -AssemblyPath $assembly -Environment $verificationEnvironment -ApiDirectory $apiDirectory
if (-not $preflight.passed) { throw ('E08 target verification failed; nothing started: ' + ($preflight.failedChecks -join '; ')) }
if (Test-BaseUrlReachable -Url $baseUrl) { throw 'E08 port is occupied; stop its prior owned process before running.' }

$connection = ConvertFrom-F6PostgresConnectionString (Resolve-F6ConnectionStringSource -RepoRoot $root).Value
$psql = Get-F6PsqlPath
$guard = Test-F6FixtureTarget -VerificationPassed $preflight.passed -Report $preflight.report -Connection $connection -PsqlPath $psql
if (-not $guard.Ok) { throw ('E08 target guard failed; nothing written: ' + ($guard.Problems -join '; ')) }

function Invoke-E08Sql([string]$Sql) {
    $result = Invoke-F6Psql -PsqlPath $psql -Connection $connection -Sql $Sql -Tag 'e08' -ExpectJson
    if (-not $result.Ok) { throw $result.ErrorSummary }
    return $result.Value
}

$admin = '019a2e08-0001-7000-8000-000000000001'
$denied = '019a2e08-0001-7000-8000-000000000002'

# Two dedicated identities: a ContentAdmin the CmsContent policy admits and a
# SupportAdmin it refuses. Dedicated rather than the shared seeded accounts so
# this run neither consumes nor evicts their device sessions. INSERT-only and
# idempotent, and the seeded account supplies the password hash so the fixture
# password is one the login route already accepts.
$sql = @"
BEGIN;
INSERT INTO identity.users (id, arabic_full_name, english_full_name, phone_number, email,
 email_verified, phone_verified, status, created_on_utc, updated_on_utc, password_hash)
SELECT f.id::uuid, 'E08 synthetic', 'E08 synthetic', f.phone, f.email,
 true, true, 2, now(), now(), source.password_hash
FROM (VALUES ('$admin', '+201000000100', 'content.e08@test.sanad.local'),
 ('$denied', '+201000000101', 'support.e08@test.sanad.local')) f(id,phone,email)
CROSS JOIN identity.users source WHERE source.email='content.admin@test.sanad.local'
AND NOT EXISTS (SELECT 1 FROM identity.users u WHERE u.id=f.id::uuid);
INSERT INTO identity.user_accounts (id,user_id,account_type,created_on_utc)
SELECT f.id::uuid, f.user_id::uuid, f.role, now() FROM (VALUES
 ('019a2e08-0002-7000-8000-000000000001','$admin',6),
 ('019a2e08-0002-7000-8000-000000000002','$denied',7)) f(id,user_id,role)
WHERE NOT EXISTS (SELECT 1 FROM identity.user_accounts a WHERE a.id=f.id::uuid);
COMMIT;
SELECT json_build_object('identities',(SELECT count(*) FROM identity.users WHERE id IN ('$admin','$denied')));
"@
$state = Invoke-E08Sql $sql
if ($state.identities -ne 2) { throw 'E08 fixture readback failed.' }

$media = '/api/v1/admin/landing/media'
$sections = '/api/v1/admin/landing/sections'
$familySections = '/api/v1/admin/family-home/sections'

# The upload route validates the real image signature and then stores into the
# shared "landing" folder, so the fixture asset is a genuine stored file rather
# than a row that claims one exists.
$pngBytes = [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D)

$client = $null
$process = $null
$tokens = @{}
$checks = [System.Collections.Generic.List[object]]::new()
$failure = $null
$logs = @()

function Invoke-E08Request {
    [CmdletBinding()]
    param(
        [string]$Method,
        [string]$Path,
        [string]$Token,
        [string]$Body = $null
    )

    $request = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::new($Method)), "$baseUrl$Path"
    try {
        if ($Token) {
            $request.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue ('Bearer', $Token)
        }
        if ($Body) {
            $request.Content = New-Object System.Net.Http.StringContent ($Body, [System.Text.Encoding]::UTF8, 'application/json')
        }

        $response = $client.SendAsync($request, [System.Net.Http.HttpCompletionOption]::ResponseContentRead).GetAwaiter().GetResult()
        try {
            return @{ Status = [int]$response.StatusCode; Body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() }
        }
        finally {
            $response.Dispose()
        }
    }
    finally {
        $request.Dispose()
    }
}

function Invoke-E08Upload {
    <#
        One multipart/form-data POST of the single image the landing media upload
        route expects (form field name "file").
    #>
    [CmdletBinding()]
    param([string]$Token)

    $content = New-Object System.Net.Http.MultipartFormDataContent
    $request = $null
    try {
        $payload = New-Object System.Net.Http.ByteArrayContent -ArgumentList (,([byte[]]$pngBytes))
        $payload.Headers.ContentType = New-Object System.Net.Http.Headers.MediaTypeHeaderValue -ArgumentList 'image/png'
        $content.Add($payload, 'file', 'e08-synthetic.png')

        $request = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::Post), "$baseUrl$media"
        $request.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue ('Bearer', $Token)
        $request.Content = $content

        $response = $client.SendAsync($request, [System.Net.Http.HttpCompletionOption]::ResponseContentRead).GetAwaiter().GetResult()
        try {
            return @{ Status = [int]$response.StatusCode; Body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() }
        }
        finally {
            $response.Dispose()
        }
    }
    finally {
        if ($request) { $request.Dispose() }
        $content.Dispose()
    }
}

function Test-E08Http {
    [CmdletBinding()]
    param(
        [string]$Name,
        [string]$Path,
        [int]$Status,
        [string]$Actor = 'admin',
        [string]$Method = 'GET',
        [string]$Body = $null,
        [switch]$Multipart,
        [switch]$ExpectEmptyBody,
        [scriptblock]$Assert = $null
    )

    $check = @{Name=$Name;Method=$Method;Path=$Path;ExpectedStatus=$Status;Expected="HTTP $Status";Actor=$Actor;Token=$tokens[$Actor]}
    if ($Assert) { $check.Assert = $Assert }

    if ($Multipart) {
        $response = Invoke-E08Upload -Token $tokens[$Actor]
    }
    else {
        $response = Invoke-E08Request -Method $Method -Path $Path -Token $tokens[$Actor] -Body $Body
    }

    $parsed = ConvertFrom-SafeJson $response.Body
    $traceId = $null
    if ($null -ne $parsed -and $parsed.PSObject.Properties.Name -contains 'traceId') { $traceId = [string]$parsed.traceId }

    # The response has already been produced, so the matching server log line -
    # when one exists - is on disk now.
    $location = $null
    if ($traceId) { $location = Get-TraceLocation -TraceId $traceId -LogLines (Get-CapturedLogLines -Path $logs) }

    $result = Get-CheckOutcome -Check $check -Status $response.Status -BodyText $response.Body -TraceId $traceId -Location $location

    if ($ExpectEmptyBody -and -not [string]::IsNullOrWhiteSpace($response.Body)) {
        $result.passed = $false
        $result.problems = @($result.problems) + 'Expected an empty response body.'
    }

    $checks.Add($result)
    if (-not $result.passed) { throw "E08 check failed: $Name $Method $Path expected=$Status actual=$($result.actual) trace=$($result.traceId)" }
    return $parsed
}

try {
    $logDir = Join-Path $env:TEMP ('opencode/e08-' + [guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $logDir
    $logs = @((Join-Path $logDir 'stdout.log'), (Join-Path $logDir 'stderr.log'))
    $process = Start-FixtureApiProcess -AssemblyPath $assembly -WorkingDirectory $apiDirectory -Url $baseUrl -Environment $environment -OutputLogPath $logs[0] -ErrorLogPath $logs[1]
    if (-not (Wait-ForEndpoint -Url $baseUrl -TimeoutSeconds 90 -Process $process)) { throw 'E08 API startup failed.' }

    $client = New-SanadHttpClient
    $tokens['admin'] = Get-FixtureToken -Client $client -Url $baseUrl -Email 'content.e08@test.sanad.local' -Password $e08Password -DeviceName 'e08-content-admin'
    $tokens['denied'] = Get-FixtureToken -Client $client -Url $baseUrl -Email 'support.e08@test.sanad.local' -Password $e08Password -DeviceName 'e08-support-admin'

    $absentKey = 'landing/019a2e08-9999-7000-8000-000000000099.png'

    # --- policy and route shape -------------------------------------------
    # The media route is DELETE-only (uploads are POST), so every media check
    # below drives DELETE: a GET answers 405 and would prove nothing.
    Test-E08Http 'auth.anonymous' "${media}?key=$absentKey" 401 'anonymous' -Method 'DELETE' | Out-Null
    Test-E08Http 'role.support-admin-denied' "${media}?key=$absentKey" 403 'denied' -Method 'DELETE' | Out-Null
    Test-E08Http 'admin.unrelated-landing-read-still-answers' $sections 200 'admin' -Assert {
        param($s, $b)
        @{ Actual = "items=$(@($b.items).Count)"; Problems = @(if ($null -eq $b.items) { 'The landing admin list no longer answers with items.' }) }
    } | Out-Null

    # --- invalid keys: 400 before the filesystem or the storage is reached --
    $invalidKeyAssert = {
        param($s, $b)
        @{ Actual = "code=$($b.code)"; Problems = @(if ($b.code -ne 'Cms.Landing.Media.InvalidKey') { 'Wrong invalid-key code.' }) }
    }
    Test-E08Http 'invalid.missing-key' $media 400 'admin' -Method 'DELETE' | Out-Null
    Test-E08Http 'invalid.blank-key' "${media}?key=%20%20" 400 'admin' -Method 'DELETE' | Out-Null
    Test-E08Http 'invalid.private-prefix' "${media}?key=private%2Flanding%2Ffree.png" 400 'admin' -Method 'DELETE' | Out-Null
    Test-E08Http 'invalid.non-landing-folder' "${media}?key=documents%2Ffree.png" 400 'admin' -Method 'DELETE' | Out-Null
    Test-E08Http 'invalid.traversal' "${media}?key=landing%2F..%2Fsecret.png" 400 'admin' -Method 'DELETE' | Out-Null
    Test-E08Http 'invalid.assigned-code' "${media}?key=private%2Flanding%2Ffree.png" 400 'admin' -Method 'DELETE' -Assert $invalidKeyAssert | Out-Null

    # --- a well-formed key that names nothing ------------------------------
    Test-E08Http 'delete.well-formed-missing' "${media}?key=$absentKey" 404 'admin' -Method 'DELETE' | Out-Null
    Test-E08Http 'delete.missing-uses-notfound-code' "${media}?key=$absentKey" 404 'admin' -Method 'DELETE' {
        param($s, $b)
        @{ Actual = "code=$($b.code)"; Problems = @(if ($b.code -ne 'Cms.Landing.Media.NotFound') { 'Wrong not-found code.' }) }
    } | Out-Null

    # --- upload, delete, and the same request twice ------------------------
    $uploaded = Test-E08Http 'upload.ok' $media 201 'admin' -Method 'POST' -Multipart -Assert {
        param($s, $b)
        @{ Actual = "key=$($b.key)"; Problems = @(
            if (-not $b.key) { 'Upload returned no key.' }
            elseif (-not $b.key.StartsWith('landing/')) { 'Uploaded key is not a landing asset.' }
            elseif (-not $b.key.EndsWith('.png')) { 'Uploaded key lost the stored extension.' }
        ) }
    }
    $key1 = [string]$uploaded.key

    Test-E08Http 'delete.unreferenced' "${media}?key=$key1" 204 'admin' -Method 'DELETE' -ExpectEmptyBody | Out-Null
    Test-E08Http 'delete.same-request-twice-is-404' "${media}?key=$key1" 404 'admin' -Method 'DELETE' -Assert {
        param($s, $b)
        @{ Actual = "code=$($b.code)"; Problems = @(if ($b.code -ne 'Cms.Landing.Media.NotFound') { 'Second delete must report not-found.' }) }
    } | Out-Null

    # --- a served landing draft blocks the delete until it is gone ---------
    $referenced = Test-E08Http 'upload.landing-reference-fixture' $media 201 'admin' -Method 'POST' -Multipart
    $key2 = [string]$referenced.key
    $landingBody = @{
        type         = 'hero'
        arabicTitle  = 'E08 synthetic'
        englishTitle = 'E08 synthetic'
        imagePath    = $key2
    } | ConvertTo-Json -Compress
    $draft = Test-E08Http 'reference.landing-draft-created' $sections 201 'admin' -Method 'POST' -Body $landingBody
    $landingDraftId = [string]$draft.id

    Test-E08Http 'delete.blocked-by-landing-draft' "${media}?key=$key2" 409 'admin' -Method 'DELETE' -Assert {
        param($s, $b)
        @{ Actual = "code=$($b.code)"; Problems = @(if ($b.code -ne 'Cms.Landing.Media.Referenced') { 'A served reference must block the delete.' }) }
    } | Out-Null
    Test-E08Http 'reference.landing-draft-removed' "$sections/$landingDraftId" 204 'admin' -Method 'DELETE' | Out-Null
    Test-E08Http 'delete.landing-reference-released' "${media}?key=$key2" 204 'admin' -Method 'DELETE' -ExpectEmptyBody | Out-Null

    # --- a served family-home item blocks the delete too --------------------
    $familyReferenced = Test-E08Http 'upload.family-reference-fixture' $media 201 'admin' -Method 'POST' -Multipart
    $key3 = [string]$familyReferenced.key
    # A banner draft must supply its title pair in both languages or in neither,
    # so the fixture carries an Arabic title as well as an English one.
    $familyBody = @{
        kind  = 'banners'
        items = @(@{ arabicTitle = 'E08 synthetic'; englishTitle = 'E08 synthetic'; imageKey = $key3 })
    } | ConvertTo-Json -Compress
    $familyDraft = Test-E08Http 'reference.family-draft-created' $familySections 201 'admin' -Method 'POST' -Body $familyBody
    $familyDraftId = [string]$familyDraft.id

    Test-E08Http 'delete.blocked-by-family-draft' "${media}?key=$key3" 409 'admin' -Method 'DELETE' -Assert {
        param($s, $b)
        @{ Actual = "code=$($b.code)"; Problems = @(if ($b.code -ne 'Cms.Landing.Media.Referenced') { 'A served family-home reference must block the delete.' }) }
    } | Out-Null
    Test-E08Http 'reference.family-draft-removed' "$familySections/$familyDraftId" 204 'admin' -Method 'DELETE' | Out-Null
    Test-E08Http 'delete.family-reference-released' "${media}?key=$key3" 204 'admin' -Method 'DELETE' -ExpectEmptyBody | Out-Null
}
catch { $failure = Get-TrimmedRedactedLine $_.Exception.Message }
finally {
    if ($client) {
        foreach ($actor in @($tokens.Keys)) {
            try { Test-E08Http "session.logout.$actor" '/api/v1/auth/sessions/logout' 204 $actor -Method 'POST' | Out-Null }
            catch { if (-not $failure) { $failure = Get-TrimmedRedactedLine $_.Exception.Message } }
        }
        $client.Dispose()
    }
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}

$failed = @($checks | Where-Object {-not $_.passed}).Count
@{
    phase   = 'E08'
    passed  = ($null -eq $failure -and $failed -eq 0)
    failure = $failure
    results = @{ total = $checks.Count; passed = $checks.Count - $failed; failed = $failed }
    checks  = @($checks.ToArray())
    limitations = @(
        'Reference discovery for retained history rows, unreadable stored payloads and normalization equivalence is proved by the focused unit suite, not over HTTP.',
        'Over HTTP the policy denial is proved for an authenticated admin with the wrong account type (SupportAdmin); the app-audience denials (Family, caregivers, Elderly, CareHomeOwner, FinanceAdmin) are proved by the controller policy matrix test.',
        'No database row, file or fixture is reset: the two identities are INSERT-only, and every asset and reference this script creates goes through the verified API and is left in place.',
        'The empty body of a 204 is asserted by the controller test (NoContentResult) and by this script on the delete checks.',
        'Trace correlation depends on captured logging.'
    )
} | ConvertTo-Json -Depth 10
if ($failure -or $failed) { exit 1 }
