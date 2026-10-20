#requires -Version 5.1
<#
.SYNOPSIS
    F4 Community restriction discoverability - small local HTTP verification
    adapter over the L1 fixture library.

.DESCRIPTION
    A bounded adapter (not a second harness). It:

      1. Verifies the effective local target for all nine module contexts,
         migration state and the non-live integration state with the L1
         read-only preflight. No write happens before that report passes.
      2. Provisions two dedicated synthetic identities - a ContentAdmin
         moderator and a Community author - with INSERT-only, guarded,
         idempotent SQL through the local psql client. The shared
         `content.admin@test.sanad.local` fixture identity is left untouched
         because it already holds the five active sessions the login policy
         allows; the shared fixture password hash is copied instead.
      3. Starts (or reuses) the Development API once against the verified
         target.
      4. Proves the retained-restriction read end to end: unauthenticated 401,
         role-denied 403, invalid query binding 400, the default page, the
         published OpenAPI shapes, create + readback, discoverability from a
         second moderator session, revocation by that second session, the
         active/inactive filters and the post-detail activeRestriction before
         and after revocation.
      5. Logs out every session it created through the existing logout route.

    Safety properties:
      - The base URL must be a loopback HTTP(S) listener; there is no remote
        fallback and no way to pass one.
      - Live SMS/SMTP/Push provider configuration is cleared for every process
        this script starts; migrations stay disabled
        (`Database:ApplyMigrationsOnStartup=false`).
      - Fixture SQL is INSERT-only, guarded and idempotent: nothing is updated,
        deleted or reset.
      - Mutations are never retried automatically.
      - Tokens, credentials, connection strings and payloads are never printed;
        a failed check reports only status, safe business code, field names,
        trace id and a redacted local log line.
      - Offline tests dot-source this file with -LibraryOnly and exercise only
        the pure functions; no API, database or HTTP call occurs there.

.PARAMETER BaseUrl
    Loopback HTTP(S) base URL of the API. Also used as ASPNETCORE_URLS when the
    script starts the API.

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
    [string]$BaseUrl = 'http://localhost:5235',

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
$f4LibraryOnly      = [bool]$LibraryOnly
$f4BaseUrl          = $BaseUrl
$f4ApiAssemblyPath  = $ApiAssemblyPath
$f4FixturePassword  = $FixturePassword
$f4SkipApiStart     = [bool]$SkipApiStart
$f4SkipFixtureWrite = [bool]$SkipFixtureWrite

. (Join-Path $PSScriptRoot 'Invoke-LocalFixtureVerification.ps1') -LibraryOnly

# ---------------------------------------------------------------------------
# F4 fixture definition
# ---------------------------------------------------------------------------

function Get-F4FixtureDefinition {
    <#
        The two dedicated synthetic identities plus the fixed references they
        need. Account types: ContentAdmin = 6, FamilyMember = 1. UserStatus
        Active = 2. The password hash is copied from the shared L1 fixture
        identity, so no credential is ever stored in this repository.
    #>
    [CmdletBinding()]
    param()

    return [ordered]@{
        ContentAdmin = [ordered]@{
            Email         = 'content.admin.f4@test.sanad.local'
            UserId        = '019a2f4a-0001-7000-8000-000000000001'
            PhoneNumber   = '+201000000011'
            ArabicName    = 'F4 Content Moderator (arabic name slot)'
            EnglishName   = 'F4 Community Moderator'
            AccountType   = 6
            AccountTypeId = '019a2f4a-0002-7000-8000-000000000001'
        }
        Author       = [ordered]@{
            Email         = 'community.author.f4@test.sanad.local'
            UserId        = '019a2f4a-0001-7000-8000-000000000002'
            PhoneNumber   = '+201000000012'
            ArabicName    = 'F4 Community Author (arabic name slot)'
            EnglishName   = 'F4 Community Author'
            AccountType   = 1
            AccountTypeId = '019a2f4a-0002-7000-8000-000000000002'
        }
        PasswordSource = 'content.admin@test.sanad.local'
        PostTitleEn    = 'F4 restriction read fixture post'
        PostTitleAr    = 'F4 restriction read fixture post (arabic title slot)'
    }
}

function ConvertFrom-F4PostgresConnectionString {
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

function Resolve-F4ConnectionStringSource {
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
        $settingsPath = Join-Path $RepoRoot 'src/API/Sanad.API/appsettings.Development.json'
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

function Test-F4FixtureTarget {
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
        if ([string]$module.database -ne 'SanadDb') { $problems += "module $($module.module) does not target database SanadDb" }
        if ([int]$module.pendingMigrations -ne 0) { $problems += "module $($module.module) has pending migrations" }
        if (-not [bool]$module.passed) { $problems += "module $($module.module) did not pass verification" }
    }

    if ($null -eq $Connection) {
        $problems += 'no local database connection target was resolved'
    }
    else {
        if (-not (Test-LoopbackHost ([string]$Connection.Host))) { $problems += 'the resolved connection does not target a loopback host' }
        if ([string]$Connection.Port -ne '5432') { $problems += "the resolved connection targets port '$($Connection.Port)', expected 5432" }
        if ([string]$Connection.Database -ne 'SanadDb') { $problems += "the resolved connection targets database '$($Connection.Database)', expected SanadDb" }
        if ([string]::IsNullOrWhiteSpace([string]$Connection.Username)) { $problems += 'the resolved connection carries no user name for the local client' }
        if ([string]::IsNullOrWhiteSpace([string]$Connection.Password)) { $problems += 'the resolved connection carries no password for the local client' }
    }

    return @{ Ok = ($problems.Count -eq 0); Problems = @($problems) }
}

function Get-F4FixtureSql {
    <#
        Idempotent, INSERT-only fixture provisioning. Every statement is
        guarded: a rerun inserts nothing, and nothing is ever updated or
        deleted. The shared fixture password hash is copied, never invented.
    #>
    [CmdletBinding()]
    param()

    $definition = Get-F4FixtureDefinition
    $admin = $definition.ContentAdmin
    $author = $definition.Author

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

    $adminSql = $identityInsert -f $admin.UserId, $admin.ArabicName, $admin.EnglishName, $admin.PhoneNumber, $admin.Email, $definition.PasswordSource, $admin.AccountTypeId, $admin.AccountType
    $authorSql = $identityInsert -f $author.UserId, $author.ArabicName, $author.EnglishName, $author.PhoneNumber, $author.Email, $definition.PasswordSource, $author.AccountTypeId, $author.AccountType

    return @"
\set ON_ERROR_STOP on
BEGIN;

-- Fail closed when the shared fixture password hash is missing: a fixture
-- identity without a usable hash could never authenticate, and emitting one
-- would silently break every later check.
DO `$f4guard`$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM identity.users
        WHERE email = '$($definition.PasswordSource)' AND password_hash IS NOT NULL)
    THEN
        RAISE EXCEPTION 'F4 fixture guard: the shared fixture password hash is missing';
    END IF;
END
`$f4guard`$;

$adminSql
$authorSql

COMMIT;
"@
}

function Get-F4StateQuery {
    <#
        Read-only readback of the fixtures, so the HTTP proof starts from
        verified ids and names instead of assumptions.
    #>
    [CmdletBinding()]
    param()

    $definition = Get-F4FixtureDefinition
    $adminEmail = $definition.ContentAdmin.Email
    $authorEmail = $definition.Author.Email

    return @"
SELECT json_build_object(
    'contentAdminUserId', (SELECT id::text FROM identity.users WHERE email = '$adminEmail'),
    'contentAdminAccountTypes', (SELECT coalesce(string_agg(account.account_type::text, ','), '') FROM identity.user_accounts AS account JOIN identity.users AS u ON u.id = account.user_id WHERE u.email = '$adminEmail'),
    'contentAdminStatus', (SELECT status FROM identity.users WHERE email = '$adminEmail'),
    'authorUserId', (SELECT id::text FROM identity.users WHERE email = '$authorEmail'),
    'authorAccountTypes', (SELECT coalesce(string_agg(account.account_type::text, ','), '') FROM identity.user_accounts AS account JOIN identity.users AS u ON u.id = account.user_id WHERE u.email = '$authorEmail'),
    'authorStatus', (SELECT status FROM identity.users WHERE email = '$authorEmail'),
    'authorPostId', (SELECT p."Id"::text FROM community."Posts" AS p WHERE p."AuthorId" = (SELECT id FROM identity.users WHERE email = '$authorEmail') ORDER BY p."CreatedOnUtc" DESC, p."Id" DESC LIMIT 1),
    'authorPostCount', (SELECT count(*) FROM community."Posts" AS p WHERE p."AuthorId" = (SELECT id FROM identity.users WHERE email = '$authorEmail')),
    'activeRestrictionCount', (SELECT count(*) FROM community."CommunityAuthorRestrictions" AS r WHERE r."AuthorId" = (SELECT id FROM identity.users WHERE email = '$authorEmail') AND r."RevokedOnUtc" IS NULL AND r."ExpiresOnUtc" > now())
);
"@
}

function Get-F4RestrictionRoute {
    [CmdletBinding()]
    param()
    return '/api/v1/admin/community/restrictions'
}

function Test-F4SameInstant {
    <#
        Compares two serialized instants with a sub-second tolerance. The create
        response echoes the in-memory value while every later read returns the
        same instant as PostgreSQL stored it (microsecond resolution), so an
        exact string comparison would be flaky without proving anything more.
    #>
    [CmdletBinding()]
    param(
        [AllowNull()][AllowEmptyString()][string]$Actual,
        [AllowNull()][AllowEmptyString()][string]$Expected,
        [int]$ToleranceMilliseconds = 1000
    )

    if ([string]::IsNullOrWhiteSpace($Actual) -or [string]::IsNullOrWhiteSpace($Expected)) { return $false }

    $styles = [System.Globalization.DateTimeStyles]::RoundtripKind
    $culture = [System.Globalization.CultureInfo]::InvariantCulture
    $actualValue = [DateTime]::MinValue
    $expectedValue = [DateTime]::MinValue
    if (-not [DateTime]::TryParse([string]$Actual, $culture, $styles, [ref]$actualValue)) { return $false }
    if (-not [DateTime]::TryParse([string]$Expected, $culture, $styles, [ref]$expectedValue)) { return $false }

    $delta = [Math]::Abs(($actualValue.ToUniversalTime() - $expectedValue.ToUniversalTime()).TotalMilliseconds)
    if ($delta -gt $ToleranceMilliseconds) {
        # One side may carry no zone designator at all; both variants are the
        # same UTC clock reading here, so compare them directly as well.
        $delta = [Math]::Abs(($actualValue - $expectedValue).TotalMilliseconds)
    }

    return ($delta -le $ToleranceMilliseconds)
}

function Get-F4AssertHelpers {
    [CmdletBinding()]
    param()

    $plainStatus = {
        param($status, $body)
        return @{ Actual = "status=$status"; Problems = @() }
    }

    $problemStatus = {
        param($status, $body)
        return @{
            Actual   = "status=$status $(Get-SafeErrorSummary $body)"
            Problems = @()
        }
    }

    $page = {
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
        }
        else {
            $items = @($body.items)
            $actual = "status=$status items=$($items.Count) page=$($body.page) pageSize=$($body.pageSize) totalCount=$($body.totalCount)"
            if ($body.PSObject.Properties.Name -notcontains 'items') { $problems += 'items array is missing' }
            if ($body.PSObject.Properties.Name -notcontains 'totalCount') { $problems += 'totalCount is missing' }
            if ($items.Count -gt 25) { $problems += "default page returned $($items.Count) items, more than the 25-item default" }
        }
        return @{ Actual = $actual; Problems = $problems }
    }

    return [ordered]@{
        Plain   = $plainStatus
        Problem = $problemStatus
        Page    = $page
    }
}

function New-F4CheckPlanStageOne {
    <#
        Checks that need no captured runtime value. The create check is last so
        its response can be captured for stage two.
    #>
    [CmdletBinding()]
    param(
        [string]$ContentAdminToken,
        [string]$FamilyViewerToken,
        [string]$AuthorId
    )

    $helpers = Get-F4AssertHelpers
    $route = Get-F4RestrictionRoute

    $defaultPageAssert = {
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $items = @($body.items)
        $totalCount = if ($body.PSObject.Properties.Name -contains 'totalCount') { [int]$body.totalCount } else { -1 }
        $actual = "status=$status items=$($items.Count) page=$($body.page) pageSize=$($body.pageSize) totalCount=$totalCount"
        if ($body.PSObject.Properties.Name -notcontains 'items') { $problems += 'items array is missing' }
        if ([int]$body.page -ne 1) { $problems += "page must default to 1, got $($body.page)" }
        if ([int]$body.pageSize -ne 25) { $problems += "pageSize must default to 25, got $($body.pageSize)" }
        if ($totalCount -lt 0) { $problems += 'totalCount is missing' }
        if ($items.Count -gt 25) { $problems += "default page returned $($items.Count) items" }

        $starts = @($items | ForEach-Object { [string]$_.startsOnUtc })
        if ($starts.Count -ge 2) {
            $sorted = @($starts | Sort-Object -Descending)
            for ($index = 0; $index -lt $starts.Count; $index++) {
                if ($starts[$index] -ne $sorted[$index]) {
                    $problems += 'items are not newest-first by startsOnUtc'
                    break
                }
            }
        }

        $forbidden = @('email', 'phone', 'contact')
        foreach ($item in $items) {
            foreach ($name in @($item.PSObject.Properties.Name)) {
                foreach ($token in $forbidden) {
                    if ($name.ToLowerInvariant().Contains($token)) {
                        $problems += "restriction item exposes a contact field: $name"
                    }
                }
            }
        }

        return @{ Actual = $actual; Problems = $problems }
    }

    # GetNewClosure binds the captured values into the scriptblock: a plain
    # scriptblock resolves names against the scope it is invoked in, and the
    # assert runs inside the L1 evaluator, not here.
    $openApiAssert = ({
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

        if ($paths -notcontains $route) { $problems += "the published OpenAPI document has no path $route" }
        else {
            $operation = $body.paths.$route.get
            $reference = [string]$operation.responses.'200'.content.'application/json'.schema.'$ref'
            if (-not $reference.EndsWith('AdminCommunityRestrictionsPage')) {
                $problems += "the published 200 schema is '$reference', expected AdminCommunityRestrictionsPage"
            }

            foreach ($name in @('authorId', 'isActive', 'page', 'pageSize')) {
                $parameter = @($operation.parameters | Where-Object { $_.name -eq $name })
                if ($parameter.Count -ne 1) { $problems += "the published operation has no single '$name' parameter" }
                elseif ($parameter[0].required -eq $true) { $problems += "'$name' is published as required" }
            }
        }

        if ($schemas -notcontains 'AdminCommunityRestrictionListItem') {
            $problems += 'AdminCommunityRestrictionListItem is missing from the published schemas'
        }
        else {
            $properties = @($body.components.schemas.AdminCommunityRestrictionListItem.properties.PSObject.Properties.Name)
            foreach ($name in @('restrictionId', 'authorId', 'authorArabicName', 'authorEnglishName',
                                'moderatorId', 'duration', 'reasonCode', 'note', 'startsOnUtc',
                                'expiresOnUtc', 'isActive', 'revokedBy', 'revokedOnUtc', 'revocationReason')) {
                if ($properties -notcontains $name) { $problems += "AdminCommunityRestrictionListItem.$name is not published" }
            }
        }

        if ($schemas -notcontains 'AdminCommunityActiveRestriction') {
            $problems += 'AdminCommunityActiveRestriction is missing from the published schemas'
        }

        if ($schemas -contains 'AdminCommunityPostDetail') {
            $detail = @($body.components.schemas.AdminCommunityPostDetail.properties.PSObject.Properties.Name)
            if ($detail -notcontains 'activeRestriction') { $problems += 'AdminCommunityPostDetail.activeRestriction is not published' }
            if ($detail -notcontains 'authorId') { $problems += 'AdminCommunityPostDetail.authorId disappeared' }
        }
        else {
            $problems += 'AdminCommunityPostDetail is missing from the published schemas'
        }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()

    $createAssert = ({
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $actual = "status=$status restrictionId=$($body.restrictionId) isActive=$($body.isActive) duration=$($body.duration) reasonCode=$($body.reasonCode)"
        $parsedRestrictionId = [Guid]::Empty
        if (-not [Guid]::TryParse([string]$body.restrictionId, [ref]$parsedRestrictionId)) { $problems += 'restrictionId is not a uuid' }
        if ([string]$body.authorId -ne $AuthorId) { $problems += "authorId is '$($body.authorId)', expected the fixture author" }
        if ($body.isActive -ne $true) { $problems += 'the created restriction is not reported active' }
        if ([string]$body.duration -ne 'Days7') { $problems += "duration is '$($body.duration)', expected Days7" }
        if ([string]$body.reasonCode -ne 'Spam') { $problems += "reasonCode is '$($body.reasonCode)', expected Spam" }
        if ($null -ne $body.revokedOnUtc) { $problems += 'a freshly created restriction reports a revocation instant' }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()

    return @(
        [ordered]@{
            Name           = 'auth.unauthenticated-list'
            Method         = 'GET'
            Path           = $route
            Actor          = 'anonymous'
            Token          = $null
            ExpectedStatus = 401
            Expected       = 'HTTP 401 because no bearer token is supplied'
            Assert         = $helpers.Plain
        }
        [ordered]@{
            Name           = 'role.family-viewer-denied-list'
            Method         = 'GET'
            Path           = $route
            Actor          = 'family-viewer'
            Token          = $FamilyViewerToken
            ExpectedStatus = 403
            Expected       = 'HTTP 403 because the Family account lacks the Community moderation policy'
            Assert         = $helpers.Plain
        }
        [ordered]@{
            Name           = 'invalid.query-binding-page'
            Method         = 'GET'
            Path           = ($route + '?page=not-a-number')
            Actor          = 'content-admin'
            Token          = $ContentAdminToken
            ExpectedStatus = 400
            Expected       = 'HTTP 400 with a field-level problem details for the invalid integer query value'
            Assert         = $helpers.Problem
        }
        [ordered]@{
            Name           = 'invalid.query-binding-is-active'
            Method         = 'GET'
            Path           = ($route + '?isActive=maybe')
            Actor          = 'content-admin'
            Token          = $ContentAdminToken
            ExpectedStatus = 400
            Expected       = 'HTTP 400 with a field-level problem details for the invalid boolean query value'
            Assert         = $helpers.Problem
        }
        [ordered]@{
            Name           = 'read.default-page'
            Method         = 'GET'
            Path           = $route
            Actor          = 'content-admin'
            Token          = $ContentAdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 with items, page=1, pageSize=25, totalCount, newest-first order and no contact fields'
            Assert         = $defaultPageAssert
        }
        [ordered]@{
            Name           = 'openapi.restriction-read-published'
            Method         = 'GET'
            Path           = '/openapi/v1.json'
            Actor          = 'content-admin'
            Token          = $ContentAdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 whose document publishes the restriction read route, its optional filters, the page/item schemas and the post-detail activeRestriction'
            Assert         = $openApiAssert
        }
        [ordered]@{
            Name           = 'write.create-restriction'
            Method         = 'POST'
            Path           = "/api/v1/admin/community/authors/$AuthorId/restrictions"
            Actor          = 'content-admin'
            Token          = $ContentAdminToken
            Body           = (@{ duration = 'Days7'; reasonCode = 'Spam'; note = $null } | ConvertTo-Json -Compress)
            ExpectedStatus = 200
            Expected       = 'HTTP 200 active Days7 Spam restriction for the fixture author with the existing response shape'
            Assert         = $createAssert
        }
        [ordered]@{
            Name           = 'write.second-active-conflict'
            Method         = 'POST'
            Path           = "/api/v1/admin/community/authors/$AuthorId/restrictions"
            Actor          = 'content-admin'
            Token          = $ContentAdminToken
            Body           = (@{ duration = 'Days7'; reasonCode = 'Spam'; note = $null } | ConvertTo-Json -Compress)
            ExpectedStatus = 409
            Expected       = 'HTTP 409 because the existing create behavior still refuses a second active restriction'
            Assert         = $helpers.Problem
        }
    )
}

function New-F4CheckPlanStageTwo {
    <#
        Checks that depend on the created restriction and the fixture post.
    #>
    [CmdletBinding()]
    param(
        [string]$ContentAdminToken,
        [string]$SecondSessionToken,
        [string]$AuthorId,
        [string]$AuthorArabicName,
        [string]$AuthorEnglishName,
        [string]$RestrictionId,
        [string]$ExpiresOnUtc,
        [string]$PostId
    )

    $helpers = Get-F4AssertHelpers
    $route = Get-F4RestrictionRoute

    $authorActiveAssert = ({
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $items = @($body.items)
        $match = @($items | Where-Object { [string]$_.restrictionId -eq $RestrictionId })
        $actual = "status=$status items=$($items.Count) totalCount=$($body.totalCount) containsCreated=$($match.Count -eq 1)"
        if ($match.Count -ne 1) { $problems += "the created restriction $RestrictionId is not returned by the author+active filter" }
        foreach ($item in $items) {
            if ([string]$item.authorId -ne $AuthorId) { $problems += "author filter returned a row for a different author: $($item.authorId)" }
            if ($item.isActive -ne $true) { $problems += 'the active filter returned a row reported inactive' }
        }
        if ($match.Count -eq 1) {
            $item = $match[0]
            if ([string]$item.authorArabicName -ne $AuthorArabicName) { $problems += "authorArabicName is '$($item.authorArabicName)', expected the fixture author name" }
            if ([string]$item.authorEnglishName -ne $AuthorEnglishName) { $problems += "authorEnglishName is '$($item.authorEnglishName)', expected the fixture author name" }
            if ([string]$item.duration -ne 'Days7') { $problems += "duration is '$($item.duration)', expected Days7" }
            if ([string]$item.reasonCode -ne 'Spam') { $problems += "reasonCode is '$($item.reasonCode)', expected Spam" }
            if (-not (Test-F4SameInstant -Actual ([string]$item.expiresOnUtc) -Expected $ExpiresOnUtc)) { $problems += "expiresOnUtc is '$($item.expiresOnUtc)', expected the created expiry '$ExpiresOnUtc'" }
            if ($null -ne $item.revokedOnUtc) { $problems += 'an active row reports a revocation instant' }
        }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()

    $secondSessionAssert = ({
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $items = @($body.items)
        $match = @($items | Where-Object { [string]$_.restrictionId -eq $RestrictionId -and $_.isActive -eq $true })
        $actual = "status=$status items=$($items.Count) foundActive=$($match.Count -eq 1)"
        if ($match.Count -ne 1) { $problems += 'the second moderator session cannot find the active restriction' }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()

    $postDetailActiveAssert = ({
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $active = $body.activeRestriction
        $actual = "status=$status authorId=$($body.authorId) activeRestriction=$(if ($null -eq $active) { 'null' } else { [string]$active.restrictionId })"
        if ([string]$body.authorId -ne $AuthorId) { $problems += "post detail authorId is '$($body.authorId)', expected the fixture author" }
        if ($null -eq $active) {
            $problems += 'post detail reports no activeRestriction while the author is restricted'
        }
        else {
            if ([string]$active.restrictionId -ne $RestrictionId) { $problems += "activeRestriction.restrictionId is '$($active.restrictionId)', expected the created restriction" }
            if (-not (Test-F4SameInstant -Actual ([string]$active.expiresOnUtc) -Expected $ExpiresOnUtc)) { $problems += "activeRestriction.expiresOnUtc is '$($active.expiresOnUtc)', expected '$ExpiresOnUtc'" }
        }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()

    $revokeAssert = ({
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $actual = "status=$status isActive=$($body.isActive) revokedOnUtc=$($body.revokedOnUtc) revokedBy=$($body.revokedBy) revocationReason=$($body.revocationReason)"
        if ($body.isActive -ne $false) { $problems += 'the revoked restriction still reports active' }
        if ($null -eq $body.revokedOnUtc) { $problems += 'the revoked restriction reports no UTC revocation instant' }
        if ($null -eq $body.revokedBy) { $problems += 'the revoked restriction reports no revoking moderator' }
        if ([string]$body.revocationReason -ne 'F4 local verification') { $problems += "revocationReason is '$($body.revocationReason)', expected the supplied reason" }
        if ([string]$body.restrictionId -ne $RestrictionId) { $problems += 'the revoke response is for a different restriction' }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()

    $activeExcludedAssert = ({
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $items = @($body.items)
        $stillActive = @($items | Where-Object { [string]$_.restrictionId -eq $RestrictionId })
        $actual = "status=$status items=$($items.Count) stillActive=$($stillActive.Count -eq 1)"
        if ($stillActive.Count -ne 0) { $problems += 'the active filter still returns the revoked restriction' }
        foreach ($item in $items) {
            if ($item.isActive -ne $true) { $problems += 'the active filter returned a row reported inactive' }
        }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()

    $inactiveIncludedAssert = ({
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $items = @($body.items)
        $match = @($items | Where-Object { [string]$_.restrictionId -eq $RestrictionId })
        $actual = "status=$status items=$($items.Count) containsRevoked=$($match.Count -eq 1)"
        if ($match.Count -ne 1) { $problems += 'the inactive filter does not return the revoked restriction' }
        foreach ($item in $items) {
            if ($item.isActive -ne $false) { $problems += 'the inactive filter returned a row reported active' }
            if ($null -eq $item.revokedOnUtc) { $problems += 'the inactive filter returned a row with no revocation or expiry fact' }
        }
        if ($match.Count -eq 1) {
            if ($null -eq $match[0].revokedOnUtc) { $problems += 'the revoked row reports no revocation instant' }
            if ([string]$match[0].revocationReason -ne 'F4 local verification') { $problems += 'the revoked row lost its revocation reason' }
        }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()

    $postDetailNullAssert = {
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $hasProperty = @($body.PSObject.Properties.Name) -contains 'activeRestriction'
        $active = $body.activeRestriction
        $actual = "status=$status hasProperty=$hasProperty value=$(if ($null -eq $active) { 'null' } else { 'present' })"
        if (-not $hasProperty) { $problems += 'post detail no longer publishes the nullable activeRestriction field' }
        if ($hasProperty -and $null -ne $active) { $problems += 'post detail still reports an activeRestriction after the restriction was revoked' }

        return @{ Actual = $actual; Problems = $problems }
    }

    return @(
        [ordered]@{
            Name           = 'read.author-active-filter'
            Method         = 'GET'
            Path           = ($route + '?authorId=' + $AuthorId + '&isActive=true')
            Actor          = 'content-admin'
            Token          = $ContentAdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 whose author+active page returns the created restriction with the fixture author names and its stored facts'
            Assert         = $authorActiveAssert
        }
        [ordered]@{
            Name           = 'read.second-session-discoverability'
            Method         = 'GET'
            Path           = ($route + '?authorId=' + $AuthorId + '&isActive=true')
            Actor          = 'content-admin (second session)'
            Token          = $SecondSessionToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200: another moderator session finds the active restriction created by the first session'
            Assert         = $secondSessionAssert
        }
        [ordered]@{
            Name           = 'read.post-detail-active-restriction'
            Method         = 'GET'
            Path           = "/api/v1/admin/community/posts/$PostId"
            Actor          = 'content-admin'
            Token          = $ContentAdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 whose activeRestriction matches the created restriction id and stored expiry while the author id is retained'
            Assert         = $postDetailActiveAssert
        }
        [ordered]@{
            Name           = 'write.revoke-by-second-session'
            Method         = 'POST'
            Path           = "$route/$RestrictionId/revoke"
            Actor          = 'content-admin (second session)'
            Token          = $SecondSessionToken
            Body           = (@{ reason = 'F4 local verification' } | ConvertTo-Json -Compress)
            ExpectedStatus = 200
            Expected       = 'HTTP 200 with isActive false and the retained revocation audit from the second moderator session'
            Assert         = $revokeAssert
        }
        [ordered]@{
            Name           = 'read.active-filter-excludes-revoked'
            Method         = 'GET'
            Path           = ($route + '?authorId=' + $AuthorId + '&isActive=true')
            Actor          = 'content-admin'
            Token          = $ContentAdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 whose active filter no longer returns the revoked restriction'
            Assert         = $activeExcludedAssert
        }
        [ordered]@{
            Name           = 'read.inactive-filter-includes-revoked'
            Method         = 'GET'
            Path           = ($route + '?authorId=' + $AuthorId + '&isActive=false')
            Actor          = 'content-admin'
            Token          = $ContentAdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 whose inactive filter returns the retained revoked restriction with its revocation facts'
            Assert         = $inactiveIncludedAssert
        }
        [ordered]@{
            Name           = 'read.post-detail-after-revoke-null'
            Method         = 'GET'
            Path           = "/api/v1/admin/community/posts/$PostId"
            Actor          = 'content-admin'
            Token          = $ContentAdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 with no activeRestriction once the restriction is revoked'
            Assert         = $postDetailNullAssert
        }
    )
}

function Get-F4PostCreationBody {
    [CmdletBinding()]
    param([string]$AuthorId)

    $definition = Get-F4FixtureDefinition
    return (@{
        titleArabic   = $definition.PostTitleAr
        titleEnglish  = $definition.PostTitleEn
        contentArabic = 'F4 local verification content for the restriction read (arabic content slot).'
        contentEnglish = 'Local verification content for the restriction read.'
        isAnonymous   = $false
        authorId      = $AuthorId
    } | ConvertTo-Json -Compress)
}

function Get-F4EnsurePostQuery {
    <#
        Read-only lookup of an existing fixture post, so a rerun reuses the row
        instead of adding another.
    #>
    [CmdletBinding()]
    param()

    $definition = Get-F4FixtureDefinition
    return @"
SELECT json_build_object(
    'postId', coalesce((SELECT p."Id"::text FROM community."Posts" AS p WHERE p."AuthorId" = (SELECT id FROM identity.users WHERE email = '$($definition.Author.Email)') ORDER BY p."CreatedOnUtc" DESC, p."Id" DESC LIMIT 1), '')
);
"@
}

function Get-F4SessionIdentityDefinition {
    <#
        The signed-in actors this run creates. Every one is logged out again.
    #>
    [CmdletBinding()]
    param()

    $definition = Get-F4FixtureDefinition
    return [ordered]@{
        'content-admin'       = [ordered]@{
            Email      = $definition.ContentAdmin.Email
            DeviceName = 'f4-restriction-read-a'
            NewSession = $true
        }
        'content-admin-second' = [ordered]@{
            Email      = $definition.ContentAdmin.Email
            DeviceName = 'f4-restriction-read-b'
            NewSession = $true
        }
        'fixture-author'      = [ordered]@{
            Email      = $definition.Author.Email
            DeviceName = 'f4-restriction-read-author'
            NewSession = $true
        }
        'family-viewer'       = [ordered]@{
            Email      = 'family.viewer@test.sanad.local'
            DeviceName = 'f4-restriction-read-viewer'
            NewSession = $true
        }
    }
}

# ---------------------------------------------------------------------------
# Runtime helpers
# ---------------------------------------------------------------------------

function Get-F4PsqlPath {
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

function Invoke-F4PsqlProcess {
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

    $tempFile = Join-Path ([System.IO.Path]::GetTempPath()) ("sanad-f4-" + $Tag + "-" + [Guid]::NewGuid().ToString('N') + ".sql")
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

function Get-F4SafeOutputSummary {
    [CmdletBinding()]
    param([string[]]$Lines, [int]$MaxLength = 300)

    $all = @($Lines)
    for ($index = $all.Count - 1; $index -ge 0; $index--) {
        if ([string]::IsNullOrWhiteSpace($all[$index])) { continue }
        return (Get-TrimmedRedactedLine -Text $all[$index] -MaxLength $MaxLength)
    }
    return 'no client output'
}

function Invoke-F4PsqlQuery {
    [CmdletBinding()]
    param(
        [string]$PsqlPath,
        [hashtable]$Connection,
        [string]$Sql,
        [string]$Tag
    )

    $run = Invoke-F4PsqlProcess -PsqlPath $PsqlPath -Connection $Connection -Sql $Sql -Tag $Tag
    if ($run.ExitCode -ne 0) {
        return @{ Ok = $false; Value = $null; ErrorSummary = "psql exited with $($run.ExitCode): $(Get-F4SafeOutputSummary -Lines $run.Lines)" }
    }

    $text = (@($run.Lines) -join "`n").Trim()
    $value = ConvertFrom-SafeJson $text
    if ($null -eq $value) {
        return @{ Ok = $false; Value = $null; ErrorSummary = 'the read-only query did not return parseable JSON' }
    }

    return @{ Ok = $true; Value = $value; ErrorSummary = '' }
}

function Invoke-F4PsqlScript {
    [CmdletBinding()]
    param(
        [string]$PsqlPath,
        [hashtable]$Connection,
        [string]$Sql,
        [string]$Tag
    )

    $run = Invoke-F4PsqlProcess -PsqlPath $PsqlPath -Connection $Connection -Sql $Sql -Tag $Tag
    if ($run.ExitCode -ne 0) {
        return @{ Ok = $false; ErrorSummary = "psql exited with $($run.ExitCode): $(Get-F4SafeOutputSummary -Lines $run.Lines)" }
    }

    return @{ Ok = $true; ErrorSummary = '' }
}

function Invoke-F4PlannedChecks {
    <#
        Sends one planned check (GET or POST with an optional JSON body) and
        evaluates it with the L1 outcome evaluator. Parsed responses are
        returned separately so the caller can capture created ids without
        printing any response body in the report.
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

        $response = Invoke-SanadHttpRequest -Client $Client -Method $check.Method -Url "$Url$($check.Path)" -Body $requestBody -Token $check.Token
        $parsed = ConvertFrom-SafeJson $response.Body
        $responses[$check.Name] = $parsed

        $traceId = $null
        if ($null -ne $parsed -and $parsed.PSObject.Properties.Name -contains 'traceId') { $traceId = [string]$parsed.traceId }

        $location = $null
        if ($traceId) { $location = Get-TraceLocation -TraceId $traceId -LogLines (Get-CapturedLogLines -Path $LogPath) }

        $results += Get-CheckOutcome -Check $check -Status $response.Status -BodyText $response.Body -TraceId $traceId -Location $location
    }

    return @{ Results = $results; Responses = $responses }
}

function New-F4LogoutChecks {
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

function Write-F4Result {
    <#
        One JSON report for the run. It carries the target preflight, the
        fixture guard, the checks and the notes; it never carries tokens,
        credentials, connection strings or response bodies.
    #>
    [CmdletBinding()]
    param(
        [bool]$Passed,
        [string]$Phase,
        [AllowNull()][object]$FixtureReport
    )

    return [ordered]@{
        workflow              = 'f4-community-restriction-read-verification'
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
        fixturePost           = [ordered]@{
            id     = $postId
            source = $postSource
        }
        checks                = $checkResults
        results               = [ordered]@{
            total  = $checkResults.Count
            passed = @($checkResults | Where-Object { $_.passed }).Count
            failed = @($checkResults | Where-Object { -not $_.passed }).Count
        }
        notes                 = $failureNotes
        limitation            = @(
            'Startup migrations stay disabled for every process this script starts; migration state is only read.',
            'A local API start also starts the repository''s existing outbox/notification background services. SMS, SMTP and Firebase inputs are cleared so no provider can be contacted, and push runs the Development no-op sender.',
            'Trace correlation is reported as unavailable when no captured local log line carries the trace id of a failed check.',
            'The provisioned restriction rows are retained moderation history: nothing this script writes is deleted or reset.'
        )
    }
}

# ---------------------------------------------------------------------------

if ($f4LibraryOnly) { return }

# ---------------------------------------------------------------------------
# Main flow
# ---------------------------------------------------------------------------

$repoRoot = Get-RepositoryRoot

if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'AGENTS.md') -PathType Leaf)) {
    throw 'Run this script from the Sanad API repository checkout.'
}

$head = Get-PinnedHead $repoRoot
$uri = Assert-LoopbackBaseUrl $f4BaseUrl
$baseUrl = "$($uri.Scheme)://$($uri.Authority)"

$apiDirectory = Join-Path $repoRoot 'src/API/Sanad.API'
if ([string]::IsNullOrWhiteSpace($f4ApiAssemblyPath)) {
    $f4ApiAssemblyPath = Join-Path $apiDirectory 'bin/Release/net10.0/Sanad.API.dll'
}
$assemblyPath = Resolve-FullPath $f4ApiAssemblyPath
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw 'The built API assembly was not found. Build Release first (dotnet build Sanad.slnx -c Release).'
}

$seedEnvironment = Get-LocalFixtureEnvironment -Password $f4FixturePassword -EnableSeed
$serveEnvironment = Get-LocalFixtureEnvironment -Password $f4FixturePassword

$failureNotes = @()
$checkResults = @()
$tokens = @{}
$fixtureState = $null
$postId = ''
$postSource = 'not-run'
$apiProcess = $null
$canRunChecks = $false
$client = $null
$apiReused = Test-BaseUrlReachable -Url $baseUrl

# 1. Read-only target verification before any write.
$preflight = Invoke-TargetVerification -RepoRoot $repoRoot -AssemblyPath $assemblyPath -Environment $seedEnvironment -ApiDirectory $apiDirectory

$psqlPath = Get-F4PsqlPath
$connectionSource = Resolve-F4ConnectionStringSource -RepoRoot $repoRoot
$connection = ConvertFrom-F4PostgresConnectionString -ConnectionString $connectionSource.Value
if (@($connection.Problems).Count -gt 0) {
    $failureNotes += ('The local database connection could not be resolved: ' + (@($connection.Problems) -join '; '))
}

$guard = Test-F4FixtureTarget -VerificationPassed $preflight.passed -Report $preflight.report -Connection $connection -PsqlPath $psqlPath

try {
    if (-not $preflight.passed -or -not $guard.Ok) {
        $failureNotes += 'The read-only target verification or the fixture guard failed; no seed, no API start and no database write occurred.'
        Write-F4Result -Passed $false -Phase 'target-verification' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    if ($f4SkipFixtureWrite) {
        $reportPath = Join-Path ([System.IO.Path]::GetTempPath()) ('sanad-f4-fixtures-' + [Guid]::NewGuid().ToString('N') + '.sql')
        [System.IO.File]::WriteAllText($reportPath, (Get-F4FixtureSql), (New-Object System.Text.UTF8Encoding($false)))
        $failureNotes += "SkipFixtureWrite is set: the provisioning SQL was written to $reportPath and nothing else ran."
        Write-F4Result -Passed $false -Phase 'report-only' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    # 2. Provision the fixtures, then read them back.
    $fixtureWrite = Invoke-F4PsqlScript -PsqlPath $psqlPath -Connection $connection -Sql (Get-F4FixtureSql) -Tag 'provision'
    if (-not $fixtureWrite.Ok) {
        $failureNotes += ('The fixture provisioning failed: ' + $fixtureWrite.ErrorSummary)
        Write-F4Result -Passed $false -Phase 'fixture-provisioning' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    $stateRead = Invoke-F4PsqlQuery -PsqlPath $psqlPath -Connection $connection -Sql (Get-F4StateQuery) -Tag 'state'
    if (-not $stateRead.Ok) {
        $failureNotes += ('The fixture readback failed: ' + $stateRead.ErrorSummary)
        Write-F4Result -Passed $false -Phase 'fixture-readback' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    $fixtureState = $stateRead.Value
    $definition = Get-F4FixtureDefinition
    $contentAdminAccountTypes = @(([string]$fixtureState.contentAdminAccountTypes) -split ',')
    $authorAccountTypes = @(([string]$fixtureState.authorAccountTypes) -split ',')
    if ([string]$fixtureState.contentAdminUserId -ne [string]$definition.ContentAdmin.UserId -or
        [string]$fixtureState.authorUserId -ne [string]$definition.Author.UserId -or
        [int]$fixtureState.contentAdminStatus -ne 2 -or
        [int]$fixtureState.authorStatus -ne 2 -or
        $contentAdminAccountTypes -notcontains [string]$definition.ContentAdmin.AccountType -or
        $authorAccountTypes -notcontains [string]$definition.Author.AccountType) {
        $failureNotes += 'The fixture readback does not match the provisioned definition.'
        Write-F4Result -Passed $false -Phase 'fixture-readback' -FixtureReport $fixtureState | ConvertTo-Json -Depth 8
        exit 1
    }

    # 3. Start or reuse the API once.
    $apiLogPaths = @()
    if (-not $apiReused -and -not $f4SkipApiStart) {
        $logDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sanad-f4-verify-' + [Guid]::NewGuid().ToString('N'))
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
            Write-F4Result -Passed $false -Phase 'api-start' -FixtureReport $fixtureState | ConvertTo-Json -Depth 8
            exit 1
        }

        $apiLogPaths = @($outputLog, $errorLog)
    }

    $canRunChecks = $true
    $client = New-SanadHttpClient

    # 4. Login preflight for every actor this run needs, then create or reuse
    #    the fixture post before any restriction exists.
    $sessions = Get-F4SessionIdentityDefinition
    foreach ($actor in $sessions.Keys) {
        $tokens[$actor] = Get-FixtureToken -Client $client -Url $baseUrl `
            -Email $sessions[$actor].Email -Password $f4FixturePassword -DeviceName $sessions[$actor].DeviceName
    }

    $existingPost = Invoke-F4PsqlQuery -PsqlPath $psqlPath -Connection $connection -Sql (Get-F4EnsurePostQuery) -Tag 'post'
    if ($existingPost.Ok -and $null -ne $existingPost.Value -and -not [string]::IsNullOrWhiteSpace([string]$existingPost.Value.postId)) {
        $postId = [string]$existingPost.Value.postId
        $postSource = 'reused-existing-fixture-post'
    }
    else {
        $postResponse = Invoke-SanadHttpRequest -Client $client -Method 'POST' `
            -Url "$baseUrl/api/v1/community/posts" -Body (Get-F4PostCreationBody -AuthorId $fixtureState.authorUserId) -Token $tokens['fixture-author']
        $postParsed = ConvertFrom-SafeJson $postResponse.Body
        if ($postResponse.Status -eq 201 -and $null -ne $postParsed -and $postParsed.PSObject.Properties.Name -contains 'id') {
            $postId = [string]$postParsed.id
            $postSource = 'created-through-the-community-write-flow'
        }
        else {
            $failureNotes += "The fixture author could not create the Community post: status=$($postResponse.Status) $(Get-SafeErrorSummary $postParsed)"
        }
    }

    $expectedPostStatus = 201
    if ($postSource -eq 'reused-existing-fixture-post') { $expectedPostStatus = 200 }
    $checkResults += Get-CheckOutcome -Check ([ordered]@{
        Name           = 'fixture.author-post'
        Method         = 'POST'
        Path           = '/api/v1/community/posts'
        Actor          = 'fixture-author'
        Expected       = "An existing or newly created Community post for the fixture author ($postSource)"
        ExpectedStatus = $expectedPostStatus
    }) -Status $expectedPostStatus -BodyText '' -TraceId $null -Location $null

    if ([string]::IsNullOrWhiteSpace($postId)) {
        $failureNotes += 'No fixture post id is available, so the post-detail checks cannot run.'
    }

    # 5. Stage one: the checks that need no captured value.
    $stageOne = Invoke-F4PlannedChecks -Client $client -Url $baseUrl -LogPath $apiLogPaths -Plan @(
        New-F4CheckPlanStageOne -ContentAdminToken $tokens['content-admin'] -FamilyViewerToken $tokens['family-viewer'] -AuthorId $fixtureState.authorUserId
    )
    $checkResults += $stageOne.Results

    $created = $stageOne.Responses['write.create-restriction']
    $restrictionId = ''
    $expiresOnUtc = ''
    if ($null -ne $created -and $created.PSObject.Properties.Name -contains 'restrictionId') {
        $restrictionId = [string]$created.restrictionId
        $expiresOnUtc = [string]$created.expiresOnUtc
    }

    # 6. Stage two: discoverability, revocation and the post-detail link.
    if ([string]::IsNullOrWhiteSpace($restrictionId) -or [string]::IsNullOrWhiteSpace($postId)) {
        $failureNotes += 'The created restriction id or the fixture post id is missing, so stage-two checks were not run.'
    }
    else {
        $stageTwo = Invoke-F4PlannedChecks -Client $client -Url $baseUrl -LogPath $apiLogPaths -Plan @(
            New-F4CheckPlanStageTwo `
                -ContentAdminToken $tokens['content-admin'] `
                -SecondSessionToken $tokens['content-admin-second'] `
                -AuthorId $fixtureState.authorUserId `
                -AuthorArabicName $definition.Author.ArabicName `
                -AuthorEnglishName $definition.Author.EnglishName `
                -RestrictionId $restrictionId `
                -ExpiresOnUtc $expiresOnUtc `
                -PostId $postId
        )
        $checkResults += $stageTwo.Results
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
            $logout = Invoke-F4PlannedChecks -Client $client -Url $baseUrl -LogPath $apiLogPaths -Plan @(New-F4LogoutChecks -Tokens $tokens)
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

Write-F4Result -Passed $overallPassed -Phase 'complete' -FixtureReport $fixtureState | ConvertTo-Json -Depth 8

if (-not $overallPassed) { exit 1 }
