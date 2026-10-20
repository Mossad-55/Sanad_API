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

    # ---------------- no runtime work ----------------

    Assert-Check ($null -eq $script:apiLogPaths) 'Library-only dot-source performed no runtime work (no API, database or dotnet process)'

    Write-Output "Local fixture verification (offline): $checks checks passed; no API, database, HTTP request or dotnet process was invoked."
}
catch {
    Write-Error $_
    exit 1
}
