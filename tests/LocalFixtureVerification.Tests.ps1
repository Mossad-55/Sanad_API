#requires -Version 5.1
<#
    Offline tests for tools/Invoke-LocalFixtureVerification.ps1.

    Runs the pure helper functions only: no API, no database, no HTTP request,
    no dotnet invocation. Mirrors the tests/BrunoPreflight.Tests.ps1 style so no
    test framework is added.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$checks = 0

. (Join-Path $repoRoot 'tools/Invoke-LocalFixtureVerification.ps1') -LibraryOnly

function Assert-Check([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "Failed: $Name" }
    $script:checks++
}

try {
    # ---------------- loopback base URL (no remote fallback) ----------------

    foreach ($accepted in @('http://localhost:5235', 'http://127.0.0.1:5235', 'https://localhost:7296', 'http://[::1]:5235')) {
        $uri = Assert-LoopbackBaseUrl $accepted
        Assert-Check ($null -ne $uri -and $uri.IsAbsoluteUri) "Accepted loopback base URL: $accepted"
    }

    $rejected = @(
        @{ Url = 'https://example.com'; Reason = 'remote host' },
        @{ Url = 'http://api.sanad.example/api'; Reason = 'non-loopback path' },
        @{ Url = 'http://user:secret@localhost:5235'; Reason = 'embedded credentials' },
        @{ Url = 'http://localhost:5235/extra'; Reason = 'path component' },
        @{ Url = 'http://localhost:5235/?token=secret'; Reason = 'query component' },
        @{ Url = 'http://localhost:5235/#fragment'; Reason = 'fragment' },
        @{ Url = 'http://0.0.0.0:5235'; Reason = 'wildcard bind address is not loopback' },
        @{ Url = 'http://192.168.1.10:5235'; Reason = 'private LAN address' },
        @{ Url = 'ftp://localhost:21'; Reason = 'non-HTTP scheme' },
        @{ Url = 'not-a-url'; Reason = 'not absolute' },
        @{ Url = ''; Reason = 'empty' }
    )
    foreach ($case in $rejected) {
        $caught = $null
        try { Assert-LoopbackBaseUrl $case.Url | Out-Null } catch { $caught = $_.Exception.Message }
        Assert-Check ($null -ne $caught -and $caught -match 'loopback|valid HTTP') "Rejected base URL ($($case.Reason))"
    }
    Assert-Check (Test-LoopbackHost 'localhost') 'localhost is loopback'
    Assert-Check (Test-LoopbackHost '[0000:0000:0000:0000:0000:0000:0000:0001]') 'Expanded IPv6 loopback is loopback'
    Assert-Check (-not (Test-LoopbackHost '0.0.0.0')) '0.0.0.0 is not loopback'
    Assert-Check (-not (Test-LoopbackHost '10.0.0.5')) 'Private address is not loopback'

    # ---------------- fixture environment ----------------

    $seedEnvironment = Get-LocalFixtureEnvironment -Password 'Test-1234!' -EnableSeed
    $serveEnvironment = Get-LocalFixtureEnvironment -Password 'Test-1234!'

    Assert-Check ($seedEnvironment['DOTNET_ENVIRONMENT'] -eq 'Development') 'Seed environment is Development'
    Assert-Check ($seedEnvironment['ASPNETCORE_ENVIRONMENT'] -eq 'Development') 'Seed ASPNETCORE environment is Development'
    Assert-Check ($seedEnvironment['App__TestUserSeed__Enabled'] -eq 'true') 'Seed environment enables the guarded seeder'
    Assert-Check ($seedEnvironment['App__TestUserSeed__TargetMode'] -eq 'PersistentLocal') 'Seed environment selects PersistentLocal'
    Assert-Check ($seedEnvironment['App__TestUserSeed__Scope'] -eq 'Minimal') 'Seed environment selects Minimal scope'
    Assert-Check ($seedEnvironment['Database__ApplyMigrationsOnStartup'] -eq 'false') 'Startup migrations are disabled in the seed environment'
    Assert-Check ($serveEnvironment['App__TestUserSeed__Enabled'] -eq 'false') 'Serving API never re-seeds'
    Assert-Check ($serveEnvironment['App__TestUserSeed__TargetMode'] -eq 'PersistentLocal') 'Serving API keeps the verified target mode'
    Assert-Check ($serveEnvironment['Database__ApplyMigrationsOnStartup'] -eq 'false') 'Startup migrations are disabled in the serving environment'
    Assert-Check ($serveEnvironment['App__TestUserSeed__ElderlyPassword'] -eq '') 'ElderlyPassword is cleared'

    $liveProviderNames = @(
        'Identity__Sms__SmsMisr__Username', 'Identity__Sms__SmsMisr__Password',
        'Identity__Sms__SmsMisr__Sender', 'Identity__Sms__SmsMisr__Environment',
        'Identity__Email__Smtp__Host', 'Identity__Email__Smtp__FromAddress',
        'Identity__Email__Smtp__FromName', 'Identity__Email__Smtp__Port',
        'Identity__Email__Smtp__Username', 'Identity__Email__Smtp__Password',
        'Identity__Email__Smtp__UseSsl',
        'Push__Firebase__ServiceAccountJson'
    )
    foreach ($name in $liveProviderNames) {
        Assert-Check ($seedEnvironment.Contains($name) -and $seedEnvironment[$name] -eq '') "Live provider input cleared in seed environment: $name"
        Assert-Check ($serveEnvironment.Contains($name) -and $serveEnvironment[$name] -eq '') "Live provider input cleared in serving environment: $name"
    }
    Assert-Check (-not $seedEnvironment.Contains('ConnectionStrings__IdentityDatabase')) 'The script never invents a connection string'
    $nonSyntheticPasswords = @(
        $seedEnvironment.Keys |
            Where-Object { $_ -like '*Password*' } |
            ForEach-Object { $seedEnvironment[$_] } |
            Where-Object { $_ -ne '' -and $_ -ne 'Test-1234!' }
    )
    Assert-Check ($nonSyntheticPasswords.Count -eq 0) 'Only the synthetic fixture password is set'

    # ---------------- redaction ----------------

    $redactedToken = Get-RedactedText '{"accessToken":"eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.abcdefghijklmno","refreshToken":"refresh-value"}'
    Assert-Check ($redactedToken -notmatch 'abcdefghijklmno' -and $redactedToken -notmatch 'refresh-value') 'Token values are redacted'
    Assert-Check ($redactedToken -match '<redacted>') 'Redaction marker is present'

    $redactedConnection = Get-RedactedText 'Host=localhost;Port=5432;Database=SanadDb;Username=someone;Password=pa55word'
    Assert-Check ($redactedConnection -notmatch 'pa55word' -and $redactedConnection -notmatch 'someone') 'Connection string credentials are redacted'

    $redactedKey = Get-RedactedText 'gateway key sk_live_abcdefghijklmno'
    Assert-Check ($redactedKey -notmatch 'abcdefghijklmno') 'Live-looking provider key is redacted'

    $redactedEgyptKey = Get-RedactedText 'gateway key egy_sk_test_abcdefghijklmno'
    Assert-Check ($redactedEgyptKey -notmatch 'abcdefghijklmno') 'New-format Paymob test key is redacted'

    $redactedEgyptPublicKey = Get-RedactedText 'public key egy_pk_test_abcdefghijklmno'
    Assert-Check ($redactedEgyptPublicKey -notmatch 'abcdefghijklmno') 'New-format Paymob public key is redacted'

    $redactedEgyptClientSecret = Get-RedactedText 'client secret egy_csk_test_abcdefghijklmno'
    Assert-Check ($redactedEgyptClientSecret -notmatch 'abcdefghijklmno') 'New-format Paymob client secret is redacted'

    $redactedBearer = Get-RedactedText 'Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.abcdefghijklmn'
    Assert-Check ($redactedBearer -notmatch 'eyJhbGciOiJIUzI1NiJ9') 'Bearer token is redacted'

    $redactedJwt = Get-RedactedText 'token=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.finalpartvalue'
    Assert-Check ($redactedJwt -notmatch 'finalpartvalue') 'Standalone JWT is redacted'

    Assert-Check ((Get-RedactedText 'plain 404 message') -eq 'plain 404 message') 'Ordinary text is unchanged'
    Assert-Check ([string]::IsNullOrEmpty((Get-RedactedText $null))) 'Null text yields no content'
    $long = Get-TrimmedRedactedLine ('x' * 500)
    Assert-Check ($long.Length -le 303 -and $long.EndsWith('...')) 'Long log lines are truncated'

    # ---------------- safe error summary (no payload echo) ----------------

    $validationSummary = Get-SafeErrorSummary (ConvertFrom-SafeJson '{"title":"Validation failed","status":400,"errors":{"status":["The value 999 is invalid"],"page":["bad"]},"traceId":"0HN1"}')
    Assert-Check ($validationSummary -match 'title=' -and $validationSummary -match 'fields=status,page') 'Field names are reported'
    Assert-Check ($validationSummary -notmatch '999') 'Field values are never reported'
    Assert-Check ((Get-SafeErrorSummary $null) -eq 'no-json-body') 'Missing body is reported without details'

    # ---------------- check plan ----------------

    $unknownGuid = '11111111-1111-1111-1111-111111111111'
    $plan = Get-LocalFixtureCheckPlan -ContentAdminToken 'admin-token' -FamilyViewerToken 'family-token' -UnknownGuid $unknownGuid
    $adminRoute = '/api/v1/admin/wellness-tips'

    Assert-Check ($plan.Count -eq 7) 'Plan contains the seven planned checks'
    Assert-Check (@($plan | Where-Object { $_.Method -ne 'GET' }).Count -eq 0) 'Every check is a read-only GET'
    Assert-Check (@($plan | Where-Object { $_.Name -eq 'success.content-admin-list' -and $_.Path -eq "${adminRoute}?page=1&pageSize=5" -and $_.ExpectedStatus -eq 200 }).Count -eq 1) 'Success check targets the existing list route'
    Assert-Check (@($plan | Where-Object { $_.Name -eq 'paging.page-size-clamp' -and $_.Path -eq "${adminRoute}?page=0&pageSize=500" }).Count -eq 1) 'Paging clamp check uses out-of-range paging'
    Assert-Check (@($plan | Where-Object { $_.Name -eq 'filter.status-draft' -and $_.Path -eq "${adminRoute}?status=1&page=1&pageSize=5" }).Count -eq 1) 'Filter check uses a defined enum value'
    Assert-Check (@($plan | Where-Object { $_.Name -eq 'unknown.guid-detail' -and $_.Path -eq "$adminRoute/$unknownGuid" -and $_.ExpectedStatus -eq 404 }).Count -eq 1) 'Unknown-id check targets an unknown detail id'
    Assert-Check (@($plan | Where-Object { $_.Name -eq 'invalid.enum-query' -and $_.Path -eq "${adminRoute}?status=999" -and $_.ExpectedStatus -eq 400 }).Count -eq 1) 'Invalid input check uses an undefined enum value'
    Assert-Check (@($plan | Where-Object { $_.Name -eq 'auth.unauthenticated' -and $_.ExpectedStatus -eq 401 -and -not $_.Token }).Count -eq 1) 'Unauthenticated check sends no token'
    Assert-Check (@($plan | Where-Object { $_.Name -eq 'role.family-denied' -and $_.ExpectedStatus -eq 403 -and $_.Token -eq 'family-token' }).Count -eq 1) 'Role check uses the Family fixture token'

    function Get-PlannedCheck([string]$Name) { return ($plan | Where-Object { $_.Name -eq $Name }) }

    # ---------------- check evaluation ----------------

    $successPass = Get-CheckOutcome -Check (Get-PlannedCheck 'success.content-admin-list') -Status 200 `
        -BodyText '{"items":[{"id":"a","status":1}],"page":1,"pageSize":5,"totalCount":1}' -TraceId '0HN7AAA' -Location 'something failed 0HN7AAA'
    Assert-Check ($successPass.passed) 'Valid success response passes'
    Assert-Check ($successPass.actual -match 'items=1' -and $successPass.actual -match 'totalCount=1') 'Success evidence reports bound values only'
    Assert-Check ($successPass.traceCorrelation -eq 'matched-local-log') 'Matching local log line is correlated'

    $successFail = Get-CheckOutcome -Check (Get-PlannedCheck 'success.content-admin-list') -Status 200 `
        -BodyText '{"items":[],"page":1,"pageSize":20,"totalCount":0}' -TraceId $null -Location $null
    Assert-Check (-not $successFail.passed) 'Wrong pageSize fails the success check'
    Assert-Check (@($successFail.problems).Count -eq 1 -and $successFail.problems[0] -match 'pageSize') 'Failure names the concrete expectation'

    $successStatusFail = Get-CheckOutcome -Check (Get-PlannedCheck 'success.content-admin-list') -Status 500 `
        -BodyText '{"title":"Internal Server Error","status":500,"traceId":"0HN7BBB"}' -TraceId '0HN7BBB' -Location $null
    Assert-Check (-not $successStatusFail.passed) 'Unexpected status fails the check'
    Assert-Check ((@($successStatusFail.problems) -join ' ') -match 'expected HTTP 200, got 500') 'Diagnostics name the expected and actual status'
    Assert-Check ($successStatusFail.actual -match 'status=500') 'Diagnostics carry the actual status'
    Assert-Check ($successStatusFail.traceCorrelation -eq 'unavailable') 'Uncorrelated trace is stated explicitly'

    Assert-Check ((Get-CheckOutcome -Check (Get-PlannedCheck 'paging.page-size-clamp') -Status 200 `
        -BodyText '{"items":[],"page":1,"pageSize":100,"totalCount":0}' -TraceId $null -Location $null).passed) 'Clamped paging response passes'
    Assert-Check (-not (Get-CheckOutcome -Check (Get-PlannedCheck 'paging.page-size-clamp') -Status 200 `
        -BodyText '{"items":[],"page":0,"pageSize":500,"totalCount":0}' -TraceId $null -Location $null).passed) 'Unclamped paging response fails'

    Assert-Check ((Get-CheckOutcome -Check (Get-PlannedCheck 'filter.status-draft') -Status 200 `
        -BodyText '{"items":[{"status":1},{"status":1}],"page":1,"pageSize":5,"totalCount":2}' -TraceId $null -Location $null).passed) 'Filtered response passes when every item matches'
    Assert-Check (-not (Get-CheckOutcome -Check (Get-PlannedCheck 'filter.status-draft') -Status 200 `
        -BodyText '{"items":[{"status":2}],"page":1,"pageSize":5,"totalCount":1}' -TraceId $null -Location $null).passed) 'Filtered response fails on a non-matching item'

    Assert-Check ((Get-CheckOutcome -Check (Get-PlannedCheck 'unknown.guid-detail') -Status 404 `
        -BodyText '{"title":"Not Found","status":404,"code":"Cms.WellnessTip.NotFound","traceId":"0HN7CCC"}' -TraceId '0HN7CCC' -Location $null).passed) 'Known not-found code passes'
    $wrongCode = Get-CheckOutcome -Check (Get-PlannedCheck 'unknown.guid-detail') -Status 404 `
        -BodyText '{"title":"Not Found","status":404,"code":"Cms.WellnessTip.InvalidOperation"}' -TraceId $null -Location $null
    Assert-Check (-not $wrongCode.passed -and (@($wrongCode.problems) -join ' ') -match 'Cms.WellnessTip.NotFound') 'Unexpected business code fails with the expected code named'

    Assert-Check ((Get-CheckOutcome -Check (Get-PlannedCheck 'invalid.enum-query') -Status 400 `
        -BodyText '{"title":"One or more validation errors occurred.","status":400,"errors":{"status":["bad"]},"traceId":"0HN7DDD"}' -TraceId '0HN7DDD' -Location $null).passed) 'Field-error validation response passes'
    Assert-Check (-not (Get-CheckOutcome -Check (Get-PlannedCheck 'invalid.enum-query') -Status 400 `
        -BodyText '{"title":"Bad Request","status":400}' -TraceId $null -Location $null).passed) 'Validation response without field errors fails'

    Assert-Check ((Get-CheckOutcome -Check (Get-PlannedCheck 'auth.unauthenticated') -Status 401 `
        -BodyText '{"title":"Unauthorized","status":401,"detail":"Authentication failed.","traceId":"0HN7EEE"}' -TraceId '0HN7EEE' -Location $null).passed) 'Unauthenticated 401 passes'
    Assert-Check ((Get-CheckOutcome -Check (Get-PlannedCheck 'role.family-denied') -Status 403 `
        -BodyText '{"title":"Forbidden","status":403,"detail":"The requested operation is not allowed."}' -TraceId $null -Location $null).passed) 'Family role denial 403 passes'
    Assert-Check (-not (Get-CheckOutcome -Check (Get-PlannedCheck 'role.family-denied') -Status 200 `
        -BodyText '{"items":[],"page":1,"pageSize":20,"totalCount":0}' -TraceId $null -Location $null).passed) 'Family role receiving 200 fails'

    # ---------------- captured log reading ----------------

    # Regression: the API writes its redirected stdout to a captured log while
    # the verification run reads it. File.ReadAllLines opens that path with a
    # share mode the writer does not permit, so it throws a sharing violation
    # and aborted the whole run (leaving an orphaned API behind). The reader
    # must tolerate the live writer and never abort; a genuinely unreadable
    # log degrades to "no lines".
    $logDir = Join-Path ([System.IO.Path]::GetTempPath()) ('sanad-l1-logtest-' + [Guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($logDir) | Out-Null
    $openLog = Join-Path $logDir 'api.stdout.log'
    $missingLog = Join-Path $logDir 'api.absent.log'

    $writerStream = [System.IO.FileStream]::new($openLog, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::ReadWrite)
    $writer = [System.IO.StreamWriter]::new($writerStream)
    $writer.WriteLine('warn: trace 0HN1 not-found')
    $writer.Flush()

    $naiveThrew = $false
    try { [void][System.IO.File]::ReadAllLines($openLog) } catch { $naiveThrew = $true }
    Assert-Check $naiveThrew 'The naive ReadAllLines really does fail on a log the writer still holds open'

    $heldLines = @(Get-CapturedLogLines -Path @($openLog))
    Assert-Check (@($heldLines).Count -eq 1 -and $heldLines[0] -match '0HN1') 'A held-open log is read without a sharing violation'

    $writer.WriteLine('warn: trace 0HN2 not-found')
    $writer.Flush()
    Assert-Check (@(Get-CapturedLogLines -Path @($openLog)).Count -eq 2) 'Lines flushed after the first read are visible to the next read'

    Assert-Check ((Get-TraceLocation -TraceId '0HN2' -LogLines @(Get-CapturedLogLines -Path @($openLog))) -match '0HN2') 'Trace correlation works against a held-open log'

    $writer.Dispose()

    Assert-Check (@(Get-CapturedLogLines -Path @($missingLog)).Count -eq 0) 'A missing log yields no lines instead of failing'
    Assert-Check (@(Get-CapturedLogLines -Path @()).Count -eq 0) 'No log paths yield no lines'
    Assert-Check (@(Get-CapturedLogLines -Path @('')).Count -eq 0) 'A blank log path yields no lines'
    Assert-Check (@(Get-CapturedLogLines -Path @($logDir)).Count -eq 0) 'A directory path yields no lines'
    Remove-Item $logDir -Recurse -Force -ErrorAction SilentlyContinue

    # ---------------- HTTP client type availability ----------------

    # Regression: System.Net.Http is not loaded in a default Windows PowerShell
    # session. `New-Object System.Net.Http.HttpClient` therefore threw, every
    # readiness probe returned "unreachable" without sending a request, and the
    # API wait always timed out. The helper must make the type constructible.
    Enable-HttpClientType
    Assert-Check ($null -ne ([System.Management.Automation.PSTypeName]'System.Net.Http.HttpClient').Type) 'The HTTP client type is available after Enable-HttpClientType'

    $typeConstructible = $true
    try {
        $availabilityClient = New-Object System.Net.Http.HttpClient
        $availabilityClient.Timeout = [TimeSpan]::FromSeconds(1)
        $availabilityClient.Dispose()
    }
    catch {
        $typeConstructible = $false
    }
    Assert-Check $typeConstructible 'A probe client can be constructed without contacting anything'

    # ---------------- login payload ----------------

    # Regression: the preflight previously sent devicePlatform = 0, which is
    # DevicePlatform.Unknown - the enum default the login validator rejects with
    # "Device platform is invalid". The API answered HTTP 400 and no
    # authenticated check could run, so the payload must never send 0.
    $loginBody = (Get-LoginPayload -Identifier 'content.admin@test.sanad.local' -Password 'fixture-password-shape' -DeviceName 'l1-verify-content-admin') | ConvertFrom-Json
    Assert-Check ($loginBody.identifier -eq 'content.admin@test.sanad.local') 'Login payload carries the identifier'
    Assert-Check ($loginBody.devicePlatform -eq 3) 'Login payload sends the defined Web platform'
    Assert-Check ($loginBody.devicePlatform -ne 0) 'Login payload never sends DevicePlatform.Unknown'
    Assert-Check (-not [string]::IsNullOrWhiteSpace($loginBody.deviceName)) 'Login payload carries a device name'
    Assert-Check ($loginBody.deviceName.Length -le 200) 'Device name stays within the session limit'
    Assert-Check (-not [string]::IsNullOrWhiteSpace($loginBody.appVersion)) 'Login payload carries an app version'
    Assert-Check ($loginBody.appVersion.Length -le 50) 'App version stays within the session limit'
    Assert-Check ($loginBody.password -eq 'fixture-password-shape') 'Login payload carries the password for the preflight'
    Assert-Check ($loginBody.appVersion -eq 'l1-local-fixture-verification') 'Login payload uses the verification app-version marker'

    # ---------------- login access type ----------------

    # Regression: AuthAccessType.Normal is 1, not 0. The preflight previously
    # compared the response against 0, so a correct Normal login was rejected
    # and no authenticated check could run.
    Assert-Check (Test-NormalAccessType -AccessType 1) 'AuthAccessType.Normal (1) is accepted'
    Assert-Check (-not (Test-NormalAccessType -AccessType 2)) 'AuthAccessType.RestrictedVerification (2) is rejected'
    Assert-Check (-not (Test-NormalAccessType -AccessType 0)) 'The undefined value 0 is rejected'
    Assert-Check (-not (Test-NormalAccessType -AccessType $null)) 'A missing access type is rejected'
    Assert-Check (-not (Test-NormalAccessType -AccessType 'not-a-number')) 'A non-numeric access type is rejected'

    $accessTypeSource = Get-Content (Join-Path $repoRoot 'src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Authentication/Tokens/AuthAccessType.cs') -Raw
    Assert-Check ($accessTypeSource -match 'Normal\s*=\s*1') 'The preflight expectation matches AuthAccessType.Normal in the source'
    Assert-Check ($accessTypeSource -match 'RestrictedVerification\s*=\s*2') 'The restricted access type is still 2 in the source'

    # ---------------- diagnostics shape ----------------

    $diagnostic = Get-CheckOutcome -Check (Get-PlannedCheck 'invalid.enum-query') -Status 200 `
        -BodyText '{"title":"Unexpected","status":200}' -TraceId '0HN7FFF' -Location $null
    foreach ($field in @('name', 'method', 'path', 'expected', 'actual', 'passed', 'problems', 'traceId', 'traceCorrelation')) {
        Assert-Check ($diagnostic.Contains($field)) "Diagnostic field present: $field"
    }
    Assert-Check ($diagnostic.name -eq 'invalid.enum-query' -and $diagnostic.method -eq 'GET' -and $diagnostic.path -eq "${adminRoute}?status=999") 'Diagnostic names the check, method and path'
    Assert-Check ($diagnostic.traceId -eq '0HN7FFF') 'Diagnostic carries the trace id'
    Assert-Check (-not (($diagnostic | ConvertTo-Json -Depth 5) -match 'Bearer|accessToken')) 'Diagnostic output contains no credential shape'

    # ---------------- F2 local HTTP verification (offline helpers) ----------------

    # The F2 verifier adds one focused tool that provisions the smallest stable
    # fixture set and proves the three read routes over loopback HTTP. Its pure
    # helpers are exercised here: the SQL text it would run, the visibility and
    # response assertions, and the fail-closed target guard.
    . (Join-Path $repoRoot 'tools/Invoke-F2LocalHttpVerification.ps1') -LibraryOnly

    $f2Definition = Get-F2FixtureDefinition
    $f2SecondDefinition = Get-F2FixtureDefinition

    Assert-Check (($f2Definition | ConvertTo-Json -Depth 6) -eq ($f2SecondDefinition | ConvertTo-Json -Depth 6)) 'The F2 fixture definition is deterministic'

    $f2Ids = @(
        $f2Definition.ownerUserId, $f2Definition.ownerAccountId, $f2Definition.adminUserId, $f2Definition.adminAccountId,
        $f2Definition.facilityId, $f2Definition.roomTypeId, $f2Definition.awaitingBookingId, $f2Definition.baseBookingId,
        $f2Definition.extensionBookingId, $f2Definition.directConversationId, $f2Definition.bookingConversationId,
        $f2Definition.ownerBookingConversationId, $f2Definition.ownerFacilityBookingConversationId,
        $f2Definition.elderlyId, $f2Definition.familyId
    )
    Assert-Check (@($f2Ids | Sort-Object -Unique).Count -eq $f2Ids.Count) 'Every F2 fixture id is distinct'
    Assert-Check (@($f2Ids | Where-Object { $_ -notmatch '^[0-9a-fA-F-]{36}$' }).Count -eq 0) 'Every F2 fixture id is a guid'
    Assert-Check ($f2Definition.ownerAccountType -eq 8 -and $f2Definition.adminAccountType -eq 7) 'The provisioned identities hold the CareHomeOwner and SupportAdmin account types'
    Assert-Check ($f2Definition.activeUserStatus -eq 2) 'The provisioned identities are created Active'
    Assert-Check ($f2Definition.awaitingBookingStatus -eq 2 -and $f2Definition.acceptedBookingStatus -eq 3) 'The fixture booking states are the domain enum values for paid-awaiting-decision and accepted'
    Assert-Check ($f2Definition.paidPaymentStatus -eq 2 -and $f2Definition.refundStatusNone -eq 0) 'The fixture payment and refund states are the domain enum values for paid and no refund'
    Assert-Check ($f2Definition.facilityStatusDraft -eq 1 -and $f2Definition.roomTypeSharedMode -eq 1) 'The fixture facility is a draft and the room type is a shared allocation'
    Assert-Check ((ConvertTo-F2Utf8Hex -Value '') -eq '' -and (ConvertTo-F2Utf8Hex -Value $null) -eq '') 'The UTF-8 hex helper handles empty and null values'
    Assert-Check ((ConvertTo-F2Utf8Hex -Value 'A') -eq '41') 'The UTF-8 hex helper encodes ASCII'
    Assert-Check ((ConvertTo-F2Utf8Hex -Value ([string][char]0x063A)) -eq 'd8ba') 'The UTF-8 hex helper encodes Arabic as UTF-8 bytes'
    Assert-Check (Test-F2SameInstant -Left '2026-10-21T10:00:00.1234567Z' -Right '2026-10-21T10:00:00.123Z') 'Instant comparison tolerates differing sub-second precision'
    Assert-Check (-not (Test-F2SameInstant -Left '2026-10-21T10:00:00Z' -Right '2026-10-21T10:00:05Z')) 'Instant comparison rejects a different instant'
    Assert-Check (-not (Test-F2SameInstant -Left $null -Right '2026-10-21T10:00:00Z')) 'Instant comparison rejects a missing value'

    # ---------------- F2 fixture definition conversations ----------------

    $f2Conversations = @($f2Definition.conversations)
    Assert-Check ($f2Conversations.Count -ge 3 -and $f2Conversations.Count -le 4) 'The F2 conversation fixtures stay within the smallest stable set'
    Assert-Check (@($f2Conversations | Where-Object { $_.scope -eq 0 }).Count -ge 1) 'At least one conversation fixture is direct'
    Assert-Check (@($f2Conversations | Where-Object { $_.scope -eq 1 }).Count -ge 1) 'At least one conversation fixture is booking-scoped'
    Assert-Check (@($f2Conversations | Where-Object { [string]::IsNullOrWhiteSpace($_.partyRule) }).Count -eq 0) 'Every conversation fixture carries the party rule its stored pair must satisfy'
    Assert-Check (@($f2Conversations | Where-Object { [string]$_.partyRule -eq 'careHomeBooking' }).Count -eq 1) 'Exactly one conversation fixture is bound to a Care Home booking the fixture facility owns'
    Assert-Check (@($f2Conversations | Where-Object { $_.scope -eq 0 -and [string]::IsNullOrWhiteSpace($_.bookingSnapshotField) }).Count -eq @($f2Conversations | Where-Object { $_.scope -eq 0 }).Count) 'A direct conversation fixture carries no booking reference source'
    Assert-Check (@($f2Conversations | Where-Object { $_.scope -eq 1 -and [string]::IsNullOrWhiteSpace($_.bookingSnapshotField) }).Count -eq 0) 'A booking conversation fixture carries a booking reference source'
    Assert-Check (@($f2Conversations | Where-Object { @($_.participantRoles).Count -ne 2 }).Count -eq 0) 'Every conversation fixture has exactly two participant roles'
    Assert-Check (@($f2Conversations | Where-Object { @($_.participantRoles | Sort-Object -Unique).Count -ne 2 }).Count -eq 0) 'A conversation fixture never pairs a role with itself'
    Assert-Check (@($f2Conversations | Where-Object { $_.lastActivityOffsetHours -lt 1 }).Count -eq 0) 'Every conversation fixture has a stored activity offset'
    Assert-Check (@(@($f2Conversations | ForEach-Object { $_.lastActivityOffsetHours }) | Sort-Object -Unique).Count -eq $f2Conversations.Count) 'Conversation activity offsets are distinct so the ordering assertion is meaningful'
    Assert-Check (@($f2Conversations | ForEach-Object { $_.id } | Sort-Object -Unique).Count -eq $f2Conversations.Count) 'Conversation fixture ids are distinct'
    $f2FeatureRows = 5 + $f2Conversations.Count
    Assert-Check ($f2FeatureRows -le 10) "The fixture set stays within the authorized feature-row budget ($f2FeatureRows rows)"

    # ---------------- F2 fixture SQL ----------------

    $f2Resolved = @{
        familyOwnerUserId      = '01a0c6b4-6061-744e-81c7-d14ee138c8b3'
        medicalCaregiverUserId = '01a0c6b4-618a-7354-bb17-99bf608a641e'
        chatBookingId          = '01a0c6b4-6da6-73c7-8c40-217f0bdfcee6'
    }
    $f2Sql = Get-F2FixtureSql -Definition $f2Definition -Resolved $f2Resolved
    $f2State = Get-F2StateQuery -Definition $f2Definition

    Assert-Check ($f2Sql.Length -gt 4000) 'The fixture SQL was built'
    Assert-Check (-not $f2Sql.Contains('{')) 'Every fixture SQL placeholder was replaced'
    Assert-Check (-not $f2State.Contains('{')) 'Every state query placeholder was replaced'
    Assert-Check ($f2Sql -match '(?m)^BEGIN;') 'The fixture SQL opens one transaction'
    Assert-Check ($f2Sql -match '(?m)^COMMIT;') 'The fixture SQL commits exactly once'
    Assert-Check (([regex]::Matches($f2Sql, '(?m)^COMMIT;')).Count -eq 1) 'The fixture SQL has a single commit'
    Assert-Check (([regex]::Matches($f2Sql, 'ON CONFLICT \( ?"?[Ii]d"? ?\) DO NOTHING')).Count -eq 13) 'Every fixture insert reuses an existing row through ON CONFLICT (id) DO NOTHING'
    Assert-Check (([regex]::Matches($f2Sql, '(?m)^INSERT INTO')).Count -eq 13) 'The fixture SQL inserts exactly the thirteen intended rows'
    Assert-Check (([regex]::Matches($f2Sql, 'RAISE EXCEPTION')).Count -ge 5) 'The fixture SQL fails closed with in-transaction guard blocks'
    Assert-Check ($f2Sql -notmatch '(?i)\b(UPDATE|DELETE|TRUNCATE|DROP|ALTER|GRANT|REVOKE)\b') 'The fixture SQL only inserts and reads'
    Assert-Check ($f2Sql -notmatch '(?m)^\s*--') 'The fixture SQL carries no comment that could hide a statement'
    Assert-Check ($f2Sql -cmatch "^[\x00-\x7F]*$") 'The fixture SQL is pure ASCII (Arabic literals use escapes)'
    Assert-Check ($f2Sql.Contains("U&'")) 'The fixture SQL writes Arabic values through U& escapes'
    Assert-Check ($f2Sql.Contains('jsonb_build_object')) 'The fixture SQL builds the medical snapshot without a JSON literal'
    Assert-Check ($f2Sql.Contains("SET client_encoding TO 'UTF8'")) 'The fixture SQL pins the client encoding'
    Assert-Check ($f2Sql.Contains("SET statement_timeout TO '60s'")) 'The fixture SQL bounds its own runtime'
    foreach ($fixtureId in @(
            $f2Definition.ownerUserId, $f2Definition.ownerAccountId, $f2Definition.adminUserId, $f2Definition.adminAccountId,
            $f2Definition.facilityId, $f2Definition.roomTypeId, $f2Definition.awaitingBookingId, $f2Definition.baseBookingId,
            $f2Definition.extensionBookingId, $f2Definition.directConversationId, $f2Definition.bookingConversationId,
            $f2Definition.ownerBookingConversationId, $f2Definition.ownerFacilityBookingConversationId)) {
        Assert-Check ($f2Sql.Contains($fixtureId)) "The fixture SQL writes the deterministic id $fixtureId"
    }
    Assert-Check ($f2Sql.Contains($f2Resolved.chatBookingId)) 'The fixture SQL binds the booking-scoped conversations to the reused caregiver booking'
    Assert-Check (([regex]::Matches($f2Sql, [regex]::Escape($f2Definition.baseBookingId))).Count -ge 2) 'The fixture SQL links the extension and the owner facility conversation to the fixture facility booking'
    Assert-Check (-not $f2Sql.Contains("UPDATE care_homes")) 'The fixture SQL never edits the reused rows'

    # The stored participant order must follow Conversation.NormalizePair so the
    # provisioned row is in the canonical shape the domain would have written.
    $f2Pair = Get-F2NormalizedPair -First $f2Resolved.familyOwnerUserId -Second $f2Definition.ownerUserId
    Assert-Check ($f2Pair.One -eq ([Guid]::Parse($f2Resolved.familyOwnerUserId)).ToString()) 'The normalized pair follows Guid.CompareTo'
    Assert-Check ($f2Sql.Contains($f2Pair.One) -and $f2Sql.Contains($f2Pair.Two)) 'The fixture SQL writes the canonical participant order'

    Assert-Check ($f2State -match "json_build_object") 'The state query returns one JSON object'
    Assert-Check ($f2State.TrimStart().StartsWith("SET client_encoding")) 'The state query pins the client encoding before reading'
    Assert-Check ($f2State -notmatch '(?i)\b(UPDATE|DELETE|TRUNCATE|DROP|ALTER)\b') 'The state query is read-only'
    Assert-Check ($f2State -cmatch "^[\x00-\x7F]*$") 'The state query is pure ASCII'
    foreach ($stateField in @('baseBookingId', 'baseBookingFacilityId', 'careHomeBookingFamilyUserId', 'careHomeBookingFacilityOwnerUserId',
            'ownerBookingConversationBookingId', 'ownerFacilityBookingConversationScope', 'ownerFacilityBookingConversationBookingId',
            'ownerFacilityBookingConversationParticipantOne', 'ownerFacilityBookingConversationParticipantTwo')) {
        Assert-Check ($f2State.Contains($stateField)) "The state query reads back $stateField"
    }

    # ---------------- F2 domain linkage of the booking-scoped conversations ----------------

    $f2LinkageRoles = @{
        fixtureOwner     = $f2Definition.ownerUserId
        familyOwner      = $f2Resolved.familyOwnerUserId
        medicalCaregiver = $f2Resolved.medicalCaregiverUserId
    }
    $f2LinkageSnapshot = @{
        chatBookingId                      = $f2Resolved.chatBookingId
        chatBookingCreatedByUserId         = $f2Resolved.familyOwnerUserId
        chatBookingCaregiverUserId         = $f2Resolved.medicalCaregiverUserId
        baseBookingId                      = $f2Definition.baseBookingId
        baseBookingFacilityId              = $f2Definition.facilityId
        careHomeBookingFamilyUserId        = $f2Resolved.familyOwnerUserId
        careHomeBookingFacilityOwnerUserId = $f2Definition.ownerUserId
    }
    foreach ($conversation in $f2Conversations) {
        $storedPair = Get-F2NormalizedPair -First $f2LinkageRoles[$conversation.participantRoles[0]] -Second $f2LinkageRoles[$conversation.participantRoles[1]]
        $f2LinkageSnapshot["$($conversation.key)ConversationParticipantOne"] = $storedPair.One
        $f2LinkageSnapshot["$($conversation.key)ConversationParticipantTwo"] = $storedPair.Two
        $storedBooking = ''
        if ([int]$conversation.scope -eq 1) {
            $storedBooking = $(if ([string]$conversation.bookingSnapshotField -eq 'baseBookingId') { [string]$f2Definition.baseBookingId } else { [string]$f2Resolved.chatBookingId })
        }
        $f2LinkageSnapshot["$($conversation.key)ConversationBookingId"] = $storedBooking
    }

    $f2Linkage = @(Test-F2ConversationLinkage -Definition $f2Definition -Snapshot $f2LinkageSnapshot)
    Assert-Check (@($f2Linkage).Count -eq $f2Conversations.Count) 'The linkage check covers every conversation fixture'
    Assert-Check (@($f2Linkage | Where-Object { @($_.problems).Count -ne 0 }).Count -eq 0) 'The domain-consistent fixture set reports no linkage problem'
    Assert-Check (@($f2Linkage | Where-Object { $_.key -eq 'ownerFacilityBooking' -and $_.domainConsistent -and $_.partyRule -eq 'careHomeBooking' }).Count -eq 1) 'The owner facility conversation is re-derived from the Care Home booking it references'
    Assert-Check (@($f2Linkage | Where-Object { $_.key -eq 'booking' -and $_.domainConsistent -and $_.partyRule -eq 'familiesBooking' }).Count -eq 1) 'The caregiver conversation is re-derived from the Families booking it references'
    Assert-Check (@($f2Linkage | Where-Object { $_.key -eq 'direct' -and $_.domainConsistent -and $_.partyRule -eq 'none' }).Count -eq 1) 'The direct conversation carries no booking reference and no booking party rule'
    Assert-Check (@($f2Linkage | Where-Object { $_.key -eq 'ownerBooking' -and $_.partyRule -eq 'sqlOnlyLegacy' -and -not $_.domainConsistent -and @($_.problems).Count -eq 0 }).Count -eq 1) 'The pre-existing owner booking row is reported as SQL-only legacy, never as domain evidence'
    Assert-Check (@($f2Linkage | Where-Object { [int]$_.scope -eq 1 -and [bool]$_.domainConsistent }).Count -ge 1) 'At least one booking-scoped fixture row is domain-consistent'

    # A fabricated or wrongly bound pair is a problem, so it can never be handed
    # to the booking-scope proof as authorization evidence.
    $f2LinkageWrongBooking = $f2LinkageSnapshot.Clone()
    $f2LinkageWrongBooking['ownerFacilityBookingConversationBookingId'] = $f2Resolved.chatBookingId
    $f2WrongBookingResult = @(Test-F2ConversationLinkage -Definition $f2Definition -Snapshot $f2LinkageWrongBooking)
    Assert-Check (@($f2WrongBookingResult | Where-Object { $_.key -eq 'ownerFacilityBooking' -and @($_.problems).Count -gt 0 }).Count -eq 1) 'A conversation stored against a booking the party rule does not resolve is reported as a problem'

    $f2LinkageWrongPair = $f2LinkageSnapshot.Clone()
    $f2LinkageWrongPair['ownerFacilityBookingConversationParticipantTwo'] = $f2Resolved.medicalCaregiverUserId
    $f2WrongPairResult = @(Test-F2ConversationLinkage -Definition $f2Definition -Snapshot $f2LinkageWrongPair)
    Assert-Check (@($f2WrongPairResult | Where-Object { $_.key -eq 'ownerFacilityBooking' -and ($_.problems -join ' ') -match 'family user / facility owner pair' }).Count -eq 1) 'A fabricated Care Home participant pair is reported as a problem'

    $f2LinkageMissingParties = $f2LinkageSnapshot.Clone()
    $f2LinkageMissingParties['careHomeBookingFamilyUserId'] = ''
    $f2LinkageMissingParties['careHomeBookingFacilityOwnerUserId'] = ''
    Assert-Check (@(@(Test-F2ConversationLinkage -Definition $f2Definition -Snapshot $f2LinkageMissingParties) | Where-Object { $_.key -eq 'ownerFacilityBooking' -and @($_.problems).Count -gt 0 }).Count -eq 1) 'A booking conversation whose booking parties cannot be resolved is reported as a problem'

    $f2LinkageWrongFacility = $f2LinkageSnapshot.Clone()
    $f2LinkageWrongFacility['baseBookingFacilityId'] = 'f2000000-0000-4000-8000-0000000009ff'
    Assert-Check (@(@(Test-F2ConversationLinkage -Definition $f2Definition -Snapshot $f2LinkageWrongFacility) | Where-Object { $_.key -eq 'ownerFacilityBooking' -and @($_.problems).Count -gt 0 }).Count -eq 1) 'A booking conversation bound to a booking another facility owns is reported as a problem'

    $f2LinkageLegacyOnly = $f2LinkageSnapshot.Clone()
    $f2LinkageLegacyOnly['ownerBookingConversationBookingId'] = ''
    Assert-Check (@(@(Test-F2ConversationLinkage -Definition $f2Definition -Snapshot $f2LinkageLegacyOnly) | Where-Object { $_.key -eq 'ownerBooking' -and @($_.problems).Count -gt 0 }).Count -eq 0) 'The kept legacy row is classified without turning into a run failure'

    $f2UnknownRuleDefinition = @{}
    foreach ($definitionKey in @($f2Definition.Keys)) { $f2UnknownRuleDefinition[$definitionKey] = $f2Definition[$definitionKey] }
    $f2UnknownRuleConversations = @($f2Definition.conversations | ForEach-Object {
            $copy = @{}
            foreach ($conversationKey in @($_.Keys)) { $copy[$conversationKey] = $_[$conversationKey] }
            $copy
        })
    $f2UnknownRuleConversations[1]['partyRule'] = 'mysteryRule'
    $f2UnknownRuleDefinition['conversations'] = $f2UnknownRuleConversations
    Assert-Check (@(@(Test-F2ConversationLinkage -Definition $f2UnknownRuleDefinition -Snapshot $f2LinkageSnapshot) | Where-Object { @($_.problems) -join ' ' -match 'unknown party rule' }).Count -eq 1) 'A conversation fixture without a known party rule is reported as a problem'

    # ---------------- F2 connection target guard ----------------

    $f2ParsedConnection = ConvertFrom-F2PostgresConnectionString -ConnectionString 'Host=localhost;Port=5432;Database=SanadDb;Username=postgres;Password=secret'
    Assert-Check ($f2ParsedConnection.Host -eq 'localhost' -and $f2ParsedConnection.Port -eq '5432') 'The connection parser reads the host and port'
    Assert-Check ($f2ParsedConnection.Database -eq 'SanadDb' -and $f2ParsedConnection.Username -eq 'postgres') 'The connection parser reads the database and user'
    Assert-Check (@($f2ParsedConnection.Problems).Count -eq 0) 'A complete connection string parses without problems'
    Assert-Check ((ConvertFrom-F2PostgresConnectionString -ConnectionString 'Server=127.0.0.1;Database=SanadDb').Port -eq '5432') 'A connection string without a port defaults to 5432'
    Assert-Check (@((ConvertFrom-F2PostgresConnectionString -ConnectionString 'Host=localhost').Problems).Count -gt 0) 'A connection string without a database is rejected'
    Assert-Check (@((ConvertFrom-F2PostgresConnectionString -ConnectionString '').Problems).Count -gt 0) 'An empty connection string is rejected'

    $f2GoodReport = [pscustomobject]@{
        passed             = $true
        environmentName    = 'Development'
        targetMode         = 'PersistentLocal'
        failedChecks       = @()
        storageRootExists  = $true
        modules            = @(1..9 | ForEach-Object { [pscustomobject]@{ module = "M$_"; host = 'localhost'; port = 5432; database = 'SanadDb'; pendingMigrations = 0; passed = $true } })
    }
    $f2GoodGuard = Test-F2FixtureTarget -VerificationPassed $true -Report $f2GoodReport -Connection $f2ParsedConnection
    Assert-Check ($f2GoodGuard.Ok) 'The target guard accepts a verified persistent local Development database'
    Assert-Check ((Test-F2FixtureTarget -VerificationPassed $false -Report $f2GoodReport -Connection $f2ParsedConnection).Ok -eq $false) 'The target guard rejects a failed verification'
    Assert-Check ((Test-F2FixtureTarget -VerificationPassed $true -Report $null -Connection $f2ParsedConnection).Ok -eq $false) 'The target guard rejects a missing report'

    foreach ($mutation in @(
            @{ Field = 'environmentName'; Value = 'Production' },
            @{ Field = 'targetMode'; Value = 'RemoteTarget' },
            @{ Field = 'storageRootExists'; Value = $false })) {
        $report = [pscustomobject]@{
            passed            = $true
            environmentName   = 'Development'
            targetMode        = 'PersistentLocal'
            failedChecks      = @()
            storageRootExists = $true
            modules           = @(1..9 | ForEach-Object { [pscustomobject]@{ module = "M$_"; host = 'localhost'; port = 5432; database = 'SanadDb'; pendingMigrations = 0; passed = $true } })
        }
        $report.$($mutation.Field) = $mutation.Value
        Assert-Check ((Test-F2FixtureTarget -VerificationPassed $true -Report $report -Connection $f2ParsedConnection).Ok -eq $false) "The target guard rejects a report whose $($mutation.Field) is wrong"
    }

    $remoteModuleReport = [pscustomobject]@{
        passed            = $true
        environmentName   = 'Development'
        targetMode        = 'PersistentLocal'
        failedChecks      = @()
        storageRootExists = $true
        modules           = @(1..9 | ForEach-Object { [pscustomobject]@{ module = "M$_"; host = 'db.internal.example'; port = 5432; database = 'SanadDb'; pendingMigrations = 0; passed = $true } })
    }
    Assert-Check ((Test-F2FixtureTarget -VerificationPassed $true -Report $remoteModuleReport -Connection $f2ParsedConnection).Ok -eq $false) 'The target guard rejects a remote module host'

    $pendingMigrationReport = [pscustomobject]@{
        passed            = $true
        environmentName   = 'Development'
        targetMode        = 'PersistentLocal'
        failedChecks      = @()
        storageRootExists = $true
        modules           = @(1..9 | ForEach-Object { [pscustomobject]@{ module = "M$_"; host = 'localhost'; port = 5432; database = 'SanadDb'; pendingMigrations = $(if ($_ -eq 4) { 1 } else { 0 }); passed = $true } })
    }
    Assert-Check ((Test-F2FixtureTarget -VerificationPassed $true -Report $pendingMigrationReport -Connection $f2ParsedConnection).Ok -eq $false) 'The target guard rejects a module with pending migrations'

    $shortReport = [pscustomobject]@{
        passed            = $true
        environmentName   = 'Development'
        targetMode        = 'PersistentLocal'
        failedChecks      = @()
        storageRootExists = $true
        modules           = @([pscustomobject]@{ module = 'Identity'; host = 'localhost'; port = 5432; database = 'SanadDb'; pendingMigrations = 0; passed = $true })
    }
    Assert-Check ((Test-F2FixtureTarget -VerificationPassed $true -Report $shortReport -Connection $f2ParsedConnection).Ok -eq $false) 'The target guard rejects an incomplete module set'

    $remoteConnection = ConvertFrom-F2PostgresConnectionString -ConnectionString 'Host=10.1.2.3;Port=5432;Database=SanadDb;Username=postgres;Password=secret'
    Assert-Check ((Test-F2FixtureTarget -VerificationPassed $true -Report $f2GoodReport -Connection $remoteConnection).Ok -eq $false) 'The target guard rejects a remote connection target'
    $wrongDatabaseConnection = ConvertFrom-F2PostgresConnectionString -ConnectionString 'Host=localhost;Port=5432;Database=OtherDb;Username=postgres;Password=secret'
    Assert-Check ((Test-F2FixtureTarget -VerificationPassed $true -Report $f2GoodReport -Connection $wrongDatabaseConnection).Ok -eq $false) 'The target guard rejects a different database on the same host'

    # ---------------- F2 conversation visibility and expectations ----------------

    $f2Snapshot = @{
        awaitingDecisionHoldUntilUtc = '2026-10-21T10:00:00.123Z'
        awaitingElderlyArabicNameHex = (ConvertTo-F2Utf8Hex -Value 'جد تجريبي')
        awaitingTaxAmount            = '1680.00'
        awaitingTotalAmount          = '14880.00'
        baseTaxAmount                = '2100.00'
        extensionTaxAmount           = '2100.00'
        familyOwnerUserId            = $f2Resolved.familyOwnerUserId
        medicalCaregiverUserId       = $f2Resolved.medicalCaregiverUserId
        contentAdminUserId           = '01a158d8-031b-7cf4-ba32-32e931e37a91'
        chatBookingId                = $f2Resolved.chatBookingId
        baseBookingId                = $f2Definition.baseBookingId
    }
    $f2Roles = Get-F2RoleMap -Definition $f2Definition -Snapshot $f2Snapshot

    $f2Visibility = @{}
    foreach ($role in @('fixtureOwner', 'fixtureAdmin', 'familyOwner', 'medicalCaregiver', 'contentAdmin')) {
        $f2Visibility[$role] = @(Get-F2VisibleConversations -Definition $f2Definition -RequesterRole $role | ForEach-Object { $_.key })
    }
    Assert-Check (($f2Visibility['fixtureOwner'] -join ',') -eq 'direct,ownerBooking,ownerFacilityBooking') 'The provisioned owner fixture sees one direct conversation and the two booking-scoped conversations it participates in'
    Assert-Check (($f2Visibility['familyOwner'] -join ',') -eq 'direct,booking,ownerFacilityBooking') 'The existing family owner sees the direct conversation, its own booking conversation and the fixture facility booking conversation'
    Assert-Check (($f2Visibility['medicalCaregiver'] -join ',') -eq 'booking,ownerBooking') 'The existing caregiver sees both booking-scoped conversations and not the direct one'
    Assert-Check (@($f2Visibility['contentAdmin']).Count -eq 0) 'A non-participant sees no fixture conversation'
    Assert-Check (@($f2Visibility['fixtureAdmin']).Count -eq 0) 'The support-admin fixture sees no fixture conversation'

    $f2OwnerExpectations = Get-F2ConversationExpectations -Definition $f2Definition -Roles $f2Roles -Snapshot $f2Snapshot -RequesterRole 'fixtureOwner'
    Assert-Check (@($f2OwnerExpectations).Count -eq 3) 'The owner fixture has three expected conversation rows'
    Assert-Check ([int]$f2OwnerExpectations[0].scope -eq 0 -and $f2OwnerExpectations[0].bookingId -eq '') 'The direct conversation expects scope 0 and no booking reference'
    Assert-Check ([int]$f2OwnerExpectations[1].scope -eq 1 -and $f2OwnerExpectations[1].bookingId -eq $f2Resolved.chatBookingId) 'The booking conversation expects the persisted scope and the reused booking reference'
    Assert-Check ($f2OwnerExpectations[2].key -eq 'ownerFacilityBooking' -and [int]$f2OwnerExpectations[2].scope -eq 1 -and $f2OwnerExpectations[2].bookingId -eq $f2Definition.baseBookingId) 'The owner facility conversation expects the persisted scope and the fixture facility booking reference'
    Assert-Check ($f2OwnerExpectations[0].counterpartUserId -eq $f2Resolved.familyOwnerUserId -and $f2OwnerExpectations[0].counterpartAccountType -eq 1) 'The direct counterpart is the existing family owner account'
    Assert-Check ($f2OwnerExpectations[1].counterpartUserId -eq $f2Resolved.medicalCaregiverUserId -and $f2OwnerExpectations[1].counterpartAccountType -eq 2) 'The booking counterpart is the existing caregiver account'
    Assert-Check ($f2OwnerExpectations[2].counterpartUserId -eq $f2Resolved.familyOwnerUserId -and $f2OwnerExpectations[2].counterpartAccountType -eq 1) 'The owner facility conversation counterpart is that booking''s family user'
    Assert-Check (@(Get-F2ConversationExpectations -Definition $f2Definition -Roles $f2Roles -Snapshot $f2Snapshot -RequesterRole 'contentAdmin').Count -eq 0) 'A non-participant has no expected conversation rows'

    # ---------------- F2 owner list assertion ----------------

    $f2OwnerAwaiting = @{
        id = $f2Definition.awaitingBookingId; facilityId = $f2Definition.facilityId; roomTypeId = $f2Definition.roomTypeId
        startDate = '2026-11-19'; endDate = '2026-12-19'; status = 2; paymentStatus = 2
        totalAmount = 14880.00; currency = 'EGP'; elderlyEnglishName = 'Test Grandfather'
        elderlyArabicName = 'جد تجريبي'; elderlyAge = 76; responsibleContactName = 'Test Contact Fixture'
        responsibleContactPhone = '+201000000099'; responsibleContactRelationship = 'Son'
        careNeedsNotes = 'Requires assistance with daily mobility.'; refundStatus = 0
        extensionOfBookingId = $null; decisionHoldUntilUtc = '2026-10-21T10:00:00.1234567Z'
    }
    $f2OwnerBase = @{
        id = $f2Definition.baseBookingId; facilityId = $f2Definition.facilityId; roomTypeId = $f2Definition.roomTypeId
        startDate = '2026-10-01'; endDate = '2026-11-01'; status = 3; paymentStatus = 2
        totalAmount = 18600.00; currency = 'EGP'; elderlyEnglishName = 'Test Grandfather'
        elderlyArabicName = 'جد تجريبي'; elderlyAge = 76; responsibleContactName = 'Test Contact Fixture'
        responsibleContactPhone = '+201000000099'; responsibleContactRelationship = 'Son'
        careNeedsNotes = 'Requires assistance with daily mobility.'; refundStatus = 0
        extensionOfBookingId = $null; decisionHoldUntilUtc = $null
    }
    $f2OwnerExtension = $f2OwnerBase.Clone()
    $f2OwnerExtension['id'] = $f2Definition.extensionBookingId
    $f2OwnerExtension['extensionOfBookingId'] = $f2Definition.baseBookingId

    $f2OwnerResult = Test-F2OwnerListBody -Body @($f2OwnerAwaiting, $f2OwnerBase, $f2OwnerExtension) -Definition $f2Definition -Snapshot $f2Snapshot
    Assert-Check (@($f2OwnerResult.Problems).Count -eq 0) 'The owner assertion accepts a complete response and reports no problem'

    $f2OwnerCases = @(
        @{ Name = 'a missing decision deadline'; Row = $f2OwnerAwaiting; Field = 'decisionHoldUntilUtc'; Value = $null },
        @{ Name = 'a decision deadline shifted away from the stored value'; Row = $f2OwnerAwaiting; Field = 'decisionHoldUntilUtc'; Value = '2026-10-22T10:00:00Z' },
        @{ Name = 'a resident Arabic name that is not the stored one'; Row = $f2OwnerAwaiting; Field = 'elderlyArabicName'; Value = 'اسم آخر' },
        @{ Name = 'a missing responsible contact name'; Row = $f2OwnerAwaiting; Field = 'responsibleContactName'; Value = '' },
        @{ Name = 'a missing care note'; Row = $f2OwnerAwaiting; Field = 'careNeedsNotes'; Value = '' },
        @{ Name = 'a wrong resident age'; Row = $f2OwnerAwaiting; Field = 'elderlyAge'; Value = 75 },
        @{ Name = 'a total amount that is not the stored value'; Row = $f2OwnerAwaiting; Field = 'totalAmount'; Value = 1.00 }
    )
    foreach ($case in $f2OwnerCases) {
        $body = @($f2OwnerAwaiting.Clone(), $f2OwnerBase.Clone(), $f2OwnerExtension.Clone())
        foreach ($row in $body) {
            if ($row['id'] -eq $case.Row['id']) { $row[$case.Field] = $case.Value }
        }
        $result = Test-F2OwnerListBody -Body $body -Definition $f2Definition -Snapshot $f2Snapshot
        Assert-Check (@($result.Problems).Count -gt 0) "The owner assertion flags $($case.Name)"
    }

    $f2LeakyRow = $f2OwnerAwaiting.Clone()
    $f2LeakyRow['medicalSnapshotJson'] = '{"fixture":"leak"}'
    $f2LeakResult = Test-F2OwnerListBody -Body @($f2LeakyRow, $f2OwnerBase.Clone(), $f2OwnerExtension.Clone()) -Definition $f2Definition -Snapshot $f2Snapshot
    Assert-Check (@($f2LeakResult.Problems | Where-Object { $_ -match 'medicalSnapshotJson' }).Count -gt 0) 'The owner assertion flags an exposed medical snapshot'

    $f2OtherFacilityRow = $f2OwnerBase.Clone()
    $f2OtherFacilityRow['facilityId'] = 'f2000000-0000-4000-8000-0000000009ff'
    $f2ScopeResult = Test-F2OwnerListBody -Body @($f2OwnerAwaiting.Clone(), $f2OwnerBase.Clone(), $f2OtherFacilityRow) -Definition $f2Definition -Snapshot $f2Snapshot
    Assert-Check (@($f2ScopeResult.Problems | Where-Object { $_ -match 'not scoped' }).Count -gt 0) 'The owner assertion flags a row outside the owned facility'

    $f2ExtraRow = $f2OwnerBase.Clone()
    $f2ExtraRow['id'] = 'f2000000-0000-4000-8000-0000000002ff'
    $f2ExtraResult = Test-F2OwnerListBody -Body @($f2OwnerAwaiting.Clone(), $f2OwnerBase.Clone(), $f2OwnerExtension.Clone(), $f2ExtraRow) -Definition $f2Definition -Snapshot $f2Snapshot
    Assert-Check (@($f2ExtraResult.Problems).Count -gt 0) 'The owner assertion flags an unexpected extra booking'

    # ---------------- F2 admin list assertion ----------------

    $f2AdminAwaiting = @{
        bookingId = $f2Definition.awaitingBookingId; facilityId = $f2Definition.facilityId; roomTypeId = $f2Definition.roomTypeId
        residentArabicName = 'جد تجريبي'; residentEnglishName = 'Test Grandfather'; startDate = '2026-11-19'; endDate = '2026-12-19'
        bookingStatus = 2; paymentStatus = 2; refundStatus = 0; totalAmount = 14880.00; currency = 'EGP'
        createdOnUtc = '2026-10-20T10:00:00Z'; paymentCompletedOnUtc = '2026-10-20T11:00:00Z'; refundCompletedOnUtc = $null
        refundAmount = $null; assignedRoomId = $null; assignedBedId = $null; extensionOfBookingId = $null; taxAmount = 1680.00
    }
    $f2AdminBase = $f2AdminAwaiting.Clone()
    $f2AdminBase['bookingId'] = $f2Definition.baseBookingId
    $f2AdminBase['bookingStatus'] = 3
    $f2AdminBase['totalAmount'] = 18600.00
    $f2AdminBase['taxAmount'] = 2100.00
    $f2AdminExtension = $f2AdminBase.Clone()
    $f2AdminExtension['bookingId'] = $f2Definition.extensionBookingId
    $f2AdminExtension['extensionOfBookingId'] = $f2Definition.baseBookingId

    $f2AdminDefaultBody = @{
        page = 1; pageSize = 25; totalCount = 3
        items = @($f2AdminAwaiting.Clone(), $f2AdminBase.Clone(), $f2AdminExtension.Clone())
    }
    Assert-Check (@((Test-F2AdminListBody -Body $f2AdminDefaultBody -Definition $f2Definition -Snapshot $f2Snapshot -Mode 'default').Problems).Count -eq 0) 'The admin assertion accepts the default page and reports no problem'

    $f2AdminBadTax = $f2AdminDefaultBody.Clone()
    $badTaxItems = @($f2AdminAwaiting.Clone(), $f2AdminBase.Clone(), $f2AdminExtension.Clone())
    $badTaxItems[1] = $f2AdminBase.Clone(); $badTaxItems[1]['taxAmount'] = 0.00
    $f2AdminBadTax['items'] = $badTaxItems
    Assert-Check (@((Test-F2AdminListBody -Body $f2AdminBadTax -Definition $f2Definition -Snapshot $f2Snapshot -Mode 'default').Problems | Where-Object { $_ -match 'tax' }).Count -gt 0) 'The admin assertion flags a tax amount that is not the stored value'

    $f2AdminMissingLink = @{ page = 1; pageSize = 25; totalCount = 3; items = @($f2AdminAwaiting.Clone(), $f2AdminBase.Clone(), $f2AdminBase.Clone()) }
    $f2AdminMissingLink.items[2] = $f2AdminExtension.Clone(); $f2AdminMissingLink.items[2]['extensionOfBookingId'] = $f2Definition.awaitingBookingId
    Assert-Check (@((Test-F2AdminListBody -Body $f2AdminMissingLink -Definition $f2Definition -Snapshot $f2Snapshot -Mode 'default').Problems | Where-Object { $_ -match 'extension' }).Count -gt 0) 'The admin assertion flags an extension that does not link back to the accepted base booking'

    $f2AdminBaseLinked = $f2AdminDefaultBody.Clone()
    $linkedItems = @($f2AdminAwaiting.Clone(), $f2AdminBase.Clone(), $f2AdminExtension.Clone())
    $linkedItems[1]['extensionOfBookingId'] = $f2Definition.awaitingBookingId
    $f2AdminBaseLinked['items'] = $linkedItems
    Assert-Check (@((Test-F2AdminListBody -Body $f2AdminBaseLinked -Definition $f2Definition -Snapshot $f2Snapshot -Mode 'default').Problems | Where-Object { $_ -match 'link|extension' }).Count -gt 0) 'The admin assertion flags a base booking reported as an extension'

    $f2AdminFilterBody = @{ page = 1; pageSize = 25; totalCount = 2; items = @($f2AdminBase.Clone(), $f2AdminExtension.Clone()) }
    Assert-Check (@((Test-F2AdminListBody -Body $f2AdminFilterBody -Definition $f2Definition -Snapshot $f2Snapshot -Mode 'statusFilter').Problems).Count -eq 0) 'The admin assertion accepts a filtered page of accepted bookings'
    $f2AdminFilterLeak = @{ page = 1; pageSize = 25; totalCount = 3; items = @($f2AdminAwaiting.Clone(), $f2AdminBase.Clone(), $f2AdminExtension.Clone()) }
    Assert-Check (@((Test-F2AdminListBody -Body $f2AdminFilterLeak -Definition $f2Definition -Snapshot $f2Snapshot -Mode 'statusFilter').Problems).Count -gt 0) 'The admin assertion flags a status filter that returned an ineligible row'

    $f2AdminPagingBody = @{ page = 1; pageSize = 1; totalCount = 3; totalPages = 3; items = @($f2AdminAwaiting.Clone()) }
    Assert-Check (@((Test-F2AdminListBody -Body $f2AdminPagingBody -Definition $f2Definition -Snapshot $f2Snapshot -Mode 'paging').Problems).Count -eq 0) 'The admin assertion accepts a single-item page with the full totals'
    $f2AdminBadPaging = @{ page = 1; pageSize = 1; totalCount = 1; totalPages = 1; items = @($f2AdminAwaiting.Clone()) }
    Assert-Check (@((Test-F2AdminListBody -Body $f2AdminBadPaging -Definition $f2Definition -Snapshot $f2Snapshot -Mode 'paging').Problems).Count -gt 0) 'The admin assertion flags a page that lost the total fixture count'
    Assert-Check (@((Test-F2AdminListBody -Body $null -Definition $f2Definition -Snapshot $f2Snapshot -Mode 'default').Problems).Count -gt 0) 'The admin assertion flags a body that is not JSON'

    # ---------------- F2 chat list assertion ----------------

    $f2ChatDirectRow = @{
        id = $f2Definition.directConversationId; scope = 0; bookingId = $null
        participant = @{ userId = $f2Resolved.familyOwnerUserId; displayName = 'Family Owner Fixture'; avatarUrl = $null; accountType = 1 }
        lastMessage = $null; unreadCount = 0
    }
    $f2ChatBookingRow = @{
        id = $f2Definition.ownerBookingConversationId; scope = 1; bookingId = $f2Resolved.chatBookingId
        participant = @{ userId = $f2Resolved.medicalCaregiverUserId; displayName = 'Medical Caregiver Fixture'; avatarUrl = $null; accountType = 2 }
        lastMessage = $null; unreadCount = 0
    }
    $f2ChatFacilityBookingRow = @{
        id = $f2Definition.ownerFacilityBookingConversationId; scope = 1; bookingId = $f2Definition.baseBookingId
        participant = @{ userId = $f2Resolved.familyOwnerUserId; displayName = 'Family Owner Fixture'; avatarUrl = $null; accountType = 1 }
        lastMessage = $null; unreadCount = 0
    }
    $f2ChatBody = @{ page = 1; pageSize = 20; totalCount = 3; totalPages = 1; items = @($f2ChatDirectRow.Clone(), $f2ChatBookingRow.Clone(), $f2ChatFacilityBookingRow.Clone()) }
    Assert-Check (@((Test-F2ChatListBody -Body $f2ChatBody -Definition $f2Definition -Expectations $f2OwnerExpectations).Problems).Count -eq 0) 'The chat assertion accepts the persisted scopes, references and counterparts'

    $f2ChatStringScope = @{ page = 1; pageSize = 20; totalCount = 3; items = @($f2ChatDirectRow.Clone(), $f2ChatBookingRow.Clone(), $f2ChatFacilityBookingRow.Clone()) }
    $f2ChatStringScope.items[1]['scope'] = '1'
    Assert-Check (@((Test-F2ChatListBody -Body $f2ChatStringScope -Definition $f2Definition -Expectations $f2OwnerExpectations).Problems | Where-Object { $_ -match 'string' }).Count -gt 0) 'The chat assertion flags a scope that is not a number'

    $f2ChatDirectWithBooking = @{ page = 1; pageSize = 20; totalCount = 3; items = @($f2ChatDirectRow.Clone(), $f2ChatBookingRow.Clone(), $f2ChatFacilityBookingRow.Clone()) }
    $f2ChatDirectWithBooking.items[0]['bookingId'] = $f2Resolved.chatBookingId
    Assert-Check (@((Test-F2ChatListBody -Body $f2ChatDirectWithBooking -Definition $f2Definition -Expectations $f2OwnerExpectations).Problems | Where-Object { $_ -match 'direct conversation' }).Count -gt 0) 'The chat assertion flags a direct conversation that exposes a booking reference'

    $f2ChatBookingWithoutBooking = @{ page = 1; pageSize = 20; totalCount = 3; items = @($f2ChatDirectRow.Clone(), $f2ChatBookingRow.Clone(), $f2ChatFacilityBookingRow.Clone()) }
    $f2ChatBookingWithoutBooking.items[1]['bookingId'] = $null
    Assert-Check (@((Test-F2ChatListBody -Body $f2ChatBookingWithoutBooking -Definition $f2Definition -Expectations $f2OwnerExpectations).Problems | Where-Object { $_ -match 'no booking reference' }).Count -gt 0) 'The chat assertion flags a booking conversation without a booking reference'

    $f2ChatWrongReference = @{ page = 1; pageSize = 20; totalCount = 3; items = @($f2ChatDirectRow.Clone(), $f2ChatBookingRow.Clone(), $f2ChatFacilityBookingRow.Clone()) }
    $f2ChatWrongReference.items[2]['bookingId'] = $f2Resolved.chatBookingId
    Assert-Check (@((Test-F2ChatListBody -Body $f2ChatWrongReference -Definition $f2Definition -Expectations $f2OwnerExpectations).Problems | Where-Object { $_ -match 'references booking' }).Count -gt 0) 'The chat assertion flags a booking reference that is not the stored one for that conversation'

    $f2ChatWrongCounterpart = @{ page = 1; pageSize = 20; totalCount = 3; items = @($f2ChatDirectRow.Clone(), $f2ChatBookingRow.Clone(), $f2ChatFacilityBookingRow.Clone()) }
    $f2ChatWrongCounterpart.items[1]['participant'] = @{ userId = $f2Resolved.familyOwnerUserId; displayName = 'Someone Else'; avatarUrl = $null; accountType = 1 }
    Assert-Check (@((Test-F2ChatListBody -Body $f2ChatWrongCounterpart -Definition $f2Definition -Expectations $f2OwnerExpectations).Problems | Where-Object { $_ -match 'counterpart' }).Count -gt 0) 'The chat assertion flags a counterpart that is not the expected participant'

    $f2ChatWithMessage = @{ page = 1; pageSize = 20; totalCount = 3; items = @($f2ChatDirectRow.Clone(), $f2ChatBookingRow.Clone(), $f2ChatFacilityBookingRow.Clone()) }
    $f2ChatWithMessage.items[0]['lastMessage'] = @{ id = 'x' }
    $f2ChatWithMessage.items[0]['unreadCount'] = 3
    $f2ChatMessageProblems = @((Test-F2ChatListBody -Body $f2ChatWithMessage -Definition $f2Definition -Expectations $f2OwnerExpectations).Problems)
    Assert-Check (@($f2ChatMessageProblems | Where-Object { $_ -match 'last message' }).Count -gt 0) 'The chat assertion flags an unexpected last message'
    Assert-Check (@($f2ChatMessageProblems | Where-Object { $_ -match 'unread' }).Count -gt 0) 'The chat assertion flags an unexpected unread count'

    Assert-Check (@((Test-F2ChatListBody -Body @{ items = @($f2ChatBookingRow.Clone()) } -Definition $f2Definition -Expectations $f2OwnerExpectations).Problems).Count -gt 0) 'The chat assertion flags a missing visible conversation'
    Assert-Check (@((Test-F2ChatListBody -Body @{ items = @($f2ChatBookingRow.Clone(), $f2ChatDirectRow.Clone()) } -Definition $f2Definition -Expectations $f2OwnerExpectations).Problems).Count -gt 0) 'The chat assertion flags a missing third visible conversation'
    Assert-Check (@((Test-F2ChatListBody -Body @{ items = @() } -Definition $f2Definition -Expectations @()).Problems).Count -eq 0) 'The chat assertion accepts an authenticated non-participant with no visible conversation'
    Assert-Check (@((Test-F2ChatListBody -Body $null -Definition $f2Definition -Expectations @()).Problems).Count -gt 0) 'The chat assertion flags a body that is not JSON'

    # ---------------- F2 session cleanup plan ----------------

    $f2LogoutPlan = @(Get-F2SessionLogoutPlan -Tokens @{ owner = 'token-a'; fixtureOwner = 'token-a'; admin = 'token-b'; familyOwner = '' })
    Assert-Check (@($f2LogoutPlan).Count -eq 2) 'The logout plan lists one session per distinct login and ignores the shared owner alias and empty tokens'
    Assert-Check ((@($f2LogoutPlan | ForEach-Object { $_.role } | Sort-Object) -join ',') -eq 'admin,owner') 'The logout plan reports the shared owner alias under its canonical role name'
    Assert-Check (@($f2LogoutPlan | Where-Object { $_.path -ne '/api/v1/auth/sessions/logout' -or [int]$_.expectedStatus -ne 204 }).Count -eq 0) 'Every planned logout uses the existing logout route and expects 204'
    $f2AliasOnlyPlan = @(Get-F2SessionLogoutPlan -Tokens @{ fixtureOwner = 'token-c' })
    Assert-Check (@($f2AliasOnlyPlan).Count -eq 1 -and $f2AliasOnlyPlan[0].role -eq 'owner') 'A plan built from the owner alias alone still reports the canonical owner role'
    Assert-Check (@(Get-F2SessionLogoutPlan -Tokens $null).Count -eq 0) 'A run with no token plans no logout'
    Assert-Check (@(Get-F2SessionLogoutPlan -Tokens @{}).Count -eq 0) 'A run with an empty token table plans no logout'
    Assert-Check (@(Get-F2SessionLogoutPlan -Tokens @{ owner = '' }).Count -eq 0) 'A token that was never obtained plans no logout'

    # ---------------- F2 not-run reporting ----------------

    $f2FailedCheck = New-F2FailedCheck -Name 'login-owner' -Actor 'carehome-owner-fixture' -Expected 'a token' -Problem 'not accepted'
    Assert-Check ($f2FailedCheck.passed -eq $false) 'A not-run check can never report a pass'
    Assert-Check ($f2FailedCheck.name -eq 'login-owner' -and @($f2FailedCheck.problems).Count -eq 1) 'A not-run check names itself and carries its problem'
    foreach ($field in @('name', 'method', 'path', 'actor', 'expected', 'actual', 'passed', 'problems', 'traceId', 'traceCorrelation')) {
        Assert-Check ($f2FailedCheck.Contains($field)) "The not-run check keeps the diagnostic field: $field"
    }

    # ---------------- no runtime work ----------------

    Assert-Check ($null -eq $script:apiLogPaths) 'Library-only dot-source performed no runtime work (no API, database or dotnet process)'

    Write-Output "Local fixture verification (offline): $checks checks passed; no API, database, HTTP request or dotnet process was invoked."
}
catch {
    Write-Error $_
    exit 1
}
