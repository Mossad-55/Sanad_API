#requires -Version 5.1
<#
.SYNOPSIS
    F8 readable notifications - minimal localhost HTTP verification adapter.

.DESCRIPTION
    A bounded adapter, not a second harness. It reuses the L1 library's helpers
    (repository root and pinned head, loopback target assertion, process
    environment overrides, API start/stop, HTTP request, outcome evaluation and
    trace correlation) and adds only what F8 needs:

      1. The L1 read-only preflight report is re-checked fail-closed: all nine
         module contexts must target the loopback PersistentLocal Development
         database with no pending migration, and no live Paymob, SMS, SMTP or
         Push input may be configured. Nothing is written before that gate
         passes.
      2. INSERT-only, guarded SQL through the local psql client adds three
         dedicated synthetic identities: a SupportAdmin admin actor and two
         ticket submitters, one with the persisted UI language Arabic (1) and
         one with English (2). Nothing is updated, deleted, reset or bulk
         seeded, every statement is guarded so a rerun inserts nothing, and no
         ticket, reply, outbox or notification row is ever pre-inserted - a
         seeded "finished" inbox row would prove nothing about a producer.
      3. Starts (or reuses) the Development API once against the verified target
         on a free non-default loopback port.
      4. Drives the real support-reply producer through HTTP: each submitter
         creates its own ticket through POST /api/v1/support/contact, the
         SupportAdmin authors one reply per ticket (one Arabic body, one English
         body) through the admin reply route, and each submitter then reads its
         own inbox and its structured destination back through
         GET /api/v1/notifications. The assertions prove, without printing any
         Arabic text, that each recipient's support-reply title is localized to
         that recipient's persisted language, that the two recipients did not
         receive the same title, that the stored body is the administrator-
         authored body byte for byte, that the destination pair keeps the
         SupportTicket id, and that no generated copy repeats a raw id.
      5. Logs out every session this run created.

    Encoding: this file is saved as UTF-8 **with** BOM on purpose. Windows
    PowerShell 5.1 reads a BOM-less file as ANSI and would mangle the two Arabic
    literals the bilingual assertion compares (the support-reply title and one
    authored reply body). Neither literal is ever printed in a report line:
    failures report booleans and lengths only.

    Safety properties:
      - The base URL must be a loopback HTTP(S) listener; there is no remote
        fallback and no way to pass one.
      - Live SMS/SMTP/Push provider configuration is cleared for every process
        this script starts; migrations stay disabled
        (`Database:ApplyMigrationsOnStartup=false`).
      - Fixture SQL is INSERT-only, guarded and idempotent, and ASCII only.
      - The only writes are the two support submissions and the two admin
        replies this run performs through public HTTP routes; nothing is
        pre-seeded to look like producer output.
      - Tokens, credentials, connection strings and payloads are never printed;
        a failed check reports only status, safe business code, field names,
        trace id and a redacted local log line.
      - Offline tests dot-source this file with -LibraryOnly and exercise only
        the pure helpers; no API, database or HTTP call occurs there.

.PARAMETER BaseUrl
    Loopback HTTP(S) base URL of the API. Defaults to a free port that does not
    collide with the other local adapters (5235/5243/5245/5247/5249). Also used
    as ASPNETCORE_URLS when the script starts the API.

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
    Reports only: the fixture provisioning SQL is written to a file and no
    database write, API start or HTTP check happens.
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:5251',

    [string]$ApiAssemblyPath,

    [string]$FixturePassword = 'Test-1234!',

    [switch]$LibraryOnly,

    [switch]$SkipApiStart,

    [switch]$SkipFixtureWrite
)

$ErrorActionPreference = 'Stop'

# Capture this script's own parameters before dot-sourcing the L1 library:
# dot-sourcing L1 binds its parameter names (BaseUrl, FixturePassword,
# ApiAssemblyPath, SkipApiStart, ...) in this scope and would otherwise
# overwrite the values passed to this script.
$f8LibraryOnly      = [bool]$LibraryOnly
$f8BaseUrl          = $BaseUrl
$f8ApiAssemblyPath  = $ApiAssemblyPath
$f8FixturePassword  = $FixturePassword
$f8SkipApiStart     = [bool]$SkipApiStart
$f8SkipFixtureWrite = [bool]$SkipFixtureWrite

. (Join-Path $PSScriptRoot 'Invoke-LocalFixtureVerification.ps1') -LibraryOnly

# ---------------------------------------------------------------------------
# F8 routes and pinned copy
# ---------------------------------------------------------------------------

function Get-F8Route {
    <#
        The public routes this adapter drives plus the pinned literals the
        bilingual assertions compare. The Arabic title is what the F8 copy
        catalog produces for a recipient whose persisted UI language is Arabic;
        it is held here (not read from the application) so the HTTP evidence
        stands on its own.
    #>
    [CmdletBinding()]
    param()

    return [ordered]@{
        Inbox        = '/api/v1/notifications'
        Contact       = '/api/v1/support/contact'
        AdminReplies  = '/api/v1/admin/support-tickets'
        SupportReplyTitleAr = 'رد الدعم'
        SupportReplyTitleEn = 'Support reply'
        SubmittedSubject     = 'F8 verification support ticket'
        SubmittedMessage     = 'Verification message for the F8 readable-notification adapter.'
        AnonymousReplyBody   = 'F8 verification anonymous reply attempt.'
        AuthoredBodyAr       = 'راجعنا طلبك وسنوافيك بالتفاصيل قريبًا.'
        AuthoredBodyEn       = 'We reviewed your request and will follow up with details shortly.'
        UiLanguageArabic     = 1
        UiLanguageEnglish    = 2
    }
}

# ---------------------------------------------------------------------------
# F8 fixture definition
# ---------------------------------------------------------------------------

function Get-F8FixtureDefinition {
    <#
        The three dedicated synthetic identities plus the pinned references they
        need. Account types (Identity): Family = 1, SupportAdmin = 7. UserStatus
        Active = 2. UiLanguage: Arabic = 1, English = 2.

        The user ids, e-mails, phone numbers and account-type ids are pinned so
        a rerun is a no-op, the names stay ASCII, and the password hash is
        copied from the shared L1 Minimal fixture identity, so no credential is
        ever stored in this repository. The guard fails closed when that
        identity (and therefore the L1 Minimal seed) is absent.

        The Arabic submitter keeps the persisted Arabic language (1) - the
        documented default - and the English submitter persists English (2).
    #>
    [CmdletBinding()]
    param()

    return [ordered]@{
        SupportAdmin     = [ordered]@{
            Email         = 'support.admin.f8@test.sanad.local'
            UserId        = '019a2f8a-0001-7000-8000-000000000001'
            PhoneNumber   = '+201000000096'
            EnglishName   = 'F8 Support Operator'
            ArabicName    = 'F8 Support Operator (arabic name slot)'
            AccountType   = 7
            AccountTypeId = '019a2f8a-0002-7000-8000-000000000001'
            UiLanguage    = 1
        }
        ArabicSubmitter  = [ordered]@{
            Email         = 'arabic.submitter.f8@test.sanad.local'
            UserId        = '019a2f8a-0001-7000-8000-000000000002'
            PhoneNumber   = '+201000000097'
            EnglishName   = 'F8 Arabic Submitter'
            ArabicName    = 'F8 Arabic Submitter (arabic name slot)'
            AccountType   = 1
            AccountTypeId = '019a2f8a-0002-7000-8000-000000000002'
            UiLanguage    = 1
        }
        EnglishSubmitter = [ordered]@{
            Email         = 'english.submitter.f8@test.sanad.local'
            UserId        = '019a2f8a-0001-7000-8000-000000000003'
            PhoneNumber   = '+201000000098'
            EnglishName   = 'F8 English Submitter'
            ArabicName    = 'F8 English Submitter (arabic name slot)'
            AccountType   = 1
            AccountTypeId = '019a2f8a-0002-7000-8000-000000000003'
            UiLanguage    = 2
        }
        PasswordSource = 'content.admin@test.sanad.local'
    }
}

function Get-F8FixtureSql {
    <#
        Idempotent, INSERT-only fixture provisioning. Every statement is
        guarded: a rerun inserts nothing, and nothing is ever updated, deleted
        or reset. No ticket, reply, notification or outbox row is inserted, so
        every support reply this run reads back was produced by the real route.
        The shared fixture password hash is copied, never invented.
    #>
    [CmdletBinding()]
    param()

    $definition = Get-F8FixtureDefinition
    $source = $definition.PasswordSource

    $identityInsert = @"
INSERT INTO identity.users
    (id, arabic_full_name, english_full_name, phone_number, email,
     email_verified, phone_verified, status, ui_language, created_on_utc, updated_on_utc, password_hash)
SELECT '{0}', '{1}', '{2}', '{3}', '{4}', true, true, 2, {8}, now(), now(), source.password_hash
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
    foreach ($name in @('SupportAdmin', 'ArabicSubmitter', 'EnglishSubmitter')) {
        $actor = $definition[$name]
        $fragments += ($identityInsert -f $actor.UserId, $actor.ArabicName, $actor.EnglishName, $actor.PhoneNumber,
            $actor.Email, $source, $actor.AccountTypeId, $actor.AccountType, $actor.UiLanguage)
    }

    return @"
\set ON_ERROR_STOP on
BEGIN;

-- Fail closed when the shared L1 fixture password hash is missing: a fixture
-- identity without a usable hash could never authenticate, and every later
-- check would fail for the wrong reason.
DO `$f8guard`$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM identity.users
        WHERE email = '$source' AND password_hash IS NOT NULL)
    THEN
        RAISE EXCEPTION 'F8 fixture guard: the shared fixture password hash is missing';
    END IF;
END
`$f8guard`$;

$($fragments -join "`n")
COMMIT;
"@
}

function Get-F8StateQuery {
    <#
        Read-only readback of the fixtures, so the HTTP proof starts from
        verified ids, account types and persisted UI languages instead of
        assumptions. The ticket counts are informational: a rerun appends new
        tickets by design and must not fail on retained history.
    #>
    [CmdletBinding()]
    param()

    $definition = Get-F8FixtureDefinition
    $adminEmail = $definition.SupportAdmin.Email
    $arabicEmail = $definition.ArabicSubmitter.Email
    $englishEmail = $definition.EnglishSubmitter.Email
    $source = $definition.PasswordSource

    return @"
SELECT json_build_object(
    'supportAdminUserId', (SELECT id::text FROM identity.users WHERE email = '$adminEmail'),
    'supportAdminAccountTypes', (SELECT coalesce(string_agg(account.account_type::text, ','), '') FROM identity.user_accounts AS account JOIN identity.users AS u ON u.id = account.user_id WHERE u.email = '$adminEmail'),
    'supportAdminStatus', (SELECT status FROM identity.users WHERE email = '$adminEmail'),
    'arabicSubmitterUserId', (SELECT id::text FROM identity.users WHERE email = '$arabicEmail'),
    'arabicSubmitterAccountTypes', (SELECT coalesce(string_agg(account.account_type::text, ','), '') FROM identity.user_accounts AS account JOIN identity.users AS u ON u.id = account.user_id WHERE u.email = '$arabicEmail'),
    'arabicSubmitterStatus', (SELECT status FROM identity.users WHERE email = '$arabicEmail'),
    'arabicSubmitterUiLanguage', (SELECT ui_language FROM identity.users WHERE email = '$arabicEmail'),
    'englishSubmitterUserId', (SELECT id::text FROM identity.users WHERE email = '$englishEmail'),
    'englishSubmitterAccountTypes', (SELECT coalesce(string_agg(account.account_type::text, ','), '') FROM identity.user_accounts AS account JOIN identity.users AS u ON u.id = account.user_id WHERE u.email = '$englishEmail'),
    'englishSubmitterStatus', (SELECT status FROM identity.users WHERE email = '$englishEmail'),
    'englishSubmitterUiLanguage', (SELECT ui_language FROM identity.users WHERE email = '$englishEmail'),
    'passwordSourceUserId', (SELECT id::text FROM identity.users WHERE email = '$source'),
    'arabicSubmitterTicketCount', (SELECT count(*)::int FROM identity.support_tickets WHERE user_id = (SELECT id FROM identity.users WHERE email = '$arabicEmail')),
    'englishSubmitterTicketCount', (SELECT count(*)::int FROM identity.support_tickets WHERE user_id = (SELECT id FROM identity.users WHERE email = '$englishEmail'))
);
"@
}

# ---------------------------------------------------------------------------
# Fail-closed guards (pure)
# ---------------------------------------------------------------------------

function ConvertFrom-F8PostgresConnectionString {
    <#
        Minimal Npgsql-style connection string parser. It returns the parsed
        values so the guard can assert the target before any write; callers must
        never print the password.
    #>
    [CmdletBinding()]
    param([AllowNull()][AllowEmptyString()][string]$ConnectionString)

    $result = [ordered]@{
        Host     = ''
        Port     = '5432'
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

function Resolve-F8ConnectionStringSource {
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

function Test-F8FixtureTarget {
    <#
        Fail-closed guard evaluated before any fixture write: the read-only
        verification must have passed against the persistent local Development
        database for all nine module contexts, no live provider input may be
        configured, and the resolved psql connection must point at the same
        loopback database.
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
    if ([bool]$Report.pushServiceAccountConfigured) { $problems += 'the verified target still has a Push service account configured' }

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

# ---------------------------------------------------------------------------
# Assert helpers (return closures the L1 evaluator can invoke)
# ---------------------------------------------------------------------------

# Note on assertion scope: a check assertion is invoked by the L1
# Get-CheckOutcome in the script scope, so an assertion that is NOT wrapped in
# .GetNewClosure() can call the script-scope helpers below. An assertion that
# IS wrapped needs .GetNewClosure() to bind the values captured while the check
# plan was built, and a closure resolves functions from the global scope only -
# it cannot see this script's functions. Every assertion that binds values
# therefore inlines its own shape checks (and uses only .NET types) instead of
# calling a helper.

function New-F8AssertSet {
    [CmdletBinding()]
    param()

    return [ordered]@{
        # Status only: authentication and authorization failures carry no
        # business code worth asserting.
        Plain   = {
            param($status, $body)
            return @{ Actual = "status=$status"; Problems = @() }
        }
        Problem = {
            param($status, $body)
            return @{ Actual = "status=$status $(Get-SafeErrorSummary $body)"; Problems = @() }
        }
    }
}

function New-F8SubmitAssert {
    <# The submitter's own support submission returns a uuid ticket in status 1. #>
    [CmdletBinding()]
    param()

    return {
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
        $actual = "status=$status ticketIdPresent=$(-not [string]::IsNullOrWhiteSpace($id)) status=$($body.status)"

        $parsed = [Guid]::Empty
        if (-not [Guid]::TryParse($id, [ref]$parsed)) { $problems += 'the submitted ticket id is not a uuid' }
        if ([int]$body.status -ne 1) { $problems += "the submitted ticket status is $($body.status), expected 1 (New)" }
        if ([string]::IsNullOrWhiteSpace([string]$body.submittedOnUtc)) { $problems += 'submittedOnUtc is missing' }

        return @{ Actual = $actual; Problems = $problems }
    }
}

function New-F8ReplyAssert {
    <#
        The admin reply response: uuid reply id, the fixture admin as actor, the
        authored body returned unchanged, and a creation instant. The authored
        body is never echoed - a mismatch reports lengths only.
    #>
    [CmdletBinding()]
    param(
        [string]$ExpectedBody,
        [string]$AdminUserId
    )

    return ({
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $parsed = [Guid]::Empty
        $replyIdOk = [Guid]::TryParse([string]$body.replyId, [ref]$parsed)
        $actorOk = ([string]$body.actorUserId -eq $AdminUserId)
        $observed = [string]$body.body
        $bodyOk = [string]::Equals($observed, $ExpectedBody, [System.StringComparison]::Ordinal)
        $instantOk = -not [string]::IsNullOrWhiteSpace([string]$body.createdOnUtc)
        $actual = "status=$status replyIdUuid=$replyIdOk actorIsFixtureAdmin=$actorOk bodyMatchesAuthoredBytes=$bodyOk createdOnUtcPresent=$instantOk"

        if (-not $replyIdOk) { $problems += 'the reply id is not a uuid' }
        if (-not $actorOk) { $problems += 'the reply actor is not the fixture SupportAdmin' }
        if (-not $bodyOk) { $problems += "the reply the API returned is not the authored body byte for byte (observed length $($observed.Length), expected length $($ExpectedBody.Length))" }
        if (-not $instantOk) { $problems += 'the reply carries no creation instant' }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()
}

function New-F8InboxAssert {
    <#
        The recipient inbox readback for one submitter. It proves:
          - the response is the pinned items/nextCursor/unreadCount page;
          - exactly one item is the support-reply notification for the ticket
            this run submitted (matched on the structured destination id);
          - that item's title is the localized support-reply title for this
            recipient's persisted language;
          - that item's body is the administrator-authored reply byte for byte;
          - the destination pair is SupportTicket + the ticket id, and the copy
            itself repeats no raw id;
          - the row is unread and every row in this dedicated inbox is a support
            reply (this fixture receives no other producer's traffic).

        No Arabic literal, authored body or raw id is ever echoed: problems
        report booleans, counts and lengths.
    #>
    [CmdletBinding()]
    param(
        [string]$ExpectedTitle,
        [string]$ExpectedBody,
        [string]$TicketId,
        [string]$LanguageLabel
    )

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

        $matches = @($items | Where-Object { [string]$_.destinationEntityId -eq $TicketId })
        $item = $null
        if ($matches.Count -eq 1) { $item = $matches[0] }

        $titleOk = $false
        $bodyOk = $false
        $destinationKindOk = $false
        $categoryTypeOk = $false
        $unreadOk = $false
        $copyHasNoIdentifier = $true
        $everyRowIsARepliesRow = $true

        foreach ($row in $items) {
            if (-not ([string]$row.category -eq 'Support' -and [string]$row.type -eq 'SupportReply')) { $everyRowIsARepliesRow = $false }
            $copy = ([string]$row.title + [string]$row.body)
            if ($copy.IndexOf($TicketId, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { $copyHasNoIdentifier = $false }
            if ($copy.IndexOf([string]$row.destinationEntityId, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { $copyHasNoIdentifier = $false }
        }

        if ($null -ne $item) {
            $titleOk = [string]::Equals([string]$item.title, $ExpectedTitle, [System.StringComparison]::Ordinal)
            $bodyOk = [string]::Equals([string]$item.body, $ExpectedBody, [System.StringComparison]::Ordinal)
            $destinationKindOk = ([string]$item.destinationEntityKind -eq 'SupportTicket')
            $categoryTypeOk = ([string]$item.category -eq 'Support' -and [string]$item.type -eq 'SupportReply')
            $unreadOk = ($null -eq $item.readOnUtc)
        }

        $actual = "status=$status items=$($items.Count) matchedDestinationRows=$($matches.Count) titleIsLocalized=$titleOk bodyIsAuthoredBytes=$bodyOk destinationKindOk=$destinationKindOk categoryTypeOk=$categoryTypeOk unread=$unreadOk copyRepeatsNoIdentifier=$copyHasNoIdentifier inboxIsOnlySupportReplies=$everyRowIsARepliesRow"

        if ($names -notcontains 'items') { $problems += 'items array is missing' }
        if ($names -notcontains 'nextCursor') { $problems += 'nextCursor is missing' }
        if ($names -notcontains 'unreadCount') { $problems += 'unreadCount is missing' }
        if ($matches.Count -ne 1) { $problems += "the inbox does not hold exactly one support-reply notification for the ticket this run submitted (matched $($matches.Count))" }
        if (-not $titleOk) { $problems += "the $LanguageLabel recipient's support-reply title is not the localized title for that language (observed length $(([string]$item.title).Length), expected length $($ExpectedTitle.Length))" }
        if (-not $bodyOk) { $problems += 'the stored body is not the administrator-authored reply byte for byte' }
        if (-not $destinationKindOk) { $problems += 'the support-reply notification does not carry the SupportTicket destination kind' }
        if (-not $categoryTypeOk) { $problems += 'the support-reply notification does not carry the Support/SupportReply pair' }
        if (-not $unreadOk) { $problems += 'the freshly produced support-reply notification is already read' }
        if (-not $copyHasNoIdentifier) { $problems += 'generated copy repeats a raw id' }
        if (-not $everyRowIsARepliesRow) { $problems += 'the dedicated submitter inbox holds a row that is not a Support/SupportReply notification' }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()
}

function New-F8BilingualTitleResult {
    <#
        Cross-check over the two inbox reads: each recipient's title must match
        its own language, so the Arabic recipient cannot have received the
        English title by accident and the two titles must differ. Only booleans
        are reported.
    #>
    [CmdletBinding()]
    param(
        [AllowNull()][string]$ArabicTitle,
        [AllowNull()][string]$EnglishTitle,
        [string]$ExpectedTitleAr,
        [string]$ExpectedTitleEn
    )

    $problems = @()
    if ([string]::IsNullOrEmpty($ArabicTitle) -or [string]::IsNullOrEmpty($EnglishTitle)) {
        $problems += 'a support-reply title was missing from one of the two inbox reads, so the bilingual comparison could not run'
        $arabicOk = $false
        $englishOk = $false
        $differ = $false
    }
    else {
        $arabicOk = [string]::Equals($ArabicTitle, $ExpectedTitleAr, [System.StringComparison]::Ordinal)
        $englishOk = [string]::Equals($EnglishTitle, $ExpectedTitleEn, [System.StringComparison]::Ordinal)
        $differ = -not [string]::Equals($ArabicTitle, $EnglishTitle, [System.StringComparison]::Ordinal)
        if (-not $differ) { $problems += 'both recipients received the same support-reply title, so the title is not localized' }
    }

    return [ordered]@{
        name             = 'read.bilingual-support-reply-titles'
        method           = 'GET'
        path             = "$((Get-F8Route).Inbox)?pageSize=20 (both submitter inboxes)"
        actor            = 'arabic-submitter + english-submitter'
        expected         = 'both inbox reads carry the support-reply title of their own persisted UI language and not the same title: Arabic recipient reads the Arabic title, English recipient reads the English title'
        actual           = "arabicTitleIsArabic=$arabicOk englishTitleIsEnglish=$englishOk titlesDiffer=$differ"
        passed           = ($problems.Count -eq 0)
        problems         = @($problems)
        traceId          = $null
        traceCorrelation = 'no-trace-id'
        localLocation    = $null
    }
}

# ---------------------------------------------------------------------------
# Check plan
# ---------------------------------------------------------------------------

function New-F8CheckPlanStageOne {
    <#
        Checks that need no captured runtime value. The two submissions are last
        so their responses can be captured for stage two. The anonymous and
        Family-denied admin-reply attempts run first: the bilingual proof is only
        meaningful when the reply copy is authored by the SupportAdmin and not
        by the recipient.
    #>
    [CmdletBinding()]
    param(
        [string]$AdminToken,
        [string]$ArabicToken,
        [string]$EnglishToken,
        [string]$UnknownGuid
    )

    $routes = Get-F8Route
    $plain = New-F8AssertSet
    $adminReplies = $routes.AdminReplies

    return @(
        [ordered]@{
            Name           = 'auth.unauthenticated-inbox-list'
            Method         = 'GET'
            Path           = "$($routes.Inbox)?pageSize=20"
            Actor          = 'anonymous'
            Token          = $null
            ExpectedStatus = 401
            Expected       = 'HTTP 401 because the recipient inbox takes its recipient from a Normal JWT only'
            Assert         = $plain.Plain
        }
        [ordered]@{
            Name           = 'auth.unauthenticated-admin-reply'
            Method         = 'POST'
            Path           = "$adminReplies/$UnknownGuid/replies"
            Actor          = 'anonymous'
            Token          = $null
            Body           = (@{ body = $routes.AnonymousReplyBody } | ConvertTo-Json -Compress)
            ExpectedStatus = 401
            Expected       = 'HTTP 401 because no bearer token is supplied on the admin reply route'
            Assert         = $plain.Plain
        }
        [ordered]@{
            Name           = 'role.submitter-denied-admin-reply'
            Method         = 'POST'
            Path           = "$adminReplies/$UnknownGuid/replies"
            Actor          = 'arabic-submitter (Family)'
            Token          = $ArabicToken
            Body           = (@{ body = $routes.AnonymousReplyBody } | ConvertTo-Json -Compress)
            ExpectedStatus = 403
            Expected       = 'HTTP 403 because a Family submitter never carries the SupportInboxAdmin policy, so the recipient cannot author the title or body that is later asserted'
            Assert         = $plain.Plain
        }
        [ordered]@{
            Name           = 'write.submit-request.arabic-submitter'
            Method         = 'POST'
            Path           = $routes.Contact
            Actor          = 'arabic-submitter'
            Token          = $ArabicToken
            Body           = (@{ subject = $routes.SubmittedSubject; message = $routes.SubmittedMessage } | ConvertTo-Json -Compress)
            ExpectedStatus = 200
            Expected       = 'HTTP 200 through the real submission route: a uuid ticket id in status 1 for the Arabic recipient'
            Assert         = (New-F8SubmitAssert)
        }
        [ordered]@{
            Name           = 'write.submit-request.english-submitter'
            Method         = 'POST'
            Path           = $routes.Contact
            Actor          = 'english-submitter'
            Token          = $EnglishToken
            Body           = (@{ subject = $routes.SubmittedSubject; message = $routes.SubmittedMessage } | ConvertTo-Json -Compress)
            ExpectedStatus = 200
            Expected       = 'HTTP 200 through the real submission route: a uuid ticket id in status 1 for the English recipient'
            Assert         = (New-F8SubmitAssert)
        }
    )
}

function New-F8CheckPlanStageTwo {
    <#
        Checks that depend on the two tickets submitted in stage one: one admin
        reply per ticket through the real admin route, then each recipient's own
        inbox readback.
    #>
    [CmdletBinding()]
    param(
        [string]$AdminToken,
        [string]$ArabicToken,
        [string]$EnglishToken,
        [string]$ArabicTicketId,
        [string]$EnglishTicketId,
        [string]$AdminUserId
    )

    $routes = Get-F8Route
    $adminReplies = $routes.AdminReplies
    $inbox = "$($routes.Inbox)?pageSize=20"

    return @(
        [ordered]@{
            Name           = 'write.admin-reply.arabic-recipient'
            Method         = 'POST'
            Path           = "$adminReplies/$ArabicTicketId/replies"
            Actor          = 'support-admin'
            Token          = $AdminToken
            Body           = (@{ body = $routes.AuthoredBodyAr } | ConvertTo-Json -Compress)
            ExpectedStatus = 200
            Expected       = 'HTTP 200 returning the authored Arabic reply byte for byte from the fixture SupportAdmin'
            Assert         = (New-F8ReplyAssert -ExpectedBody $routes.AuthoredBodyAr -AdminUserId $AdminUserId)
        }
        [ordered]@{
            Name           = 'write.admin-reply.english-recipient'
            Method         = 'POST'
            Path           = "$adminReplies/$EnglishTicketId/replies"
            Actor          = 'support-admin'
            Token          = $AdminToken
            Body           = (@{ body = $routes.AuthoredBodyEn } | ConvertTo-Json -Compress)
            ExpectedStatus = 200
            Expected       = 'HTTP 200 returning the authored English reply byte for byte from the fixture SupportAdmin'
            Assert         = (New-F8ReplyAssert -ExpectedBody $routes.AuthoredBodyEn -AdminUserId $AdminUserId)
        }
        [ordered]@{
            Name           = 'read.arabic-recipient-inbox'
            Method         = 'GET'
            Path           = $inbox
            Actor          = 'arabic-submitter'
            Token          = $ArabicToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 whose support-reply row for this run''s ticket reads in Arabic, stores the authored Arabic body byte for byte, keeps SupportTicket + ticket id as the destination and repeats no raw id'
            Assert         = (New-F8InboxAssert -ExpectedTitle $routes.SupportReplyTitleAr -ExpectedBody $routes.AuthoredBodyAr -TicketId $ArabicTicketId -LanguageLabel 'Arabic')
        }
        [ordered]@{
            Name           = 'read.english-recipient-inbox'
            Method         = 'GET'
            Path           = $inbox
            Actor          = 'english-submitter'
            Token          = $EnglishToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 whose support-reply row for this run''s ticket reads in English, stores the authored English body byte for byte, keeps SupportTicket + ticket id as the destination and repeats no raw id'
            Assert         = (New-F8InboxAssert -ExpectedTitle $routes.SupportReplyTitleEn -ExpectedBody $routes.AuthoredBodyEn -TicketId $EnglishTicketId -LanguageLabel 'English')
        }
    )
}

function Get-F8SessionIdentityDefinition {
    <#
        The signed-in actors this run creates. Every one is logged out again.
        All three are F8's own dedicated fixtures, so this gate never depends on
        how many sessions a shared identity happens to be holding.
    #>
    [CmdletBinding()]
    param()

    $definition = Get-F8FixtureDefinition
    return [ordered]@{
        'support-admin'     = [ordered]@{
            Email      = $definition.SupportAdmin.Email
            DeviceName = 'f8-readable-notifications-admin'
            NewSession = $true
        }
        'arabic-submitter'  = [ordered]@{
            Email      = $definition.ArabicSubmitter.Email
            DeviceName = 'f8-readable-notifications-arabic'
            NewSession = $true
        }
        'english-submitter' = [ordered]@{
            Email      = $definition.EnglishSubmitter.Email
            DeviceName = 'f8-readable-notifications-english'
            NewSession = $true
        }
    }
}

function New-F8LogoutChecks {
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

# ---------------------------------------------------------------------------
# psql helpers (runtime)
# ---------------------------------------------------------------------------

function Get-F8PsqlPath {
    <#
        Locates the local PostgreSQL client. A missing client fails the run
        closed before any write.
    #>
    [CmdletBinding()]
    param()

    $command = Get-Command 'psql' -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    foreach ($root in @('C:\Program Files\PostgreSQL', 'C:\Program Files (x86)\PostgreSQL', '/usr/lib/postgresql', '/usr/pgsql')) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $candidate = Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
            Sort-Object -Property Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'bin/psql.exe' } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
            Select-Object -First 1
        if ($candidate) { return $candidate }
    }

    return $null
}

function Get-F8SafeOutputSummary {
    [CmdletBinding()]
    param([string[]]$Lines, [int]$MaxLength = 300)

    $all = @($Lines)
    for ($index = $all.Count - 1; $index -ge 0; $index--) {
        if ([string]::IsNullOrWhiteSpace($all[$index])) { continue }
        return (Get-TrimmedRedactedLine -Text $all[$index] -MaxLength $MaxLength)
    }
    return 'no client output'
}

function Invoke-F8Psql {
    <#
        Runs one ASCII-only SQL script against the resolved local database with
        the connection supplied through the standard connection environment,
        then restores the caller environment. With -ExpectJson the last stdout
        line is parsed as the read-only readback value.
    #>
    [CmdletBinding()]
    param(
        [string]$PsqlPath,
        [hashtable]$Connection,
        [string]$Sql,
        [string]$Tag,
        [switch]$ExpectJson
    )

    $tempFile = Join-Path ([System.IO.Path]::GetTempPath()) ("ahlena-f8-" + $Tag + "-" + [Guid]::NewGuid().ToString('N') + ".sql")
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
    $exitCode = -1
    $output = @()

    try {
        foreach ($key in $overrides.Keys) { [Environment]::SetEnvironmentVariable($key, [string]$overrides[$key]) }
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
        Remove-Item -LiteralPath $tempFile -Force -ErrorAction SilentlyContinue
    }

    if ($exitCode -ne 0) {
        return @{ Ok = $false; Value = $null; ErrorSummary = "psql exited with $exitCode`: $(Get-F8SafeOutputSummary -Lines $output)" }
    }

    if (-not $ExpectJson) {
        return @{ Ok = $true; Value = $null; ErrorSummary = '' }
    }

    $text = (@($output) -join "`n").Trim()
    $value = ConvertFrom-SafeJson $text
    if ($null -eq $value) {
        return @{ Ok = $false; Value = $null; ErrorSummary = 'the read-only query did not return parseable JSON' }
    }

    return @{ Ok = $true; Value = $value; ErrorSummary = '' }
}

function Get-F8CapturedTicketId {
    <#
        Reads the ticket id out of one submission response without printing it.
    #>
    [CmdletBinding()]
    param([AllowNull()][object]$Response)

    if ($null -eq $Response) { return '' }
    if (@($Response.PSObject.Properties.Name) -notcontains 'ticketId') { return '' }

    $ticket = $Response.ticketId
    if ($null -ne $ticket -and @($ticket.PSObject.Properties.Name) -contains 'value') { return [string]$ticket.value }
    return [string]$ticket
}

function Invoke-F8PlannedChecks {
    <#
        Sends one planned check (GET or POST with an optional JSON body) and
        evaluates it with the L1 outcome evaluator. Parsed responses are
        returned separately so the caller can capture the two ticket ids and the
        two inbox pages without printing any response body in the report.
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

# ---------------------------------------------------------------------------
# Result writer
# ---------------------------------------------------------------------------

function Write-F8Result {
    <#
        One JSON report for the run. It carries the target preflight, the
        fixture guard, the readback and the checks; it never carries tokens,
        credentials, connection strings, response bodies or Arabic copy.
    #>
    [CmdletBinding()]
    param(
        [bool]$Passed,
        [string]$Phase,
        [AllowNull()][object]$FixtureReport
    )

    return [ordered]@{
        workflow           = 'f8-readable-notifications-verification'
        head               = $head
        baseUrl            = $baseUrl
        phase              = $Phase
        apiStarted         = ($null -ne $script:apiProcess) -or ($script:canRunChecks -and $apiReused)
        apiReused          = $apiReused
        databaseSourceName = $connectionSource.Source
        passed             = $Passed
        targetVerification = [ordered]@{
            passed       = $preflight.passed
            failedChecks = @($preflight.failedChecks)
            report       = $preflight.report
        }
        fixtureGuard       = [ordered]@{
            passed    = $guard.Ok
            problems  = @($guard.Problems)
            psqlFound = (-not [string]::IsNullOrWhiteSpace($psqlPath))
        }
        fixtures           = $FixtureReport
        checks             = $checkResults
        results            = [ordered]@{
            total  = $checkResults.Count
            passed = @($checkResults | Where-Object { $_.passed }).Count
            failed = @($checkResults | Where-Object { -not $_.passed }).Count
        }
        notes              = $failureNotes
        limitation         = @(
            'Startup migrations stay disabled for every process this script starts; migration state is only read, so the Identity AddSupportTicketWorkflow migration and the F8 source must already be built into the Release assembly.',
            'A local API start also starts the repository''s existing outbox/notification background services. SMS, SMTP, Firebase and Push inputs are cleared so no provider can be contacted, and push runs the Development no-op sender.',
            'End-to-end HTTP evidence covers the support-reply producer only: it is the one F8 branch that a public route drives synchronously. The chat, negative check-in, help-request, medication-late, SOS, care-home booking/review/transfer/license, admin account status/password and inbox-read copy changes are covered by the focused unit tests instead.',
            'No notification, reply or outbox row is pre-seeded: every support-reply row this run reads back was produced by the submission and reply routes it calls.',
            'Each run appends two support tickets, two admin replies and two in-app notifications to the persistent local database. They are retained history: nothing this script writes is updated, deleted or reset.',
            'Trace correlation is reported as unavailable when no captured local log line carries the trace id of a failed check.'
        )
    }
}

# ---------------------------------------------------------------------------

if ($f8LibraryOnly) { return }

# ---------------------------------------------------------------------------
# Main flow
# ---------------------------------------------------------------------------

$repoRoot = Get-RepositoryRoot

if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'AGENTS.md') -PathType Leaf)) {
    throw 'Run this script from the Ahlena API repository checkout.'
}

$head = Get-PinnedHead $repoRoot
$uri = Assert-LoopbackBaseUrl $f8BaseUrl
$baseUrl = "$($uri.Scheme)://$($uri.Authority)"

$apiDirectory = Join-Path $repoRoot 'src/API/Ahlena.API'
if ([string]::IsNullOrWhiteSpace($f8ApiAssemblyPath)) {
    $f8ApiAssemblyPath = Join-Path $apiDirectory 'bin/Release/net10.0/Ahlena.API.dll'
}
$assemblyPath = Resolve-FullPath $f8ApiAssemblyPath
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw 'The built API assembly was not found. Build Release first (dotnet build Ahlena.slnx -c Release).'
}

$seedEnvironment = Get-LocalFixtureEnvironment -Password $f8FixturePassword -EnableSeed
$serveEnvironment = Get-LocalFixtureEnvironment -Password $f8FixturePassword

$failureNotes = @()
$checkResults = @()
$tokens = @{}
$fixtureState = $null
$arabicTicketId = ''
$englishTicketId = ''
$apiLogPaths = @()
$apiProcess = $null
$canRunChecks = $false
$client = $null
$apiReused = Test-BaseUrlReachable -Url $baseUrl

# 1. Read-only target verification before any write.
$preflight = Invoke-TargetVerification -RepoRoot $repoRoot -AssemblyPath $assemblyPath -Environment $seedEnvironment -ApiDirectory $apiDirectory

$psqlPath = Get-F8PsqlPath
$connectionSource = Resolve-F8ConnectionStringSource -RepoRoot $repoRoot
$connection = ConvertFrom-F8PostgresConnectionString -ConnectionString $connectionSource.Value
if (@($connection.Problems).Count -gt 0) {
    $failureNotes += ('The local database connection could not be resolved: ' + (@($connection.Problems) -join '; '))
}

$guard = Test-F8FixtureTarget -VerificationPassed $preflight.passed -Report $preflight.report -Connection $connection -PsqlPath $psqlPath

try {
    if (-not $preflight.passed -or -not $guard.Ok) {
        $failureNotes += 'The read-only target verification or the fixture guard failed; no fixture write, no API start and no HTTP check occurred.'
        Write-F8Result -Passed $false -Phase 'target-verification' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    if ($f8SkipFixtureWrite) {
        $reportPath = Join-Path ([System.IO.Path]::GetTempPath()) ('ahlena-f8-fixtures-' + [Guid]::NewGuid().ToString('N') + '.sql')
        [System.IO.File]::WriteAllText($reportPath, (Get-F8FixtureSql), (New-Object System.Text.UTF8Encoding($false)))
        $failureNotes += "SkipFixtureWrite is set: the provisioning SQL was written to $reportPath and nothing else ran."
        Write-F8Result -Passed $false -Phase 'report-only' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    # 2. Provision the fixtures, then read them back.
    $fixtureWrite = Invoke-F8Psql -PsqlPath $psqlPath -Connection $connection -Sql (Get-F8FixtureSql) -Tag 'provision'
    if (-not $fixtureWrite.Ok) {
        $failureNotes += ('The fixture provisioning failed: ' + $fixtureWrite.ErrorSummary)
        Write-F8Result -Passed $false -Phase 'fixture-provisioning' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    $stateRead = Invoke-F8Psql -PsqlPath $psqlPath -Connection $connection -Sql (Get-F8StateQuery) -Tag 'state' -ExpectJson
    if (-not $stateRead.Ok) {
        $failureNotes += ('The fixture readback failed: ' + $stateRead.ErrorSummary)
        Write-F8Result -Passed $false -Phase 'fixture-readback' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    $fixtureState = $stateRead.Value
    $definition = Get-F8FixtureDefinition
    $adminAccountTypes = @(([string]$fixtureState.supportAdminAccountTypes) -split ',')
    $arabicAccountTypes = @(([string]$fixtureState.arabicSubmitterAccountTypes) -split ',')
    $englishAccountTypes = @(([string]$fixtureState.englishSubmitterAccountTypes) -split ',')
    if ([string]$fixtureState.supportAdminUserId -ne $definition.SupportAdmin.UserId -or
        [int]$fixtureState.supportAdminStatus -ne 2 -or
        $adminAccountTypes -notcontains [string]$definition.SupportAdmin.AccountType -or
        [string]$fixtureState.arabicSubmitterUserId -ne $definition.ArabicSubmitter.UserId -or
        [int]$fixtureState.arabicSubmitterStatus -ne 2 -or
        [int]$fixtureState.arabicSubmitterUiLanguage -ne [int]$definition.ArabicSubmitter.UiLanguage -or
        $arabicAccountTypes -notcontains [string]$definition.ArabicSubmitter.AccountType -or
        [string]$fixtureState.englishSubmitterUserId -ne $definition.EnglishSubmitter.UserId -or
        [int]$fixtureState.englishSubmitterStatus -ne 2 -or
        [int]$fixtureState.englishSubmitterUiLanguage -ne [int]$definition.EnglishSubmitter.UiLanguage -or
        $englishAccountTypes -notcontains [string]$definition.EnglishSubmitter.AccountType -or
        [string]::IsNullOrWhiteSpace([string]$fixtureState.passwordSourceUserId)) {
        $failureNotes += 'The fixture readback does not match the provisioned definition.'
        Write-F8Result -Passed $false -Phase 'fixture-readback' -FixtureReport $fixtureState | ConvertTo-Json -Depth 8
        exit 1
    }

    # 3. Start or reuse the API once.
    if (-not $apiReused -and -not $f8SkipApiStart) {
        $logDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('ahlena-f8-verify-' + [Guid]::NewGuid().ToString('N'))
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
            Write-F8Result -Passed $false -Phase 'api-start' -FixtureReport $fixtureState | ConvertTo-Json -Depth 8
            exit 1
        }

        $apiLogPaths = @($outputLog, $errorLog)
    }

    $canRunChecks = $true
    $client = New-AhlenaHttpClient

    # 4. Login preflight for every actor this run needs.
    $sessions = Get-F8SessionIdentityDefinition
    foreach ($actor in $sessions.Keys) {
        $tokens[$actor] = Get-FixtureToken -Client $client -Url $baseUrl `
            -Email $sessions[$actor].Email -Password $f8FixturePassword -DeviceName $sessions[$actor].DeviceName
    }

    # 5. Stage one: the guards and the two submissions.
    $stageOne = Invoke-F8PlannedChecks -Client $client -Url $baseUrl -LogPath $apiLogPaths -Plan @(
        New-F8CheckPlanStageOne `
            -AdminToken $tokens['support-admin'] `
            -ArabicToken $tokens['arabic-submitter'] `
            -EnglishToken $tokens['english-submitter'] `
            -UnknownGuid ([Guid]::NewGuid().ToString())
    )
    $checkResults += $stageOne.Results

    $arabicTicketId = Get-F8CapturedTicketId -Response $stageOne.Responses['write.submit-request.arabic-submitter']
    $englishTicketId = Get-F8CapturedTicketId -Response $stageOne.Responses['write.submit-request.english-submitter']

    # 6. Stage two: one authored reply per ticket, then each recipient's inbox.
    if ([string]::IsNullOrWhiteSpace($arabicTicketId) -or [string]::IsNullOrWhiteSpace($englishTicketId)) {
        $failureNotes += 'A submission returned no ticket id, so the reply and inbox checks were not run.'
    }
    else {
        $plan = @(New-F8CheckPlanStageTwo `
            -AdminToken $tokens['support-admin'] `
            -ArabicToken $tokens['arabic-submitter'] `
            -EnglishToken $tokens['english-submitter'] `
            -ArabicTicketId $arabicTicketId `
            -EnglishTicketId $englishTicketId `
            -AdminUserId ([string]$fixtureState.supportAdminUserId))

        $stageTwo = Invoke-F8PlannedChecks -Client $client -Url $baseUrl -LogPath $apiLogPaths -Plan $plan
        $checkResults += $stageTwo.Results

        # 7. The bilingual cross-check: the two recipients must not have received
        #    the same title, and each must have received its own language.
        $routes = Get-F8Route
        $arabicInbox = $stageTwo.Responses['read.arabic-recipient-inbox']
        $englishInbox = $stageTwo.Responses['read.english-recipient-inbox']
        $arabicTitle = ''
        $englishTitle = ''
        if ($null -ne $arabicInbox) {
            $row = @($arabicInbox.items | Where-Object { [string]$_.destinationEntityId -eq $arabicTicketId })
            if ($row.Count -eq 1) { $arabicTitle = [string]$row[0].title }
        }
        if ($null -ne $englishInbox) {
            $row = @($englishInbox.items | Where-Object { [string]$_.destinationEntityId -eq $englishTicketId })
            if ($row.Count -eq 1) { $englishTitle = [string]$row[0].title }
        }
        $checkResults += New-F8BilingualTitleResult -ArabicTitle $arabicTitle -EnglishTitle $englishTitle `
            -ExpectedTitleAr $routes.SupportReplyTitleAr -ExpectedTitleEn $routes.SupportReplyTitleEn
    }
}
catch {
    $failureNotes += ('The verification could not complete: ' + (Get-TrimmedRedactedLine $_.Exception.Message))
}
finally {
    # 8. Log out every session this run created, then stop any API this script
    #    started so a failed run cannot leave an orphaned listener behind.
    if ($client -and $tokens.Count -gt 0) {
        try {
            $logout = Invoke-F8PlannedChecks -Client $client -Url $baseUrl -LogPath $apiLogPaths -Plan @(New-F8LogoutChecks -Tokens $tokens)
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

Write-F8Result -Passed $overallPassed -Phase 'complete' -FixtureReport $fixtureState | ConvertTo-Json -Depth 8

if (-not $overallPassed) { exit 1 }
