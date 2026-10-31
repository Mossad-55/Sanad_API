#requires -Version 5.1
<#
.SYNOPSIS
    F5 support inbox - small local HTTP verification adapter over the L1 fixture
    library.

.DESCRIPTION
    A bounded adapter (not a second harness). It:

      1. Verifies the effective local target for all nine module contexts,
         migration state and the non-live integration state with the L1
         read-only preflight. No write happens before that report passes.
      2. Provisions four dedicated synthetic identities - a SupportAdmin inbox
         operator, a Family ticket owner, a second Family account and a
         Care-home Owner - with INSERT-only, guarded, idempotent SQL through
         the local psql client. The shared fixture identities are left
         untouched (they already hold the five active sessions the login policy
         allows); the shared fixture password hash is copied, never invented.
      3. Starts (or reuses) the Development API once against the verified
         target.
      4. Proves the support surface end to end: the two anonymous 401s, the
         Family 403 on the admin inbox, invalid query binding, the anonymous
         contact card, the owner ticket page, the admin page and status filter,
         the published OpenAPI shapes, the Care-home Owner Help Center
         audience, submission through the real route, and the full forward-only
         lifecycle (New -> InProgress -> Resolved -> Closed) with an admin reply
         the owner reads back, a refused no-op, a refused reply to a closed
         ticket and a refused status change on a closed ticket.
      5. Logs out every session it created through the existing logout route.

    Safety properties:
      - The base URL must be a loopback HTTP(S) listener; there is no remote
        fallback and no way to pass one.
      - Live SMS/SMTP/Push provider configuration is cleared for every process
        this script starts; migrations stay disabled
        (`Database:ApplyMigrationsOnStartup=false`), so the Identity
        `AddSupportTicketWorkflow` migration must already be applied - the
        read-only preflight proves the resolved target has no pending
        migrations before any write.
      - Fixture SQL is INSERT-only, guarded and idempotent: nothing is updated,
        deleted or reset.
      - Mutations are never retried automatically.
      - Tokens, credentials, connection strings and payloads are never printed;
        a failed check reports only status, safe business code, field names,
        trace id and a redacted local log line.
      - Offline tests dot-source this file with -LibraryOnly and exercise only
        the pure functions; no API, database or HTTP call occurs there.
      - Each run appends exactly one support ticket and one reply to the
        persistent local database. Both are retained history and are never
        deleted; the report states the owner's ticket count so growth is
        visible.

.PARAMETER BaseUrl
    Loopback HTTP(S) base URL of the API. Also used as ASPNETCORE_URLS when the
    script starts the API. Defaults to port 5245, which no other phase adapter
    claims (5235 L1/F2/F4, 5241 F3, 5243 F6).

.PARAMETER ApiAssemblyPath
    Optional. Built API assembly. Defaults to the Release output.

.PARAMETER FixturePassword
    Synthetic fixture password shared with the existing L1 fixture identities.
    Never a real credential and never emitted in output.

.PARAMETER LibraryOnly
    Returns after defining the helper functions so offline tests can dot-source
    them without any runtime work.

.PARAMETER SkipApiStart
    Never starts the API; the script requires an already-reachable listener.

.PARAMETER SkipFixtureWrite
    Reports only: the fixture provisioning SQL is printed to a file and no
    database write, API start or HTTP check happens.
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:5245',

    [string]$ApiAssemblyPath,

    [string]$FixturePassword = 'Test-1234!',

    [switch]$LibraryOnly,

    [switch]$SkipApiStart,

    [switch]$SkipFixtureWrite
)

$ErrorActionPreference = 'Stop'

# Capture this script's own parameters before dot-sourcing the L1 library:
# dot-sourcing binds L1's parameter names (BaseUrl, FixturePassword,
# ApiAssemblyPath, SkipApiStart, ...) in this scope and would otherwise
# overwrite the values passed to this script.
$f5LibraryOnly      = [bool]$LibraryOnly
$f5BaseUrl          = $BaseUrl
$f5ApiAssemblyPath  = $ApiAssemblyPath
$f5FixturePassword  = $FixturePassword
$f5SkipApiStart     = [bool]$SkipApiStart
$f5SkipFixtureWrite = [bool]$SkipFixtureWrite

. (Join-Path $PSScriptRoot 'Invoke-LocalFixtureVerification.ps1') -LibraryOnly

# ---------------------------------------------------------------------------
# F5 routes
# ---------------------------------------------------------------------------

function Get-F5Route {
    [CmdletBinding()]
    param()

    return [ordered]@{
        AdminList       = '/api/v1/admin/support-tickets'
        AccountTypeSupportAdmin = 7
        AccountTypeFamily       = 1
        AccountTypeCareHomeOwner = 8
        SubmittedSubject = 'F5 verification support ticket'
        SubmittedMessage = 'Verification message for the F5 support inbox adapter.'
        ReplyBody        = 'F5 verification reply from the support inbox.'
    }
}

# ---------------------------------------------------------------------------
# F5 fixture definition
# ---------------------------------------------------------------------------

function Get-F5FixtureDefinition {
    <#
        The four dedicated synthetic identities. Account types: Family = 1,
        SupportAdmin = 7, CareHomeOwner = 8. UserStatus Active = 2. The
        password hash is copied from the shared L1 fixture identity, so no
        credential is ever stored in this repository.
    #>
    [CmdletBinding()]
    param()

    return [ordered]@{
        SupportAdmin = [ordered]@{
            Email         = 'support.admin.f5@test.sanad.local'
            UserId        = '019a2f5a-0001-7000-8000-000000000001'
            PhoneNumber   = '+201000000051'
            ArabicName    = 'F5 Support Admin (arabic name slot)'
            EnglishName   = 'F5 Support Admin'
            AccountType   = 7
            AccountTypeId = '019a2f5a-0002-7000-8000-000000000001'
        }
        Owner        = [ordered]@{
            Email         = 'family.owner.f5@test.sanad.local'
            UserId        = '019a2f5a-0001-7000-8000-000000000002'
            PhoneNumber   = '+201000000052'
            ArabicName    = 'F5 Ticket Owner (arabic name slot)'
            EnglishName   = 'F5 Ticket Owner'
            AccountType   = 1
            AccountTypeId = '019a2f5a-0002-7000-8000-000000000002'
        }
        Other        = [ordered]@{
            Email         = 'family.other.f5@test.sanad.local'
            UserId        = '019a2f5a-0001-7000-8000-000000000003'
            PhoneNumber   = '+201000000053'
            ArabicName    = 'F5 Other Family (arabic name slot)'
            EnglishName   = 'F5 Other Family'
            AccountType   = 1
            AccountTypeId = '019a2f5a-0002-7000-8000-000000000003'
        }
        CareHomeOwner = [ordered]@{
            Email         = 'carehome.owner.f5@test.sanad.local'
            UserId        = '019a2f5a-0001-7000-8000-000000000004'
            PhoneNumber   = '+201000000054'
            ArabicName    = 'F5 Care Home Owner (arabic name slot)'
            EnglishName   = 'F5 Care Home Owner'
            AccountType   = 8
            AccountTypeId = '019a2f5a-0002-7000-8000-000000000004'
        }
        PasswordSource = 'content.admin@test.sanad.local'
    }
}

function ConvertFrom-F5PostgresConnectionString {
    <#
        Minimal Npgsql-style connection string parser. It returns the parsed
        values so the guard can assert the target before any write; callers must
        never print the password.
    #>
    [CmdletBinding()]
    param([AllowNull()][AllowEmptyString()][string]$ConnectionString)

    $result = [ordered]@{
        Host     = ''
        Port     = ''
        Database = ''
        Username = ''
        Password = ''
        Problems = @()
    }

    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        $result.Problems = @('the resolved connection string is empty')
        return $result
    }

    foreach ($segment in ($ConnectionString -split ';')) {
        if ([string]::IsNullOrWhiteSpace($segment)) { continue }
        $index = $segment.IndexOf('=')
        if ($index -lt 1) {
            $result.Problems += 'a connection string segment is not a key/value pair'
            continue
        }
        $key = $segment.Substring(0, $index).Trim()
        $value = $segment.Substring($index + 1).Trim().Trim('"').Trim("'")
        switch -Regex ($key) {
            '^(?i)(host|server|data source)$' { $result.Host = $value }
            '^(?i)port$' { $result.Port = $value }
            '^(?i)(database|initial catalog)$' { $result.Database = $value }
            '^(?i)(username|user id|user)$' { $result.Username = $value }
            '^(?i)(password|pwd)$' { $result.Password = $value }
        }
    }

    if ([string]::IsNullOrWhiteSpace([string]$result.Host)) { $result.Problems += 'the connection string carries no host' }
    if ([string]::IsNullOrWhiteSpace([string]$result.Database)) { $result.Problems += 'the connection string carries no database' }
    if ([string]::IsNullOrWhiteSpace([string]$result.Port)) { $result.Port = '5432' }

    return $result
}

function Resolve-F5ConnectionStringSource {
    <#
        Resolves the connection string the API itself uses without surfacing it:
        the process environment first, then the API's Development settings file.
    #>
    [CmdletBinding()]
    param([AllowNull()][AllowEmptyString()][string]$RepoRoot)

    foreach ($name in @('ConnectionStrings__IdentityDatabase', 'ConnectionStrings:IdentityDatabase')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return @{ Value = $value; Source = "environment:$name" }
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($RepoRoot)) {
        $settingsPath = Join-Path $RepoRoot 'src/API/Ahlena.API/appsettings.Development.json'
        if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
            $settings = ConvertFrom-SafeJson ([System.IO.File]::ReadAllText($settingsPath))
            if ($null -ne $settings -and $null -ne $settings.ConnectionStrings) {
                $names = @($settings.ConnectionStrings.PSObject.Properties.Name)
                if ($names -contains 'IdentityDatabase') {
                    $candidate = [string]$settings.ConnectionStrings.IdentityDatabase
                    if (-not [string]::IsNullOrWhiteSpace($candidate)) {
                        return @{ Value = $candidate; Source = 'appsettings.Development.json' }
                    }
                }
            }
        }
    }

    return @{ Value = ''; Source = '' }
}

function Test-F5FixtureTarget {
    <#
        Fail-closed guard evaluated before any fixture write: the read-only
        verification must have passed against the persistent local Development
        database for all nine module contexts, and the resolved psql connection
        must point at the same loopback database.
    #>
    [CmdletBinding()]
    param(
        [bool]$VerificationPassed,
        [AllowNull()][object]$Report,
        [AllowNull()][hashtable]$Connection,
        [AllowNull()][string]$PsqlPath
    )

    $problems = @()
    if ([string]::IsNullOrWhiteSpace($PsqlPath)) {
        $problems += 'the local PostgreSQL client (psql) was not found, so the fixtures cannot be provisioned or read back'
    }
    if (-not $VerificationPassed) { $problems += 'the read-only target verification did not pass' }
    if ($null -eq $Report) {
        $problems += 'the read-only target verification produced no report'
        return @{ Ok = $false; Problems = @($problems) }
    }

    if (-not [bool]$Report.passed) { $problems += 'the target verification report is not passed' }
    if ([string]$Report.environmentName -ne 'Development') { $problems += "the verified environment is '$($Report.environmentName)', expected 'Development'" }
    if ([string]$Report.targetMode -ne 'PersistentLocal') { $problems += "the verified target mode is '$($Report.targetMode)', expected 'PersistentLocal'" }
    if (@($Report.failedChecks).Count -gt 0) { $problems += 'the target verification reported failed checks' }
    if (-not [bool]$Report.storageRootExists) { $problems += 'the local storage root does not exist' }
    if ([bool]$Report.paymobLiveKey) { $problems += 'the verified target reports a live Paymob key' }
    if ([bool]$Report.smsMisrConfigured) { $problems += 'the verified target still has SMS Misr credentials configured' }
    if ([bool]$Report.smtpEmailConfigured) { $problems += 'the verified target still has SMTP credentials configured' }

    $modules = @($Report.modules)
    if ($modules.Count -ne 9) { $problems += "the target verification covered $($modules.Count) module contexts, expected 9" }
    foreach ($module in $modules) {
        if (-not (Test-LoopbackHost ([string]$module.host))) { $problems += "module $($module.module) does not target a loopback host" }
        if ([int]$module.port -ne 5432) { $problems += "module $($module.module) does not target the local port 5432" }
        if ([string]$module.database -ne 'AhlenaDb') { $problems += "module $($module.module) does not target database AhlenaDb" }
        if ([int]$module.pendingMigrations -ne 0) { $problems += "module $($module.module) has pending migrations" }
        if (-not [bool]$module.passed) { $problems += "module $($module.module) did not pass verification" }
    }

    if ($null -eq $Connection) {
        $problems += 'no local database connection target was resolved'
    }
    else {
        if (-not (Test-LoopbackHost ([string]$Connection.Host))) { $problems += 'the resolved connection does not target a loopback host' }
        if ([string]$Connection.Port -ne '5432') { $problems += "the resolved connection targets port '$($Connection.Port)', expected 5432" }
        if ([string]$Connection.Database -ne 'AhlenaDb') { $problems += "the resolved connection targets database '$($Connection.Database)', expected AhlenaDb" }
        if ([string]::IsNullOrWhiteSpace([string]$Connection.Username)) { $problems += 'the resolved connection carries no user name for the local client' }
        if ([string]::IsNullOrWhiteSpace([string]$Connection.Password)) { $problems += 'the resolved connection carries no password for the local client' }
    }

    return @{ Ok = ($problems.Count -eq 0); Problems = @($problems) }
}

function Get-F5FixtureSql {
    <#
        Idempotent, INSERT-only fixture provisioning. Every statement is
        guarded: a rerun inserts nothing, and nothing is ever updated or
        deleted. The shared fixture password hash is copied, never invented.
    #>
    [CmdletBinding()]
    param()

    $definition = Get-F5FixtureDefinition

    $identityInsert = @"
INSERT INTO identity.users
    (id, arabic_full_name, english_full_name, phone_number, email,
     email_verified, phone_verified, status, created_on_utc, updated_on_utc, password_hash)
SELECT '{0}', '{1}', '{2}', '{3}', '{4}', true, true, 2, now(), now(), source.password_hash
FROM identity.users AS source
WHERE source.email = '{5}'
  AND NOT EXISTS (SELECT 1 FROM identity.users AS existing WHERE existing.email = '{4}');

INSERT INTO identity.user_accounts (id, account_type, created_on_utc, user_id)
SELECT '{6}', {7}, now(), fixture.id
FROM identity.users AS fixture
WHERE fixture.email = '{4}'
  AND NOT EXISTS (
      SELECT 1 FROM identity.user_accounts AS account
      WHERE account.user_id = fixture.id AND account.account_type = {7});
"@

    $fragments = @()
    foreach ($name in @('SupportAdmin', 'Owner', 'Other', 'CareHomeOwner')) {
        $actor = $definition[$name]
        $fragments += ($identityInsert -f $actor.UserId, $actor.ArabicName, $actor.EnglishName, $actor.PhoneNumber, $actor.Email, $definition.PasswordSource, $actor.AccountTypeId, $actor.AccountType)
    }

    return @"
\set ON_ERROR_STOP on
BEGIN;

-- Fail closed when the shared fixture password hash is missing: a fixture
-- identity without a usable hash could never authenticate, and emitting one
-- would silently break every later check.
DO `$f5guard`$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM identity.users
        WHERE email = '$($definition.PasswordSource)' AND password_hash IS NOT NULL)
    THEN
        RAISE EXCEPTION 'F5 fixture guard: the shared fixture password hash is missing';
    END IF;
END
`$f5guard`$;

$($fragments -join "`n")
COMMIT;
"@
}

function Get-F5StateQuery {
    <#
        Read-only readback of the fixtures, so the HTTP proof starts from
        verified ids and account types instead of assumptions.
    #>
    [CmdletBinding()]
    param()

    $definition = Get-F5FixtureDefinition
    $adminEmail = $definition.SupportAdmin.Email
    $ownerEmail = $definition.Owner.Email
    $otherEmail = $definition.Other.Email
    $careHomeEmail = $definition.CareHomeOwner.Email

    return @"
SELECT json_build_object(
    'supportAdminUserId', (SELECT id::text FROM identity.users WHERE email = '$adminEmail'),
    'supportAdminAccountTypes', (SELECT coalesce(string_agg(account.account_type::text, ','), '') FROM identity.user_accounts AS account JOIN identity.users AS u ON u.id = account.user_id WHERE u.email = '$adminEmail'),
    'supportAdminStatus', (SELECT status FROM identity.users WHERE email = '$adminEmail'),
    'ownerUserId', (SELECT id::text FROM identity.users WHERE email = '$ownerEmail'),
    'ownerAccountTypes', (SELECT coalesce(string_agg(account.account_type::text, ','), '') FROM identity.user_accounts AS account JOIN identity.users AS u ON u.id = account.user_id WHERE u.email = '$ownerEmail'),
    'ownerStatus', (SELECT status FROM identity.users WHERE email = '$ownerEmail'),
    'otherUserId', (SELECT id::text FROM identity.users WHERE email = '$otherEmail'),
    'careHomeOwnerUserId', (SELECT id::text FROM identity.users WHERE email = '$careHomeEmail'),
    'careHomeOwnerAccountTypes', (SELECT coalesce(string_agg(account.account_type::text, ','), '') FROM identity.user_accounts AS account JOIN identity.users AS u ON u.id = account.user_id WHERE u.email = '$careHomeEmail'),
    'ownerTicketCount', (SELECT count(*) FROM identity.support_tickets WHERE user_id = (SELECT id FROM identity.users WHERE email = '$ownerEmail'))
);
"@
}

# ---------------------------------------------------------------------------
# Assert factories (pure)
# ---------------------------------------------------------------------------

# Note on assertion scope: a check assertion is invoked by the L1
# Get-CheckOutcome in the script scope, so an assertion that is NOT wrapped in
# .GetNewClosure() can call the script-scope helpers below. An assertion that
# IS wrapped needs .GetNewClosure() to bind the values captured while the check
# plan was built, and a closure resolves functions from the global scope only -
# it cannot see this script's functions. Every assertion that binds values
# therefore inlines its own shape checks instead of calling a helper.

function New-F5AssertSet {
    [CmdletBinding()]
    param()

    $plain = {
        param($status, $body)
        return @{ Actual = "status=$status"; Problems = @() }
    }

    $problem = {
        param($status, $body)
        $code = ''
        if ($null -ne $body -and $body.PSObject.Properties.Name -contains 'code') { $code = [string]$body.code }
        $actual = "status=$status code=$code $(Get-SafeErrorSummary $body)"
        return @{ Actual = $actual; Problems = @() }
    }

    return [ordered]@{
        Plain   = $plain
        Problem = $problem
    }
}

function New-F5CodeAssert {
    <#
        The response must be a problem details carrying exactly this business
        code.
    #>
    [CmdletBinding()]
    param([string]$Code)

    return ({
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $code = ''
        if ($body.PSObject.Properties.Name -contains 'code') { $code = [string]$body.code }
        $actual = "status=$status code=$code"
        if ($code -ne $Code) { $problems += "expected business code '$Code', got '$code'" }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()
}

function New-F5PageAssert {
    <#
        The paged envelope of the owner/admin list reads: items + page + pageSize
        + totalCount, the default page, no over-sized page, newest-first order,
        the pinned item members and - when a status filter is stated - only that
        status.
    #>
    [CmdletBinding()]
    param([AllowNull()][object]$RequiredStatus = $null)

    return ({
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $names = @($body.PSObject.Properties.Name)
        $items = @()
        if (($names -contains 'items') -and $null -ne $body.items) { $items = @($body.items) }
        $hasTotal = $names -contains 'totalCount'
        $total = if ($hasTotal) { [int]$body.totalCount } else { -1 }
        $actual = "status=$status items=$($items.Count) page=$($body.page) pageSize=$($body.pageSize) totalCount=$total"

        if ($names -notcontains 'items') { $problems += 'items array is missing' }
        if (-not $hasTotal) { $problems += 'totalCount is missing' }
        if ([int]$body.page -ne 1) { $problems += "page must default to 1, got $($body.page)" }
        if ([int]$body.pageSize -ne 25) { $problems += "pageSize must default to 25, got $($body.pageSize)" }
        if ($items.Count -gt 25) { $problems += "the default page returned $($items.Count) items" }
        if ($items.Count -gt 0 -and $items.Count -gt $total) { $problems += "the page returned more items ($($items.Count)) than totalCount ($total)" }

        foreach ($item in $items) {
            foreach ($member in @('ticketId', 'subject', 'status', 'createdOnUtc')) {
                if (@($item.PSObject.Properties.Name) -notcontains $member) { $problems += "a list item is missing $member" }
            }
            if ($null -ne $RequiredStatus -and [int]$item.status -ne [int]$RequiredStatus) {
                $problems += "the status filter returned a row with status $($item.status)"
            }
        }

        $instants = @()
        foreach ($item in $items) {
            try {
                $instants += [DateTime]::Parse(
                    [string]$item.createdOnUtc,
                    [System.Globalization.CultureInfo]::InvariantCulture,
                    [System.Globalization.DateTimeStyles]::RoundtripKind)
            }
            catch {
                $problems += "an item createdOnUtc is not a parseable instant: $($item.createdOnUtc)"
                $instants = @()
                break
            }
        }
        for ($index = 1; $index -lt $instants.Count; $index++) {
            if ($instants[$index] -gt $instants[$index - 1]) {
                $problems += 'items are not newest-first by createdOnUtc'
                break
            }
        }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()
}

# ---------------------------------------------------------------------------
# Check plans
# ---------------------------------------------------------------------------

function New-F5CheckPlanStageOne {
    <#
        Checks that need no captured runtime value. The submission is last so
        its response can be captured for stage two.
    #>
    [CmdletBinding()]
    param(
        [string]$AdminToken,
        [string]$OwnerToken,
        [string]$OtherToken,
        [string]$CareHomeOwnerToken
    )

    $helpers = Get-F5Route
    $plain = New-F5AssertSet

    $contactAssert = {
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $names = @($body.PSObject.Properties.Name)
        $actual = "status=$status fields=$($names -join ',')"
        foreach ($expected in @('supportPhone', 'supportEmail')) {
            if ($names -notcontains $expected) { $problems += "the contact card is missing $expected" }
        }
        foreach ($name in $names) {
            if (@('supportPhone', 'supportEmail') -notcontains $name) {
                $problems += "the anonymous contact card exposes an unexpected field: $name"
            }
        }

        return @{ Actual = $actual; Problems = $problems }
    }

    $ownerHelpAssert = {
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $names = @($body.PSObject.Properties.Name)
        $actual = "status=$status fields=$($names -join ',')"
        if ($names -notcontains 'faqs') { $problems += 'the Help Center read publishes no faqs array' }
        if ($names -notcontains 'supportContact') { $problems += 'the Help Center read publishes no supportContact' }

        return @{ Actual = $actual; Problems = $problems }
    }

    $openApiAssert = {
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $paths = @($body.paths.PSObject.Properties.Name)
        $schemas = @($body.components.schemas.PSObject.Properties.Name)
        $actual = "status=$status paths=$($paths.Count) schemas=$($schemas.Count)"

        foreach ($route in @('/api/v1/support/contact', '/api/v1/support/tickets')) {
            if ($paths -notcontains $route) { $problems += "the published OpenAPI document has no path $route" }
        }
        if (@($paths | Where-Object { $_ -like '/api/v1/support/tickets/*' }).Count -lt 1) {
            $problems += 'the published document has no owner ticket detail path'
        }
        if (@($paths | Where-Object { $_ -like '/api/v1/admin/support-tickets*' }).Count -lt 4) {
            $problems += 'the published document does not publish all four admin support inbox paths'
        }
        if (@($paths | Where-Object { $_ -like '*/status' }).Count -lt 1) { $problems += 'the admin status route is not published' }
        if (@($paths | Where-Object { $_ -like '*/replies' }).Count -lt 1) { $problems += 'the admin reply route is not published' }

        foreach ($schema in @('AdminSupportTicketsPage', 'AdminSupportTicketListItem', 'AdminSupportTicketDetail',
                              'SupportTicketReplyDto', 'MySupportTicketsPage', 'MySupportTicketDetail',
                              'PublicSupportContactResponse')) {
            if ($schemas -notcontains $schema) { $problems += "$schema is missing from the published schemas" }
        }

        if ($paths -contains '/api/v1/admin/support-tickets') {
            $operation = $body.paths.'/api/v1/admin/support-tickets'.get
            foreach ($name in @('status', 'page', 'pageSize')) {
                $parameter = @($operation.parameters | Where-Object { $_.name -eq $name })
                if ($parameter.Count -ne 1) { $problems += "the published admin list has no single '$name' parameter" }
            }
        }

        return @{ Actual = $actual; Problems = $problems }
    }

    $submitAssert = {
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $id = ''
        if ($body.PSObject.Properties.Name -contains 'ticketId') {
            $ticket = $body.ticketId
            if ($null -ne $ticket -and @($ticket.PSObject.Properties.Name) -contains 'value') { $id = [string]$ticket.value }
            else { $id = [string]$ticket }
        }
        $actual = "status=$status ticketId=$id status=$($body.status)"

        $parsed = [Guid]::Empty
        if (-not [Guid]::TryParse($id, [ref]$parsed)) { $problems += "the submitted ticket id is not a uuid: '$id'" }
        if ([int]$body.status -ne 1) { $problems += "the submitted ticket status is $($body.status), expected 1 (New)" }
        if ([string]::IsNullOrWhiteSpace([string]$body.submittedOnUtc)) { $problems += 'submittedOnUtc is missing' }

        return @{ Actual = $actual; Problems = $problems }
    }

    return @(
        [ordered]@{
            Name           = 'auth.unauthenticated-admin-list'
            Method         = 'GET'
            Path           = $helpers.AdminList
            Actor          = 'anonymous'
            Token          = $null
            ExpectedStatus = 401
            Expected       = 'HTTP 401 because no bearer token is supplied'
            Assert         = $plain.Plain
        }
        [ordered]@{
            Name           = 'auth.unauthenticated-my-tickets'
            Method         = 'GET'
            Path           = '/api/v1/support/tickets'
            Actor          = 'anonymous'
            Token          = $null
            ExpectedStatus = 401
            Expected       = 'HTTP 401 because no bearer token is supplied'
            Assert         = $plain.Plain
        }
        [ordered]@{
            Name           = 'role.family-denied-admin-list'
            Method         = 'GET'
            Path           = $helpers.AdminList
            Actor          = 'ticket-owner (Family)'
            Token          = $OwnerToken
            ExpectedStatus = 403
            Expected       = 'HTTP 403 because the Family account lacks the SupportInboxAdmin policy'
            Assert         = $plain.Plain
        }
        [ordered]@{
            Name           = 'read.anonymous-contact-card'
            Method         = 'GET'
            Path           = '/api/v1/support/contact'
            Actor          = 'anonymous'
            Token          = $null
            ExpectedStatus = 200
            Expected       = 'HTTP 200 carrying only supportPhone and supportEmail, with no FAQ or ticket field'
            Assert         = $contactAssert
        }
        [ordered]@{
            Name           = 'invalid.query-binding-status'
            Method         = 'GET'
            Path           = ($helpers.AdminList + '?status=not-a-number')
            Actor          = 'support-admin'
            Token          = $AdminToken
            ExpectedStatus = 400
            Expected       = 'HTTP 400 with a field-level problem details for the invalid integer status filter'
            Assert         = $plain.Problem
        }
        [ordered]@{
            Name           = 'read.admin-list-default-page'
            Method         = 'GET'
            Path           = $helpers.AdminList
            Actor          = 'support-admin'
            Token          = $AdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 with items, page=1, pageSize=25, totalCount and newest-first order'
            Assert         = (New-F5PageAssert)
        }
        [ordered]@{
            Name           = 'read.admin-list-status-filter-new'
            Method         = 'GET'
            Path           = ($helpers.AdminList + '?status=1')
            Actor          = 'support-admin'
            Token          = $AdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 whose status=1 filter returns only New tickets'
            Assert         = (New-F5PageAssert -RequiredStatus 1)
        }
        [ordered]@{
            Name           = 'read.owner-tickets-page'
            Method         = 'GET'
            Path           = '/api/v1/support/tickets'
            Actor          = 'ticket-owner'
            Token          = $OwnerToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 with the owner page envelope, default paging and newest-first order'
            Assert         = (New-F5PageAssert)
        }
        [ordered]@{
            Name           = 'read.other-tickets-empty'
            Method         = 'GET'
            Path           = '/api/v1/support/tickets'
            Actor          = 'other-family'
            Token          = $OtherToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 with totalCount 0 because this account never submits a ticket'
            Assert         = (New-F5PageAssert)
        }
        [ordered]@{
            Name           = 'openapi.support-routes-published'
            Method         = 'GET'
            Path           = '/openapi/v1.json'
            Actor          = 'support-admin'
            Token          = $AdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 whose document publishes the support/admin routes, their query parameters and the support schemas'
            Assert         = $openApiAssert
        }
        [ordered]@{
            Name           = 'read.help-center-owner-audience'
            Method         = 'GET'
            Path           = '/api/v1/help-center'
            Actor          = 'care-home-owner'
            Token          = $CareHomeOwnerToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 because the Care Home Owner audience is a Help Center audience as of F5'
            Assert         = $ownerHelpAssert
        }
        [ordered]@{
            Name           = 'write.submit-support-request'
            Method         = 'POST'
            Path           = '/api/v1/support/contact'
            Actor          = 'ticket-owner'
            Token          = $OwnerToken
            Body           = (@{ subject = $helpers.SubmittedSubject; message = $helpers.SubmittedMessage } | ConvertTo-Json -Compress)
            ExpectedStatus = 200
            Expected       = 'HTTP 200 with a uuid ticket id and status 1 through the real submission route'
            Assert         = $submitAssert
        }
    )
}

function New-F5CheckPlanStageTwo {
    <#
        Checks that depend on the ticket submitted in stage one.
    #>
    [CmdletBinding()]
    param(
        [string]$AdminToken,
        [string]$OwnerToken,
        [string]$OtherToken,
        [string]$TicketId,
        [string]$OwnerUserId,
        [string]$AdminUserId
    )

    $helpers = Get-F5Route
    $plain = New-F5AssertSet
    $detailRoute = "$($helpers.AdminList)/$TicketId"
    $myDetailRoute = "/api/v1/support/tickets/$TicketId"
    $statusRoute = "$detailRoute/status"
    $replyRoute = "$detailRoute/replies"

    $newDetailAssert = ({
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $replies = @()
        if (@($body.PSObject.Properties.Name) -contains 'replies' -and $null -ne $body.replies) { $replies = @($body.replies) }
        $actual = "status=$status ticketId=$($body.ticketId) userId=$($body.userId) status=$($body.status) replies=$($replies.Count)"
        if ([string]$body.ticketId -ne $TicketId) { $problems += "the detail is for ticket '$($body.ticketId)', expected '$TicketId'" }
        if ([string]$body.userId -ne $OwnerUserId) { $problems += "the detail reports user '$($body.userId)', expected the fixture owner" }
        if ([int]$body.status -ne 1) { $problems += "the new ticket reports status $($body.status), expected 1" }
        if ($replies.Count -ne 0) { $problems += "a freshly submitted ticket already has $($replies.Count) replies" }
        if (@($body.PSObject.Properties.Name) -contains 'statusChangedByUserId' -and $null -ne $body.statusChangedByUserId) { $problems += 'a new ticket already reports a status-changing admin' }
        if (@($body.PSObject.Properties.Name) -contains 'statusChangedOnUtc' -and $null -ne $body.statusChangedOnUtc) { $problems += 'a new ticket already reports a status-change instant' }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()

    $ownerDetailNewAssert = ({
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $replies = @()
        if (@($body.PSObject.Properties.Name) -contains 'replies' -and $null -ne $body.replies) { $replies = @($body.replies) }
        $actual = "status=$status ticketId=$($body.ticketId) status=$($body.status) replies=$($replies.Count)"
        if ([string]$body.ticketId -ne $TicketId) { $problems += 'the owner detail is for a different ticket' }
        if ([string]$body.subject -ne $helpers.SubmittedSubject) { $problems += "the owner detail subject is '$($body.subject)', expected the submitted subject" }
        if ([string]$body.message -ne $helpers.SubmittedMessage) { $problems += 'the owner detail message does not match the submitted message' }
        if ([int]$body.status -ne 1) { $problems += "the owner detail reports status $($body.status), expected 1" }
        if ($replies.Count -ne 0) { $problems += 'a freshly submitted ticket already shows replies to its owner' }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()

    $afterRefusedAssert = ({
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $names = @($body.PSObject.Properties.Name)
        $instant = 'null'
        if (($names -contains 'statusChangedOnUtc') -and $null -ne $body.statusChangedOnUtc) { $instant = [string]$body.statusChangedOnUtc }
        $actual = "status=$status status=$($body.status) statusChangedOnUtc=$instant"
        if ([int]$body.status -ne 1) { $problems += "the refused no-op changed the status to $($body.status)" }
        if (($names -contains 'statusChangedOnUtc') -and $null -ne $body.statusChangedOnUtc) { $problems += 'the refused no-op recorded a status-change instant' }
        if (($names -contains 'statusChangedByUserId') -and $null -ne $body.statusChangedByUserId) { $problems += 'the refused no-op recorded a status-changing admin' }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()

    $actorRecordedAssert = ({
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $names = @($body.PSObject.Properties.Name)
        $actor = ''
        if ($names -contains 'statusChangedByUserId') { $actor = [string]$body.statusChangedByUserId }
        $instant = ''
        if ($names -contains 'statusChangedOnUtc') { $instant = [string]$body.statusChangedOnUtc }
        $actual = "status=$status status=$($body.status) statusChangedByUserId=$actor statusChangedOnUtc=$instant"
        if ([int]$body.status -ne 2) { $problems += "the detail reports status $($body.status), expected 2 (InProgress)" }
        if ($actor -ne $AdminUserId) { $problems += "statusChangedByUserId is '$actor', expected the fixture SupportAdmin" }
        if ([string]::IsNullOrWhiteSpace($instant)) { $problems += 'the accepted status change recorded no instant' }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()

    $replyAssert = ({
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $parsed = [Guid]::Empty
        $actual = "status=$status replyId=$($body.replyId) actorUserId=$($body.actorUserId)"
        if (-not [Guid]::TryParse([string]$body.replyId, [ref]$parsed)) { $problems += 'the reply id is not a uuid' }
        if ([string]$body.actorUserId -ne $AdminUserId) { $problems += "the reply actor is '$($body.actorUserId)', expected the fixture SupportAdmin" }
        if ([string]$body.body -ne $helpers.ReplyBody) { $problems += "the reply body is '$($body.body)', expected the submitted body" }
        if ([string]::IsNullOrWhiteSpace([string]$body.createdOnUtc)) { $problems += 'the reply carries no created instant' }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()

    $ownerSeesReplyAssert = ({
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $replies = @()
        if (@($body.PSObject.Properties.Name) -contains 'replies' -and $null -ne $body.replies) { $replies = @($body.replies) }
        $actual = "status=$status replies=$($replies.Count) first=$(if ($replies.Count -ge 1) { [string]$replies[0].body } else { 'none' })"
        if ($replies.Count -ne 1) {
            $problems += "the owner detail reports $($replies.Count) replies, expected 1"
        }
        else {
            if ([string]$replies[0].body -ne $helpers.ReplyBody) { $problems += 'the reply the owner reads back does not match the admin reply' }
            if ([string]$replies[0].actorUserId -ne $AdminUserId) { $problems += 'the reply the owner reads back reports a different actor' }
        }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()

    $closedDetailAssert = ({
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $names = @($body.PSObject.Properties.Name)
        $replies = @()
        if (($names -contains 'replies') -and $null -ne $body.replies) { $replies = @($body.replies) }
        $actor = ''
        if ($names -contains 'statusChangedByUserId') { $actor = [string]$body.statusChangedByUserId }
        $instant = ''
        if ($names -contains 'statusChangedOnUtc') { $instant = [string]$body.statusChangedOnUtc }
        $actual = "status=$status status=$($body.status) replies=$($replies.Count) statusChangedByUserId=$actor"
        if ([int]$body.status -ne 4) { $problems += "the detail reports status $($body.status), expected 4 (Closed)" }
        if ($replies.Count -ne 1) { $problems += "the refused reply to the closed ticket left $($replies.Count) replies, expected the single earlier reply" }
        if ($actor -ne $AdminUserId) { $problems += 'the closing actor was not retained' }
        if ([string]::IsNullOrWhiteSpace($instant)) { $problems += 'the closing transition recorded no instant' }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()

    $closedFilterAssert = ({
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $items = @()
        if (@($body.PSObject.Properties.Name) -contains 'items' -and $null -ne $body.items) { $items = @($body.items) }
        $match = @($items | Where-Object { [string]$_.ticketId -eq $TicketId })
        $actual = "status=$status items=$($items.Count) containsClosedTicket=$($match.Count -eq 1)"
        if ($match.Count -ne 1) { $problems += 'the status=4 filter does not return the ticket just closed' }
        foreach ($item in $items) {
            if ([int]$item.status -ne 4) { $problems += "the status=4 filter returned a row with status $($item.status)" }
        }
        if ($match.Count -eq 1 -and [int]$match[0].status -ne 4) { $problems += 'the returned row does not report status 4' }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()

    return @(
        [ordered]@{
            Name           = 'read.admin-detail-new-ticket'
            Method         = 'GET'
            Path           = $detailRoute
            Actor          = 'support-admin'
            Token          = $AdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 for the submitted ticket: status 1, the fixture owner, no replies, no status audit'
            Assert         = $newDetailAssert
        }
        [ordered]@{
            Name           = 'read.owner-detail-own-ticket'
            Method         = 'GET'
            Path           = $myDetailRoute
            Actor          = 'ticket-owner'
            Token          = $OwnerToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 with the owner''s own subject/message, status 1 and no replies yet'
            Assert         = $ownerDetailNewAssert
        }
        [ordered]@{
            Name           = 'security.other-user-ticket-hidden'
            Method         = 'GET'
            Path           = $myDetailRoute
            Actor          = 'other-family'
            Token          = $OtherToken
            ExpectedStatus = 404
            Expected       = 'HTTP 404 Identity.Support.NotFound: another user''s ticket id never returns data'
            Assert         = (New-F5CodeAssert -Code 'Identity.Support.NotFound')
        }
        [ordered]@{
            Name           = 'write.status-undefined-value'
            Method         = 'POST'
            Path           = $statusRoute
            Actor          = 'support-admin'
            Token          = $AdminToken
            Body           = (@{ status = 9 } | ConvertTo-Json -Compress)
            ExpectedStatus = 400
            Expected       = 'HTTP 400 because 9 is outside the defined New/InProgress/Resolved/Closed values'
            Assert         = $plain.Plain
        }
        [ordered]@{
            Name           = 'write.status-same-value-refused'
            Method         = 'POST'
            Path           = $statusRoute
            Actor          = 'support-admin'
            Token          = $AdminToken
            Body           = (@{ status = 1 } | ConvertTo-Json -Compress)
            ExpectedStatus = 409
            Expected       = 'HTTP 409 Identity.Support.Conflict because New -> New is a no-op, not a transition'
            Assert         = (New-F5CodeAssert -Code 'Identity.Support.Conflict')
        }
        [ordered]@{
            Name           = 'read.admin-detail-after-refused-noop'
            Method         = 'GET'
            Path           = $detailRoute
            Actor          = 'support-admin'
            Token          = $AdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 with the ticket still New and no recorded actor or instant: the refused no-op wrote nothing'
            Assert         = $afterRefusedAssert
        }
        [ordered]@{
            Name           = 'write.status-new-to-inprogress'
            Method         = 'POST'
            Path           = $statusRoute
            Actor          = 'support-admin'
            Token          = $AdminToken
            Body           = (@{ status = 2 } | ConvertTo-Json -Compress)
            ExpectedStatus = 204
            Expected       = 'HTTP 204 for the forward New -> InProgress transition'
            Assert         = $plain.Plain
        }
        [ordered]@{
            Name           = 'read.admin-detail-actor-recorded'
            Method         = 'GET'
            Path           = $detailRoute
            Actor          = 'support-admin'
            Token          = $AdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 reporting InProgress with the acting admin and a UTC instant'
            Assert         = $actorRecordedAssert
        }
        [ordered]@{
            Name           = 'write.reply-on-inprogress-ticket'
            Method         = 'POST'
            Path           = $replyRoute
            Actor          = 'support-admin'
            Token          = $AdminToken
            Body           = (@{ body = $helpers.ReplyBody } | ConvertTo-Json -Compress)
            ExpectedStatus = 200
            Expected       = 'HTTP 200 returning the durable reply with its id, the acting admin and a UTC instant'
            Assert         = $replyAssert
        }
        [ordered]@{
            Name           = 'read.owner-detail-sees-reply'
            Method         = 'GET'
            Path           = $myDetailRoute
            Actor          = 'ticket-owner'
            Token          = $OwnerToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 whose owner detail contains the single admin reply with its actor'
            Assert         = $ownerSeesReplyAssert
        }
        [ordered]@{
            Name           = 'write.status-inprogress-to-resolved'
            Method         = 'POST'
            Path           = $statusRoute
            Actor          = 'support-admin'
            Token          = $AdminToken
            Body           = (@{ status = 3 } | ConvertTo-Json -Compress)
            ExpectedStatus = 204
            Expected       = 'HTTP 204 for the forward InProgress -> Resolved transition'
            Assert         = $plain.Plain
        }
        [ordered]@{
            Name           = 'write.status-resolved-to-closed'
            Method         = 'POST'
            Path           = $statusRoute
            Actor          = 'support-admin'
            Token          = $AdminToken
            Body           = (@{ status = 4 } | ConvertTo-Json -Compress)
            ExpectedStatus = 204
            Expected       = 'HTTP 204 for the forward Resolved -> Closed transition'
            Assert         = $plain.Plain
        }
        [ordered]@{
            Name           = 'conflict.reply-to-closed-ticket'
            Method         = 'POST'
            Path           = $replyRoute
            Actor          = 'support-admin'
            Token          = $AdminToken
            Body           = (@{ body = 'Too late.' } | ConvertTo-Json -Compress)
            ExpectedStatus = 409
            Expected       = 'HTTP 409 Identity.Support.Conflict because a closed ticket cannot be replied to'
            Assert         = (New-F5CodeAssert -Code 'Identity.Support.Conflict')
        }
        [ordered]@{
            Name           = 'conflict.closed-is-terminal'
            Method         = 'POST'
            Path           = $statusRoute
            Actor          = 'support-admin'
            Token          = $AdminToken
            Body           = (@{ status = 2 } | ConvertTo-Json -Compress)
            ExpectedStatus = 409
            Expected       = 'HTTP 409 Identity.Support.Conflict because nothing leaves Closed'
            Assert         = (New-F5CodeAssert -Code 'Identity.Support.Conflict')
        }
        [ordered]@{
            Name           = 'read.admin-detail-closed-unchanged'
            Method         = 'GET'
            Path           = $detailRoute
            Actor          = 'support-admin'
            Token          = $AdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 still Closed with exactly the one earlier reply: both refused writes changed nothing'
            Assert         = $closedDetailAssert
        }
        [ordered]@{
            Name           = 'read.admin-list-closed-filter'
            Method         = 'GET'
            Path           = ($helpers.AdminList + '?status=4')
            Actor          = 'support-admin'
            Token          = $AdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 whose status=4 filter returns the ticket the run just closed'
            Assert         = $closedFilterAssert
        }
    )
}

function Get-F5SessionIdentityDefinition {
    <#
        The signed-in actors this run creates. Every one is logged out again.
    #>
    [CmdletBinding()]
    param()

    $definition = Get-F5FixtureDefinition

    return [ordered]@{
        'support-admin'    = [ordered]@{
            Email      = $definition.SupportAdmin.Email
            DeviceName = 'f5-support-inbox-admin'
            NewSession = $true
        }
        'ticket-owner'     = [ordered]@{
            Email      = $definition.Owner.Email
            DeviceName = 'f5-support-inbox-owner'
            NewSession = $true
        }
        'other-family'     = [ordered]@{
            Email      = $definition.Other.Email
            DeviceName = 'f5-support-inbox-other'
            NewSession = $true
        }
        'care-home-owner'  = [ordered]@{
            Email      = $definition.CareHomeOwner.Email
            DeviceName = 'f5-support-inbox-carehome'
            NewSession = $true
        }
    }
}

# ---------------------------------------------------------------------------
# Runtime helpers
# ---------------------------------------------------------------------------

function Get-F5PsqlPath {
    <#
        Locates the local PostgreSQL client. A missing client fails the run
        closed before any write.
    #>
    [CmdletBinding()]
    param()

    $command = Get-Command 'psql' -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $candidates = @()
    foreach ($root in @('C:\Program Files\PostgreSQL', 'C:\Program Files (x86)\PostgreSQL', '/usr/lib/postgresql', '/usr/pgsql')) {
        if (Test-Path -LiteralPath $root) {
            $candidates += Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
                Sort-Object -Property Name -Descending |
                ForEach-Object { Join-Path $_.FullName 'bin\psql.exe' }
        }
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }

    return $null
}

function Get-F5SafeOutputSummary {
    [CmdletBinding()]
    param([string[]]$Lines, [int]$MaxLength = 300)

    $all = @($Lines)
    for ($index = $all.Count - 1; $index -ge 0; $index--) {
        if ([string]::IsNullOrWhiteSpace($all[$index])) { continue }
        return (Get-TrimmedRedactedLine -Text $all[$index] -MaxLength $MaxLength)
    }
    return 'no client output'
}

function Invoke-F5PsqlProcess {
    <#
        Runs one psql script against the resolved local database with the
        connection supplied through the standard connection environment, then
        restores the caller environment and console encoding.
    #>
    [CmdletBinding()]
    param(
        [string]$PsqlPath,
        [hashtable]$Connection,
        [string]$Sql,
        [string]$Tag
    )

    $tempFile = Join-Path ([System.IO.Path]::GetTempPath()) ("ahlena-f5-" + $Tag + "-" + [Guid]::NewGuid().ToString('N') + ".sql")
    [System.IO.File]::WriteAllText($tempFile, $Sql, (New-Object System.Text.UTF8Encoding($false)))

    $overrides = [ordered]@{
        'PGHOST'            = [string]$Connection.Host
        'PGPORT'            = [string]$Connection.Port
        'PGDATABASE'        = [string]$Connection.Database
        'PGUSER'            = [string]$Connection.Username
        'PGSSLMODE'         = 'disable'
        'PGCONNECT_TIMEOUT' = '5'
        'PGCLIENTENCODING'  = 'UTF8'
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$Connection.Password)) {
        $overrides['PGPASSWORD'] = [string]$Connection.Password
    }

    $saved = @{}
    foreach ($key in $overrides.Keys) { $saved[$key] = [Environment]::GetEnvironmentVariable($key) }
    $savedOutputEncoding = $null
    $savedConsoleEncoding = $null
    $exitCode = -1
    $output = @()

    try {
        foreach ($key in $overrides.Keys) { [Environment]::SetEnvironmentVariable($key, [string]$overrides[$key]) }
        $utf8 = New-Object System.Text.UTF8Encoding($false)
        $savedOutputEncoding = $OutputEncoding
        $OutputEncoding = $utf8
        try {
            $savedConsoleEncoding = [Console]::OutputEncoding
            [Console]::OutputEncoding = $utf8
        }
        catch {
            $savedConsoleEncoding = $null
        }

        $output = @(& $PsqlPath '-X' '-q' '-v' 'ON_ERROR_STOP=1' '-A' '-t' '-f' $tempFile 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        foreach ($key in $saved.Keys) {
            # An unset variable must really be removed: a child process that
            # inherits an empty PGCLIENTENCODING makes PostgreSQL reject the
            # connection outright.
            $savedValue = [string]$saved[$key]
            if ([string]::IsNullOrEmpty($savedValue)) {
                Remove-Item -LiteralPath ("Env:" + $key) -ErrorAction SilentlyContinue
            }
            else {
                [Environment]::SetEnvironmentVariable($key, $savedValue)
            }
        }
        if ($null -ne $savedOutputEncoding) { $OutputEncoding = $savedOutputEncoding }
        if ($null -ne $savedConsoleEncoding) {
            try { [Console]::OutputEncoding = $savedConsoleEncoding } catch { }
        }
        Remove-Item -LiteralPath $tempFile -Force -ErrorAction SilentlyContinue
    }

    return @{
        ExitCode = $exitCode
        Lines    = @($output | ForEach-Object { [string]$_ })
    }
}

function Invoke-F5PsqlQuery {
    [CmdletBinding()]
    param(
        [string]$PsqlPath,
        [hashtable]$Connection,
        [string]$Sql,
        [string]$Tag
    )

    $run = Invoke-F5PsqlProcess -PsqlPath $PsqlPath -Connection $Connection -Sql $Sql -Tag $Tag
    if ($run.ExitCode -ne 0) {
        return @{ Ok = $false; Value = $null; ErrorSummary = "psql exited with $($run.ExitCode): $(Get-F5SafeOutputSummary -Lines $run.Lines)" }
    }

    $text = (@($run.Lines) -join "`n").Trim()
    $value = ConvertFrom-SafeJson $text
    if ($null -eq $value) {
        return @{ Ok = $false; Value = $null; ErrorSummary = 'the read-only query did not return parseable JSON' }
    }

    return @{ Ok = $true; Value = $value; ErrorSummary = '' }
}

function Invoke-F5PsqlScript {
    [CmdletBinding()]
    param(
        [string]$PsqlPath,
        [hashtable]$Connection,
        [string]$Sql,
        [string]$Tag
    )

    $run = Invoke-F5PsqlProcess -PsqlPath $PsqlPath -Connection $Connection -Sql $Sql -Tag $Tag
    if ($run.ExitCode -ne 0) {
        return @{ Ok = $false; ErrorSummary = "psql exited with $($run.ExitCode): $(Get-F5SafeOutputSummary -Lines $run.Lines)" }
    }

    return @{ Ok = $true; ErrorSummary = '' }
}

function Invoke-F5PlannedChecks {
    <#
        Sends one planned check (GET or POST with an optional JSON body) and
        evaluates it with the L1 outcome evaluator. Parsed responses are
        returned separately so the caller can capture the submitted ticket id
        without printing any response body in the report.
    #>
    [CmdletBinding()]
    param(
        [System.Net.Http.HttpClient]$Client,
        [string]$Url,
        [hashtable[]]$Plan,
        [string[]]$LogPath
    )

    $results = @()
    $responses = @{}

    foreach ($check in $Plan) {
        $requestBody = $null
        if ($check.ContainsKey('Body') -and $null -ne $check.Body) { $requestBody = [string]$check.Body }

        $response = Invoke-AhlenaHttpRequest -Client $Client -Method $check.Method -Url "$Url$($check.Path)" -Body $requestBody -Token $check.Token
        $parsed = ConvertFrom-SafeJson $response.Body
        $responses[$check.Name] = $parsed

        $traceId = $null
        if ($null -ne $parsed -and $parsed.PSObject.Properties.Name -contains 'traceId') { $traceId = [string]$parsed.traceId }

        $location = $null
        if ($traceId) { $location = Get-TraceLocation -TraceId $traceId -LogLines (Get-CapturedLogLines -Path $LogPath) }

        # A broken assertion must fail its own check, never the batch: the whole
        # point of the run is to see every result.
        try {
            $results += Get-CheckOutcome -Check $check -Status $response.Status -BodyText $response.Body -TraceId $traceId -Location $location
        }
        catch {
            $assertionError = 'the check assertion threw: ' + (Get-TrimmedRedactedLine $_.Exception.Message)
            $results += [ordered]@{
                name             = $check.Name
                method           = $check.Method
                path             = $check.Path
                actor            = $check.Actor
                expected         = $check.Expected
                actual           = $assertionError
                passed           = $false
                problems         = @($assertionError)
                traceId          = $traceId
                traceCorrelation = 'no-trace-id'
                localLocation    = $location
            }
        }
    }

    return @{ Results = $results; Responses = $responses }
}

function New-F5LogoutChecks {
    [CmdletBinding()]
    param([hashtable]$Tokens)

    $checks = @()
    foreach ($actor in $Tokens.Keys) {
        $checks += [ordered]@{
            Name           = "session.logout.$actor"
            Method         = 'POST'
            Path           = '/api/v1/auth/sessions/logout'
            Actor          = $actor
            Token          = $Tokens[$actor]
            ExpectedStatus = 204
            Expected       = 'HTTP 204, revoking the session this run created'
            Assert         = {
                param($status, $body)
                return @{ Actual = "status=$status"; Problems = @() }
            }
        }
    }

    return $checks
}

function Write-F5Result {
    <#
        One JSON report for the run. It carries the target preflight, the
        fixture guard, the fixtures, the checks and the notes; it never carries
        tokens, credentials, connection strings or response bodies.
    #>
    [CmdletBinding()]
    param(
        [bool]$Passed,
        [string]$Phase,
        [AllowNull()][object]$FixtureReport
    )

    return [ordered]@{
        workflow              = 'f5-support-inbox-verification'
        head                  = $head
        baseUrl               = $baseUrl
        phase                 = $Phase
        apiStarted            = ($null -ne $script:apiProcess) -or ($script:canRunChecks -and $apiReused)
        apiReused             = $apiReused
        databaseSourceName    = $connectionSource.Source
        passed                = $Passed
        targetVerification    = [ordered]@{
            passed       = $preflight.passed
            failedChecks = @($preflight.failedChecks)
            report       = $preflight.report
        }
        fixtureGuard          = [ordered]@{
            passed    = $guard.Ok
            problems  = @($guard.Problems)
            psqlFound = (-not [string]::IsNullOrWhiteSpace($psqlPath))
        }
        fixtures              = $FixtureReport
        submittedTicket       = [ordered]@{
            id              = $ticketId
            ownerTicketCount = $ownerTicketCount
        }
        checks                = $checkResults
        results               = [ordered]@{
            total  = $checkResults.Count
            passed = @($checkResults | Where-Object { $_.passed }).Count
            failed = @($checkResults | Where-Object { -not $_.passed }).Count
        }
        notes                 = $failureNotes
        limitation            = @(
            'Startup migrations stay disabled for every process this script starts; migration state is only read, so the Identity AddSupportTicketWorkflow migration must already be applied.',
            'A local API start also starts the repository''s existing outbox/notification background services. SMS, SMTP and Firebase inputs are cleared so no provider can be contacted, and push runs the Development no-op sender.',
            'Trace correlation is reported as unavailable when no captured local log line carries the trace id of a failed check.',
            'Each run appends one support ticket and one reply to the persistent local database. Both are retained history: nothing this script writes is updated, deleted or reset.'
        )
    }
}

# ---------------------------------------------------------------------------

if ($f5LibraryOnly) { return }

# ---------------------------------------------------------------------------
# Main flow
# ---------------------------------------------------------------------------

$repoRoot = Get-RepositoryRoot

if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'AGENTS.md') -PathType Leaf)) {
    throw 'Run this script from the Ahlena API repository checkout.'
}

$head = Get-PinnedHead $repoRoot
$uri = Assert-LoopbackBaseUrl $f5BaseUrl
$baseUrl = "$($uri.Scheme)://$($uri.Authority)"

$apiDirectory = Join-Path $repoRoot 'src/API/Ahlena.API'
if ([string]::IsNullOrWhiteSpace($f5ApiAssemblyPath)) {
    $f5ApiAssemblyPath = Join-Path $apiDirectory 'bin/Release/net10.0/Ahlena.API.dll'
}
$assemblyPath = Resolve-FullPath $f5ApiAssemblyPath
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw 'The built API assembly was not found. Build Release first (dotnet build Ahlena.slnx -c Release).'
}

$seedEnvironment = Get-LocalFixtureEnvironment -Password $f5FixturePassword -EnableSeed
$serveEnvironment = Get-LocalFixtureEnvironment -Password $f5FixturePassword

$failureNotes = @()
$checkResults = @()
$tokens = @{}
$fixtureState = $null
$ticketId = ''
$ownerTicketCount = 0
$apiLogPaths = @()
$apiProcess = $null
$canRunChecks = $false
$client = $null
$apiReused = Test-BaseUrlReachable -Url $baseUrl

# 1. Read-only target verification before any write.
$preflight = Invoke-TargetVerification -RepoRoot $repoRoot -AssemblyPath $assemblyPath -Environment $seedEnvironment -ApiDirectory $apiDirectory

$psqlPath = Get-F5PsqlPath
$connectionSource = Resolve-F5ConnectionStringSource -RepoRoot $repoRoot
$connection = ConvertFrom-F5PostgresConnectionString -ConnectionString $connectionSource.Value
if (@($connection.Problems).Count -gt 0) {
    $failureNotes += ('The local database connection could not be resolved: ' + (@($connection.Problems) -join '; '))
}

$guard = Test-F5FixtureTarget -VerificationPassed $preflight.passed -Report $preflight.report -Connection $connection -PsqlPath $psqlPath

try {
    if (-not $preflight.passed -or -not $guard.Ok) {
        $failureNotes += 'The read-only target verification or the fixture guard failed; no seed, no API start and no database write occurred.'
        Write-F5Result -Passed $false -Phase 'target-verification' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    if ($f5SkipFixtureWrite) {
        $reportPath = Join-Path ([System.IO.Path]::GetTempPath()) ('ahlena-f5-fixtures-' + [Guid]::NewGuid().ToString('N') + '.sql')
        [System.IO.File]::WriteAllText($reportPath, (Get-F5FixtureSql), (New-Object System.Text.UTF8Encoding($false)))
        $failureNotes += "SkipFixtureWrite is set: the provisioning SQL was written to $reportPath and nothing else ran."
        Write-F5Result -Passed $false -Phase 'report-only' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    # 2. Provision the fixtures, then read them back.
    $fixtureWrite = Invoke-F5PsqlScript -PsqlPath $psqlPath -Connection $connection -Sql (Get-F5FixtureSql) -Tag 'provision'
    if (-not $fixtureWrite.Ok) {
        $failureNotes += ('The fixture provisioning failed: ' + $fixtureWrite.ErrorSummary)
        Write-F5Result -Passed $false -Phase 'fixture-provisioning' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    $stateRead = Invoke-F5PsqlQuery -PsqlPath $psqlPath -Connection $connection -Sql (Get-F5StateQuery) -Tag 'state'
    if (-not $stateRead.Ok) {
        $failureNotes += ('The fixture readback failed: ' + $stateRead.ErrorSummary)
        Write-F5Result -Passed $false -Phase 'fixture-readback' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    $fixtureState = $stateRead.Value
    $definition = Get-F5FixtureDefinition
    $adminAccountTypes = @(([string]$fixtureState.supportAdminAccountTypes) -split ',')
    $ownerAccountTypes = @(([string]$fixtureState.ownerAccountTypes) -split ',')
    $careHomeAccountTypes = @(([string]$fixtureState.careHomeOwnerAccountTypes) -split ',')
    if ([string]::IsNullOrWhiteSpace([string]$fixtureState.supportAdminUserId) -or
        [string]::IsNullOrWhiteSpace([string]$fixtureState.ownerUserId) -or
        [string]::IsNullOrWhiteSpace([string]$fixtureState.otherUserId) -or
        [string]::IsNullOrWhiteSpace([string]$fixtureState.careHomeOwnerUserId) -or
        [int]$fixtureState.supportAdminStatus -ne 2 -or
        [int]$fixtureState.ownerStatus -ne 2 -or
        $adminAccountTypes -notcontains [string]$definition.SupportAdmin.AccountType -or
        $ownerAccountTypes -notcontains [string]$definition.Owner.AccountType -or
        $careHomeAccountTypes -notcontains [string]$definition.CareHomeOwner.AccountType) {
        $failureNotes += 'The fixture readback does not match the provisioned definition.'
        Write-F5Result -Passed $false -Phase 'fixture-readback' -FixtureReport $fixtureState | ConvertTo-Json -Depth 8
        exit 1
    }

    $ownerTicketCount = [int]$fixtureState.ownerTicketCount

    # 3. Start or reuse the API once.
    if (-not $apiReused -and -not $f5SkipApiStart) {
        $logDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('ahlena-f5-verify-' + [Guid]::NewGuid().ToString('N'))
        [System.IO.Directory]::CreateDirectory($logDirectory) | Out-Null
        $outputLog = Join-Path $logDirectory 'api.stdout.log'
        $errorLog = Join-Path $logDirectory 'api.stderr.log'

        $apiProcess = Start-FixtureApiProcess -AssemblyPath $assemblyPath -WorkingDirectory $apiDirectory -Url $baseUrl -Environment $serveEnvironment -OutputLogPath $outputLog -ErrorLogPath $errorLog

        $ready = Wait-ForEndpoint -Url $baseUrl -TimeoutSeconds 90 -Process $apiProcess
        if (-not $ready) {
            if ($apiProcess -and -not $apiProcess.HasExited) {
                Stop-Process -Id $apiProcess.Id -Force
                Wait-Process -Id $apiProcess.Id -Timeout 15 -ErrorAction SilentlyContinue
            }
            $apiProcess = $null
            $logLines = @(Get-CapturedLogLines -Path @($outputLog, $errorLog))
            $lastLogLine = $logLines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1
            if ($lastLogLine) { $failureNotes += ('Last captured API log line: ' + (Get-TrimmedRedactedLine $lastLogLine)) }
            $failureNotes += 'The Development API did not become reachable within 90 seconds.'
            Write-F5Result -Passed $false -Phase 'api-start' -FixtureReport $fixtureState | ConvertTo-Json -Depth 8
            exit 1
        }

        $apiLogPaths = @($outputLog, $errorLog)
    }

    $canRunChecks = $true
    $client = New-AhlenaHttpClient

    # 4. Login preflight for every actor this run needs.
    $sessions = Get-F5SessionIdentityDefinition
    foreach ($actor in $sessions.Keys) {
        $tokens[$actor] = Get-FixtureToken -Client $client -Url $baseUrl `
            -Email $sessions[$actor].Email -Password $f5FixturePassword -DeviceName $sessions[$actor].DeviceName
    }

    foreach ($actor in $tokens.Keys) {
        if ([string]::IsNullOrWhiteSpace([string]$tokens[$actor])) {
            $failureNotes += "The fixture identity '$actor' could not obtain a session; its checks were skipped."
        }
    }

    if ($failureNotes.Count -gt 0) {
        $failureNotes += 'At least one fixture session is unavailable, so the HTTP checks were not run.'
    }
    else {
        # 5. Stage one: the checks that need no captured value, submission last.
        $stageOne = Invoke-F5PlannedChecks -Client $client -Url $baseUrl -LogPath $apiLogPaths -Plan @(
            New-F5CheckPlanStageOne `
                -AdminToken $tokens['support-admin'] `
                -OwnerToken $tokens['ticket-owner'] `
                -OtherToken $tokens['other-family'] `
                -CareHomeOwnerToken $tokens['care-home-owner']
        )
        $checkResults += $stageOne.Results

        $submitted = $stageOne.Responses['write.submit-support-request']
        if ($null -ne $submitted -and $submitted.PSObject.Properties.Name -contains 'ticketId') {
            $ticket = $submitted.ticketId
            if ($null -ne $ticket -and @($ticket.PSObject.Properties.Name) -contains 'value') { $ticketId = [string]$ticket.value }
            else { $ticketId = [string]$ticket }
        }

        # 6. Stage two: the lifecycle, the reply and the refusals.
        if ([string]::IsNullOrWhiteSpace($ticketId)) {
            $failureNotes += 'The submission returned no ticket id, so the lifecycle checks were not run.'
        }
        else {
            $stageTwo = Invoke-F5PlannedChecks -Client $client -Url $baseUrl -LogPath $apiLogPaths -Plan @(
                New-F5CheckPlanStageTwo `
                    -AdminToken $tokens['support-admin'] `
                    -OwnerToken $tokens['ticket-owner'] `
                    -OtherToken $tokens['other-family'] `
                    -TicketId $ticketId `
                    -OwnerUserId ([string]$fixtureState.ownerUserId) `
                    -AdminUserId ([string]$fixtureState.supportAdminUserId)
            )
            $checkResults += $stageTwo.Results
        }
    }
}
catch {
    $failureNotes += ('The verification could not complete: ' + (Get-TrimmedRedactedLine $_.Exception.Message))
}
finally {
    # 7. Log out every session this run created, then stop any API this script
    #    started so a failed run cannot leave an orphaned listener behind.
    if ($client -and $tokens.Count -gt 0) {
        try {
            $logout = Invoke-F5PlannedChecks -Client $client -Url $baseUrl -LogPath $apiLogPaths -Plan @(New-F5LogoutChecks -Tokens $tokens)
            $checkResults += $logout.Results
        }
        catch {
            $failureNotes += ('Session logout could not complete: ' + (Get-TrimmedRedactedLine $_.Exception.Message))
        }
    }

    if ($client) { $client.Dispose() }
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force
        Wait-Process -Id $apiProcess.Id -Timeout 15 -ErrorAction SilentlyContinue
    }
}

$failedCount = @($checkResults | Where-Object { -not $_.passed }).Count
$overallPassed = ($failureNotes.Count -eq 0) -and ($failedCount -eq 0) -and $canRunChecks

Write-F5Result -Passed $overallPassed -Phase 'complete' -FixtureReport $fixtureState | ConvertTo-Json -Depth 8

if (-not $overallPassed) { exit 1 }
