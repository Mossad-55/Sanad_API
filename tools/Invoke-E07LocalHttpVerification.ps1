#requires -Version 5.1
<#
.SYNOPSIS
    E07 append-only caregiver review notes - minimal localhost HTTP verification
    adapter.

.DESCRIPTION
    A bounded adapter, not a second harness. It reuses the L1 library's helpers
    (repository root and pinned head, loopback target assertion, process
    environment overrides, API start/stop, HTTP request, outcome evaluation and
    trace correlation) and adds only what E07 needs:

      1. The L1 read-only preflight report is re-checked fail-closed: all nine
         module contexts must target the loopback PersistentLocal Development
         database with no pending migration, and no live Paymob, SMS, SMTP or
         Push input may be configured. Nothing is written before that gate
         passes.
      2. INSERT-only, guarded SQL through the local psql client adds one
         SupportAdmin actor (the note author), one ContentAdmin actor (the role
         denial check), and two dedicated caregiver rows: the caregiver the notes
         are about and a second caregiver that must stay note-free so the empty
         page is real evidence. Nothing is updated, deleted, reset or bulk
         seeded, every statement is guarded so a rerun inserts nothing, and no
         review note is ever pre-inserted - a seeded note would prove nothing
         about the POST route.
      3. Starts (or reuses) the Development API once against the verified target
         on a free non-default loopback port.
      4. Drives the real routes through HTTP: anonymous and ContentAdmin
         attempts are refused, an unknown caregiver is 404, empty,
         whitespace-only, missing and 2001-character texts are 400 with the
         local business code, four notes are appended (the first exactly 2000
         characters, the fourth carrying a client-supplied author and timestamp
         that must be ignored), then the list route is read with the newest
         page, a second page, a clamped pageSize, a default pageSize and an
         empty page for the note-free caregiver. A final cross-check proves the
         four list reads agree on totalCount, that the four newest rows are
         exactly the four notes this run appended, and that the first note is
         byte-for-byte, author-for-author and instant-for-instant (at the store's
         microsecond resolution - see the cross-check) unchanged after three
         further appends.
      5. Logs out every session this run created.

    The four invalid-text attempts run *before* the appends on purpose: if a
    rejected request had stored a row, that row would sit between this run's
    oldest note and the retained history, so the newest-page/second-page proof
    would no longer cover exactly this run's four notes.

    Safety properties:
      - The base URL must be a loopback HTTP(S) listener; there is no remote
        fallback and no way to pass one.
      - Live SMS/SMTP/Push provider configuration is cleared for every process
        this script starts; migrations stay disabled
        (`Database:ApplyMigrationsOnStartup=false`), so this adapter never
        applies the E07 migration and fails closed while it is pending.
      - Fixture SQL is INSERT-only, guarded, idempotent and ASCII only.
      - The only writes are the review notes this run appends through the public
        POST route; a rejected text stores nothing.
      - Tokens, credentials, connection strings and payloads are never printed;
        a failed check reports only status, safe business code, field names,
        trace id and a redacted local log line. Note text is reported as a
        length and an equality boolean, never echoed.
      - Offline tests dot-source this file with -LibraryOnly and exercise only
        the pure helpers; no API, database or HTTP call occurs there.

.PARAMETER BaseUrl
    Loopback HTTP(S) base URL of the API. Defaults to a free port that does not
    collide with the other local adapters (5235/5241/5243/5245/5247/5249/5251/
    5253). Also used as ASPNETCORE_URLS when the script starts the API.

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
    [string]$BaseUrl = 'http://localhost:5255',

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
$e07LibraryOnly      = [bool]$LibraryOnly
$e07BaseUrl          = $BaseUrl
$e07ApiAssemblyPath  = $ApiAssemblyPath
$e07FixturePassword  = $FixturePassword
$e07SkipApiStart     = [bool]$SkipApiStart
$e07SkipFixtureWrite = [bool]$SkipFixtureWrite

. (Join-Path $PSScriptRoot 'Invoke-LocalFixtureVerification.ps1') -LibraryOnly

# ---------------------------------------------------------------------------
# E07 routes, pinned literals and check names
# ---------------------------------------------------------------------------

function Get-E07Route {
    <#
        The public routes this adapter drives plus the pinned literals the
        assertions compare. The two business codes are what the controller's
        local status mapping publishes; they are held here so the HTTP evidence
        stands on its own.
    #>
    [CmdletBinding()]
    param()

    return [ordered]@{
        Caregivers            = '/api/v1/admin/caregivers'
        AnonymousNoteText     = 'E07 verification anonymous note attempt.'
        InvalidTextCode       = 'Caregivers.ReviewNote.InvalidText'
        CaregiverNotFoundCode = 'Caregivers.ReviewNote.CaregiverNotFound'
        MaxTextLength         = 2000
        DefaultPageSize       = 20
        MaxPageSize           = 100
        TrapAuthorUserId      = '019a2f9c-00ff-7000-8000-0000000000ff'
        TrapCreatedOnUtc      = '2000-01-01T00:00:00Z'
        Checks                = [ordered]@{
            UnauthenticatedList   = 'auth.unauthenticated-list-notes'
            UnauthenticatedAdd    = 'auth.unauthenticated-add-note'
            ContentAdminList      = 'role.content-admin-denied-list-notes'
            ContentAdminAdd       = 'role.content-admin-denied-add-note'
            UnknownAdd            = 'error.unknown-caregiver-add-note'
            UnknownList           = 'error.unknown-caregiver-list-notes'
            EmptyText             = 'error.empty-text-add-note'
            WhitespaceText        = 'error.whitespace-text-add-note'
            MissingText           = 'error.missing-text-add-note'
            OversizeText          = 'error.oversize-text-add-note'
            CreateBoundary        = 'write.add-note.boundary-max-length'
            CreateSecond          = 'write.add-note.second'
            CreateThird           = 'write.add-note.third'
            CreateFourth          = 'write.add-note.fourth-ignores-client-attribution'
            EmptyList             = 'read.empty-list-for-caregiver-without-notes'
            NewestPage            = 'read.newest-page'
            SecondPage            = 'read.second-page'
            ClampedPage           = 'read.page-size-clamped'
            DefaultPageSizeRead   = 'read.page-size-default'
            CrossCheck            = 'cross-check.total-count-and-append-only'
        }
    }
}

function New-E07NoteText {
    <#
        Pure builder for the pinned note bodies. Every literal this adapter
        submits is ASCII, so the fixture SQL and the request payloads carry no
        encoding trap; the boundary case is generated rather than typed so the
        exact length is provable.
    #>
    [CmdletBinding()]
    param(
        [int]$Length = 0,
        [string]$Prefix = 'E07 verification boundary note '
    )

    if ($Length -le 0) { return '' }

    $prefix = [string]$Prefix
    if ($Length -le $prefix.Length) { return $prefix.Substring(0, $Length) }

    return $prefix + ('x' * ($Length - $prefix.Length))
}

function Get-E07NoteDefinition {
    <#
        The four notes this run appends, in creation order. The first is exactly
        the pinned maximum length (so the boundary and the verbatim-storage
        proof coincide), the fourth carries client-supplied author and timestamp
        members that the request record does not declare and that must therefore
        be ignored.
    #>
    [CmdletBinding()]
    param()

    $routes = Get-E07Route
    $checks = $routes.Checks

    return @(
        [ordered]@{ Key = 'note1'; Check = $checks.CreateBoundary; Text = (New-E07NoteText -Length $routes.MaxTextLength); ClientTrap = $false }
        [ordered]@{ Key = 'note2'; Check = $checks.CreateSecond; Text = 'E07 verification review note two.'; ClientTrap = $false }
        [ordered]@{ Key = 'note3'; Check = $checks.CreateThird; Text = 'E07 verification review note three.'; ClientTrap = $false }
        [ordered]@{ Key = 'note4'; Check = $checks.CreateFourth; Text = 'E07 verification review note four.'; ClientTrap = $true }
    )
}

# ---------------------------------------------------------------------------
# E07 fixture definition
# ---------------------------------------------------------------------------

function Get-E07FixtureDefinition {
    <#
        Two dedicated synthetic identities plus the two caregiver rows the
        routes address.

        Account types (Identity): MedicalCaregiver = 2, ContentAdmin = 6,
        SupportAdmin = 7. UserStatus Active = 2. CaregiverStatus Active = 4.
        CaregiverType Medical = 1. CaregiverAvailability Available = 1.

        The user ids, e-mails, phone numbers and account-type ids are pinned so
        a rerun is a no-op, the names stay ASCII, and the password hash is
        copied from the shared L1 Minimal fixture identity, so no credential is
        ever stored in this repository. The guard fails closed when that
        identity (and therefore the L1 Minimal seed) is absent.

        `caregivers.caregivers.user_id` carries no foreign key to identity.users
        (only a unique index), so the two caregiver rows can keep their own
        pinned user ids without inventing identity rows this phase never uses:
        E07 never signs in as a caregiver.
    #>
    [CmdletBinding()]
    param()

    return [ordered]@{
        SupportAdmin          = [ordered]@{
            Email         = 'support.admin.e07@test.sanad.local'
            UserId        = '019a2f9c-0001-7000-8000-000000000001'
            PhoneNumber   = '+201000000099'
            EnglishName   = 'E07 Support Operator'
            ArabicName    = 'E07 Support Operator (arabic name slot)'
            AccountType   = 7
            AccountTypeId = '019a2f9c-0002-7000-8000-000000000001'
        }
        ContentAdmin          = [ordered]@{
            Email         = 'content.admin.e07@test.sanad.local'
            UserId        = '019a2f9c-0001-7000-8000-000000000002'
            PhoneNumber   = '+201000000110'
            EnglishName   = 'E07 Content Operator'
            ArabicName    = 'E07 Content Operator (arabic name slot)'
            AccountType   = 6
            AccountTypeId = '019a2f9c-0002-7000-8000-000000000002'
        }
        CaregiverProfile      = [ordered]@{
            Id           = '019a2f9c-0003-7000-8000-000000000001'
            UserId       = '019a2f9c-0001-7000-8000-000000000003'
            Type         = 1
            Status       = 4
            Availability = 1
        }
        NoteFreeProfile       = [ordered]@{
            Id           = '019a2f9c-0003-7000-8000-000000000002'
            UserId       = '019a2f9c-0001-7000-8000-000000000004'
            Type         = 1
            Status       = 4
            Availability = 1
        }
        PasswordSource        = 'content.admin@test.sanad.local'
    }
}

function Get-E07FixtureSql {
    <#
        Idempotent, INSERT-only fixture provisioning. Every statement is guarded
        so a rerun inserts nothing, and nothing is ever updated, deleted or
        reset. No review-note row is inserted: every note this run reads back
        was produced by the POST route it calls.
    #>
    [CmdletBinding()]
    param()

    $definition = Get-E07FixtureDefinition
    $source = $definition.PasswordSource

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

    $identitySql = ''
    foreach ($actor in @($definition.SupportAdmin, $definition.ContentAdmin)) {
        $identitySql += ($identityInsert -f $actor.UserId, $actor.ArabicName, $actor.EnglishName,
            $actor.PhoneNumber, $actor.Email, $source, $actor.AccountTypeId, $actor.AccountType)
        $identitySql += "`n"
    }

    $profile = $definition.CaregiverProfile
    $noteFree = $definition.NoteFreeProfile

    $caregiverSql = @"
INSERT INTO caregivers.caregivers
    (id, user_id, type, status, availability, average_rating, reviews_count,
     show_profile, show_rating, share_location, show_phone, medical_home_visit_price,
     created_on_utc, updated_on_utc)
SELECT '$($profile.Id)', '$($profile.UserId)', $($profile.Type), $($profile.Status), $($profile.Availability),
       0.00, 0, true, true, false, false, 250.00, now(), now()
WHERE NOT EXISTS (SELECT 1 FROM caregivers.caregivers WHERE id = '$($profile.Id)');

INSERT INTO caregivers.caregivers
    (id, user_id, type, status, availability, average_rating, reviews_count,
     show_profile, show_rating, share_location, show_phone, medical_home_visit_price,
     created_on_utc, updated_on_utc)
SELECT '$($noteFree.Id)', '$($noteFree.UserId)', $($noteFree.Type), $($noteFree.Status), $($noteFree.Availability),
       0.00, 0, true, true, false, false, 250.00, now(), now()
WHERE NOT EXISTS (SELECT 1 FROM caregivers.caregivers WHERE id = '$($noteFree.Id)');
"@

    return @"
\set ON_ERROR_STOP on
BEGIN;

-- Fail closed when the shared L1 fixture password hash is missing: a fixture
-- identity without a usable hash could never authenticate, and every later
-- check would fail for the wrong reason.
DO `$e07guard`$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM identity.users
        WHERE email = '$source' AND password_hash IS NOT NULL)
    THEN
        RAISE EXCEPTION 'E07 fixture guard: the shared fixture password hash is missing';
    END IF;
END
`$e07guard`$;

$identitySql
$caregiverSql
COMMIT;
"@
}

function Get-E07StateQuery {
    <#
        Read-only readback of the fixtures, so the HTTP proof starts from
        verified ids, account types and note counts instead of assumptions. The
        note counts are per fixture caregiver: the note-free caregiver must
        still hold zero notes or the empty-page check would not be evidence.
    #>
    [CmdletBinding()]
    param()

    $definition = Get-E07FixtureDefinition
    $adminEmail = $definition.SupportAdmin.Email
    $contentEmail = $definition.ContentAdmin.Email
    $source = $definition.PasswordSource
    $profileId = $definition.CaregiverProfile.Id
    $noteFreeId = $definition.NoteFreeProfile.Id

    return @"
SELECT json_build_object(
    'supportAdminUserId', (SELECT id::text FROM identity.users WHERE email = '$adminEmail'),
    'supportAdminAccountTypes', (SELECT coalesce(string_agg(account.account_type::text, ','), '') FROM identity.user_accounts AS account JOIN identity.users AS u ON u.id = account.user_id WHERE u.email = '$adminEmail'),
    'supportAdminStatus', (SELECT status FROM identity.users WHERE email = '$adminEmail'),
    'contentAdminUserId', (SELECT id::text FROM identity.users WHERE email = '$contentEmail'),
    'contentAdminAccountTypes', (SELECT coalesce(string_agg(account.account_type::text, ','), '') FROM identity.user_accounts AS account JOIN identity.users AS u ON u.id = account.user_id WHERE u.email = '$contentEmail'),
    'contentAdminStatus', (SELECT status FROM identity.users WHERE email = '$contentEmail'),
    'caregiverId', (SELECT coalesce(id::text, '') FROM caregivers.caregivers WHERE id = '$profileId'),
    'caregiverStatus', (SELECT status FROM caregivers.caregivers WHERE id = '$profileId'),
    'caregiverNoteCount', (SELECT count(*)::int FROM caregivers.caregiver_review_notes WHERE caregiver_id = '$profileId'),
    'noteFreeCaregiverId', (SELECT coalesce(id::text, '') FROM caregivers.caregivers WHERE id = '$noteFreeId'),
    'noteFreeCaregiverStatus', (SELECT status FROM caregivers.caregivers WHERE id = '$noteFreeId'),
    'noteFreeCaregiverNoteCount', (SELECT count(*)::int FROM caregivers.caregiver_review_notes WHERE caregiver_id = '$noteFreeId'),
    'passwordSourceUserId', (SELECT id::text FROM identity.users WHERE email = '$source')
);
"@
}

function Get-E07FootprintQuery {
    <#
        Read-only total count of the E07 feature table, so the fail-closed
        footprint guard can weigh the rows already retained plus this run's
        planned appends against the L1 cap before any fixture write. It selects
        a count only and never writes, deletes or resets a row.
    #>
    [CmdletBinding()]
    param()

    return @"
SELECT json_build_object(
    'reviewNoteRows', (SELECT count(*)::int FROM caregivers.caregiver_review_notes)
);
"@
}

# ---------------------------------------------------------------------------
# Fail-closed guards (pure)
# ---------------------------------------------------------------------------

function ConvertFrom-E07PostgresConnectionString {
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

function Resolve-E07ConnectionStringSource {
    <#
        Resolves the connection string the API itself uses without surfacing it:
        the process environment first, then the API's Development settings file.
    #>
    [CmdletBinding()]
    param([AllowNull()][AllowEmptyString()][string]$RepoRoot)

    foreach ($name in @('ConnectionStrings__CaregiversDatabase', 'ConnectionStrings:CaregiversDatabase')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return @{ Value = $value; Source = "environment:$name" }
        }
    }

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
                foreach ($candidateName in @('CaregiversDatabase', 'IdentityDatabase')) {
                    if ($names -contains $candidateName) {
                        $candidate = [string]$settings.ConnectionStrings.$candidateName
                        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
                            return @{ Value = $candidate; Source = 'appsettings.Development.json' }
                        }
                    }
                }
            }
        }
    }

    return @{ Value = ''; Source = '' }
}

function Test-E07FixtureTarget {
    <#
        Fail-closed guard evaluated before any fixture write: the read-only
        verification must have passed against the persistent local Development
        database for all nine module contexts, no live provider input may be
        configured, and the resolved psql connection must point at the same
        loopback database. Every module must also be fully migrated, so this
        adapter refuses to run while the E07 migration is still pending.
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

function Test-E07ReviewNoteFootprint {
    <#
        Fail-closed feature-table footprint gate (F2), evaluated read-only before
        any fixture write. The L1 contract preserves at most ten synthetic rows
        per feature table, so the TOTAL footprint - the rows the E07 table
        already retains plus the rows this run would append - must fit inside the
        cap. A footprint above the cap denies the run: nothing is written, no
        retained row is deleted and the cap is never relaxed.

        The decision is a pure function of the counts, so the boundary cases can
        be exercised offline with no database, API or file access at all.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int]$ExistingRows,
        [Parameter(Mandatory)][int]$PlannedWrites,
        [int]$Cap = 10
    )

    $total = $ExistingRows + $PlannedWrites
    $valid = $Cap -ge 0 -and $ExistingRows -ge 0 -and $PlannedWrites -ge 0
    $allowed = $valid -and $total -le $Cap

    if (-not $valid) {
        $reason = "the footprint cannot be evaluated (retained $ExistingRows + planned $PlannedWrites against cap $Cap), so the run fails closed"
    }
    elseif (-not $allowed) {
        $reason = "footprint $total exceeds the $Cap-row feature-table cap (retained $ExistingRows + planned $PlannedWrites); no write, no delete and no cap relaxation"
    }
    else {
        $reason = "footprint $total is within the $Cap-row feature-table cap (retained $ExistingRows + planned $PlannedWrites)"
    }

    return [pscustomobject]@{
        Allowed       = $allowed
        ExistingRows  = $ExistingRows
        PlannedWrites = $PlannedWrites
        Total         = $total
        Cap           = $Cap
        Reason        = $reason
    }
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

function New-E07AssertSet {
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

function New-E07ProblemAssert {
    <#
        A rejected request: the response must carry the pinned business code in
        the problem-details extensions and a trace id, so a 400/404 is proven to
        come from the review-note mapping and not from an unrelated failure.
    #>
    [CmdletBinding()]
    param([string]$ExpectedCode)

    return ({
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON problem details'
            return @{ Actual = $actual; Problems = $problems }
        }

        $code = ''
        if (@($body.PSObject.Properties.Name) -contains 'code') { $code = [string]$body.code }
        $traceId = ''
        if (@($body.PSObject.Properties.Name) -contains 'traceId') { $traceId = [string]$body.traceId }
        $codeOk = [string]::Equals($code, $ExpectedCode, [System.StringComparison]::Ordinal)
        $traceOk = -not [string]::IsNullOrWhiteSpace($traceId)
        $actual = "status=$status code=$code traceIdPresent=$traceOk codeMatches=$codeOk"

        if (-not $codeOk) { $problems += "the response code is '$code', expected '$ExpectedCode'" }
        if (-not $traceOk) { $problems += 'the problem details carry no trace id' }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()
}

function New-E07NoteAssert {
    <#
        The created-note response: a uuid note id, the addressed caregiver, the
        submitted text returned byte for byte, the authenticated administrator
        as author and a creation instant. The text is never echoed - a mismatch
        reports lengths only. When the request carried a client-supplied author
        and timestamp, the assert additionally proves that neither was honoured.
    #>
    [CmdletBinding()]
    param(
        [string]$ExpectedText,
        [string]$CaregiverId,
        [string]$AuthorUserId,
        [AllowNull()][string]$TrapAuthorUserId,
        [AllowNull()][string]$TrapCreatedOnUtc
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
        $keysPinned = ($names.Count -eq 5) -and
            ($names -contains 'noteId') -and ($names -contains 'caregiverId') -and
            ($names -contains 'text') -and ($names -contains 'authorUserId') -and
            ($names -contains 'createdOnUtc')

        $parsed = [Guid]::Empty
        $idOk = [Guid]::TryParse([string]$body.noteId, [ref]$parsed)
        $caregiverOk = ([string]$body.caregiverId -eq $CaregiverId)
        $observed = [string]$body.text
        $textOk = [string]::Equals($observed, $ExpectedText, [System.StringComparison]::Ordinal)
        $authorOk = ([string]$body.authorUserId -eq $AuthorUserId)

        # Instant normalization inlined exactly as Get-E07InstantKey does it: a
        # closure cannot call this script's functions, and the JSON reader hands
        # back a DateTime whose default string form drops the sub-second digits.
        $instantKey = $null
        $rawInstant = $body.createdOnUtc
        if ($rawInstant -is [System.DateTimeOffset]) {
            $instantKey = $rawInstant.ToUniversalTime().ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
        }
        elseif ($rawInstant -is [System.DateTime]) {
            $instantKey = ([System.DateTimeOffset]$rawInstant).ToUniversalTime().ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
        }
        elseif (-not [string]::IsNullOrWhiteSpace([string]$rawInstant)) {
            $parsedInstantValue = [System.DateTimeOffset]::MinValue
            if ([System.DateTimeOffset]::TryParse(
                    [string]$rawInstant,
                    [System.Globalization.CultureInfo]::InvariantCulture,
                    [System.Globalization.DateTimeStyles]::RoundtripKind,
                    [ref]$parsedInstantValue)) {
                $instantKey = $parsedInstantValue.ToUniversalTime().ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
            }
        }
        $instantOk = ($null -ne $instantKey)

        $trapAuthorOk = $true
        $trapInstantOk = $true
        if (-not [string]::IsNullOrWhiteSpace($TrapAuthorUserId)) {
            $trapAuthorOk = -not ([string]$body.authorUserId -eq $TrapAuthorUserId)
        }
        if (-not [string]::IsNullOrWhiteSpace($TrapCreatedOnUtc)) {
            $trapInstantValue = [System.DateTimeOffset]::MinValue
            $trapParsed = [System.DateTimeOffset]::TryParse(
                $TrapCreatedOnUtc,
                [System.Globalization.CultureInfo]::InvariantCulture,
                [System.Globalization.DateTimeStyles]::RoundtripKind,
                [ref]$trapInstantValue)
            $trapKey = $null
            if ($trapParsed) {
                $trapKey = $trapInstantValue.ToUniversalTime().ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
            }
            $trapInstantOk = ($null -ne $instantKey) -and ($null -ne $trapKey) -and ($instantKey -ne $trapKey) -and ($instantKey -gt $trapKey)
        }

        $actual = "status=$status noteIdUuid=$idOk caregiverMatches=$caregiverOk textMatchesBytes=$textOk textLength=$($observed.Length) itemKeysPinned=$keysPinned authorIsAuthenticatedAdmin=$authorOk clientAuthorIgnored=$trapAuthorOk createdOnUtcPresent=$instantOk clientInstantIgnored=$trapInstantOk"

        if (-not $keysPinned) { $problems += "the created note body does not carry exactly noteId/caregiverId/text/authorUserId/createdOnUtc (observed $($names.Count) members)" }
        if (-not $idOk) { $problems += 'the note id is not a uuid' }
        if (-not $caregiverOk) { $problems += 'the created note is not attached to the addressed caregiver' }
        if (-not $textOk) { $problems += "the stored text is not the submitted text byte for byte (observed length $($observed.Length), expected length $($ExpectedText.Length))" }
        if (-not $authorOk) { $problems += 'the note author is not the authenticated fixture SupportAdmin' }
        if (-not $instantOk) { $problems += 'the note carries no creation instant' }
        if (-not $trapAuthorOk) { $problems += 'the client-supplied author was honoured instead of the authenticated actor' }
        if (-not $trapInstantOk) { $problems += 'the client-supplied timestamp was honoured instead of the server UTC clock' }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()
}

function New-E07EnvelopeAssert {
    <#
        The list envelope shared by the clamped, default-size and empty-page
        reads: exactly items/page/pageSize/totalCount, the clamped page and
        pageSize the paging convention demands, the expected item count and the
        expected totalCount. Each returned item must also carry exactly the five
        pinned note members, so the projection is proven not to leak caregiver
        identity, contact or medical data.
    #>
    [CmdletBinding()]
    param(
        [int]$ExpectedPage,
        [int]$ExpectedPageSize,
        [int]$ExpectedItemCount = -1,
        [int]$ExpectedTotalCount = -1,
        [int]$MinTotalCount = 0
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
        $envelopeOk = ($names.Count -eq 4) -and
            ($names -contains 'items') -and ($names -contains 'page') -and
            ($names -contains 'pageSize') -and ($names -contains 'totalCount')

        $items = @()
        if ($names -contains 'items' -and $null -ne $body.items) { $items = @($body.items) }
        $page = if ($names -contains 'page') { [int]$body.page } else { -1 }
        $pageSize = if ($names -contains 'pageSize') { [int]$body.pageSize } else { -1 }
        $totalCount = if ($names -contains 'totalCount') { [int]$body.totalCount } else { -1 }

        $pageOk = ($page -eq $ExpectedPage)
        $pageSizeOk = ($pageSize -eq $ExpectedPageSize)
        $countOk = $true
        if ($ExpectedItemCount -ge 0) { $countOk = ($items.Count -eq $ExpectedItemCount) }
        $totalOk = ($totalCount -ge $MinTotalCount)
        if ($ExpectedTotalCount -ge 0) { $totalOk = ($totalCount -eq $ExpectedTotalCount) }

        $fieldsOk = $true
        foreach ($item in $items) {
            $itemNames = @($item.PSObject.Properties.Name)
            if ($itemNames.Count -ne 5 -or
                $itemNames -notcontains 'noteId' -or $itemNames -notcontains 'caregiverId' -or
                $itemNames -notcontains 'text' -or $itemNames -notcontains 'authorUserId' -or
                $itemNames -notcontains 'createdOnUtc') {
                $fieldsOk = $false
                break
            }
        }

        $actual = "status=$status envelopeKeysPinned=$envelopeOk items=$($items.Count) page=$page pageSize=$pageSize totalCount=$totalCount itemKeysPinned=$fieldsOk"

        if (-not $envelopeOk) { $problems += "the list envelope does not carry exactly items/page/pageSize/totalCount (observed $($names.Count) members)" }
        if (-not $pageOk) { $problems += "the list reports page $page, expected $ExpectedPage" }
        if (-not $pageSizeOk) { $problems += "the list reports pageSize $pageSize, expected $ExpectedPageSize" }
        if (-not $countOk) { $problems += "the list returned $($items.Count) items, expected $ExpectedItemCount" }
        if (-not $totalOk) { $problems += "the list reports totalCount $totalCount, expected $(if ($ExpectedTotalCount -ge 0) { $ExpectedTotalCount } else { "at least $MinTotalCount" })" }
        if (-not $fieldsOk) { $problems += 'a list item does not carry exactly noteId/caregiverId/text/authorUserId/createdOnUtc' }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()
}

function Get-E07InstantKey {
    <#
        Canonical fixed-width UTC key for one instant value, so two instants can
        be compared chronologically by ordinal string comparison.

        The API returns ISO-8601 instants, but PowerShell's JSON reader hands
        them back as DateTime objects whose default string form drops the
        sub-second digits and depends on the machine culture - comparing that
        form would rank two instants inside the same second as equal and could
        order a page wrongly. This function normalizes every accepted shape
        (DateTime, DateTimeOffset, ISO-8601 string) to one round-trip UTC key.

        It is a function for the callers that are plain functions; the value
        closures below inline the same normalization because a closure resolves
        script functions from the global scope only and can never call this one.
    #>
    [CmdletBinding()]
    param([AllowNull()][object]$Value)

    if ($null -eq $Value) { return $null }

    if ($Value -is [System.DateTimeOffset]) {
        return $Value.ToUniversalTime().ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
    }
    if ($Value -is [System.DateTime]) {
        return ([System.DateTimeOffset]$Value).ToUniversalTime().ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
    }

    $instant = [System.DateTimeOffset]::MinValue
    $parsed = [System.DateTimeOffset]::TryParse(
        [string]$Value,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::RoundtripKind,
        [ref]$instant)
    if (-not $parsed) { return $null }

    return $instant.ToUniversalTime().ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
}

function Get-E07InstantValue {
    <#
        Canonical UTC instant for one JSON instant value, returned as a
        DateTimeOffset instead of a formatted string so two instants can be
        compared with a tolerance.

        The append-only cross-check needs the value rather than the key: the
        create response echoes the caller's clock straight from the in-memory
        entity, so it carries 100-nanosecond ticks, while every later read
        returns the persisted value and PostgreSQL keeps microseconds only.
        Exact equality would fail on a row nothing touched.

        Accepts the same three shapes as Get-E07InstantKey (DateTimeOffset,
        DateTime, ISO-8601 string) and returns $null when the value is absent or
        unparseable.
    #>
    [CmdletBinding()]
    param([AllowNull()][object]$Value)

    if ($null -eq $Value) { return $null }

    if ($Value -is [System.DateTimeOffset]) {
        return $Value.ToUniversalTime()
    }
    if ($Value -is [System.DateTime]) {
        return ([System.DateTimeOffset]$Value).ToUniversalTime()
    }

    $instant = [System.DateTimeOffset]::MinValue
    $parsed = [System.DateTimeOffset]::TryParse(
        [string]$Value,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::RoundtripKind,
        [ref]$instant)
    if (-not $parsed) { return $null }

    return $instant.ToUniversalTime()
}

function New-E07OrderingProbe {
    <#
        Returns a pure scriptblock that evaluates one returned page's ordering:
        newest first by creation instant, then by note id descending.

        It is a scriptblock rather than a plain function because the value
        assertions below are closures, and a closure resolves *functions* from
        the global scope only - it can see this script's functions never, but it
        does capture variables from the scope that created it. Pure .NET only,
        so the captured scriptblock behaves the same inside and outside a
        closure. The instant normalization inlines Get-E07InstantKey exactly
        (fixed-width round-trip UTC key) for that reason.
    #>
    [CmdletBinding()]
    param()

    return {
        param([object[]]$Items)

        $keys = @()
        foreach ($item in $Items) {
            $value = $item.createdOnUtc
            if ($null -eq $value) { return $false }

            if ($value -is [System.DateTimeOffset]) {
                $keys += $value.ToUniversalTime().ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
            }
            elseif ($value -is [System.DateTime]) {
                $keys += ([System.DateTimeOffset]$value).ToUniversalTime().ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
            }
            else {
                $instant = [System.DateTimeOffset]::MinValue
                $parsed = [System.DateTimeOffset]::TryParse(
                    [string]$value,
                    [System.Globalization.CultureInfo]::InvariantCulture,
                    [System.Globalization.DateTimeStyles]::RoundtripKind,
                    [ref]$instant)
                if (-not $parsed) { return $false }
                $keys += $instant.ToUniversalTime().ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
            }
        }

        for ($index = 1; $index -lt $Items.Count; $index++) {
            if ($keys[$index] -gt $keys[$index - 1]) { return $false }
            if ($keys[$index] -eq $keys[$index - 1]) {
                $previousId = [Guid]$Items[$index - 1].noteId
                $currentId = [Guid]$Items[$index].noteId
                if ($currentId.CompareTo($previousId) -gt 0) { return $false }
            }
        }

        return $true
    }
}

function New-E07NewestPageAssert {
    <#
        The newest page (pageSize 3) of the caregiver's notes. It proves that
        every returned row is one of the notes this run appended, that each row
        carries the addressed caregiver and the authenticated author, and that
        the page really is ordered newest first with the note-id tiebreak.
    #>
    [CmdletBinding()]
    param(
        [string[]]$ExpectedIds,
        [string]$CaregiverId,
        [string]$AuthorUserId
    )

    $ordering = New-E07OrderingProbe

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
        if ($names -contains 'items' -and $null -ne $body.items) { $items = @($body.items) }
        $totalCount = if ($names -contains 'totalCount') { [int]$body.totalCount } else { -1 }

        $expected = @()
        foreach ($id in @($ExpectedIds)) { $expected += ([string]$id).ToLowerInvariant() }

        $membershipOk = ($items.Count -gt 0)
        $caregiverOk = ($items.Count -gt 0)
        $authorOk = ($items.Count -gt 0)
        $instantsOk = ($items.Count -gt 0)
        foreach ($item in $items) {
            if ($expected -notcontains ([string]$item.noteId).ToLowerInvariant()) { $membershipOk = $false }
            if (-not ([string]$item.caregiverId -eq $CaregiverId)) { $caregiverOk = $false }
            if (-not ([string]$item.authorUserId -eq $AuthorUserId)) { $authorOk = $false }
            if ([string]::IsNullOrWhiteSpace([string]$item.createdOnUtc)) { $instantsOk = $false }
        }

        $orderedOk = (& $ordering -Items $items)
        $envelopeOk = ($names.Count -eq 4) -and
            ($names -contains 'items') -and ($names -contains 'page') -and
            ($names -contains 'pageSize') -and ($names -contains 'totalCount')
        $totalOk = ($totalCount -ge $expected.Count)

        $actual = "status=$status envelopeKeysPinned=$envelopeOk items=$($items.Count) allItemsAreThisRun=$membershipOk caregiverMatches=$caregiverOk authorMatches=$authorOk instantsPresent=$instantsOk newestFirstWithIdTiebreak=$orderedOk totalCount=$totalCount"

        if (-not $envelopeOk) { $problems += 'the list envelope does not carry exactly items/page/pageSize/totalCount' }
        if (-not $membershipOk) { $problems += 'the newest page holds a row that is not one of the four notes this run appended' }
        if (-not $caregiverOk) { $problems += 'a newest-page row is not attached to the addressed caregiver' }
        if (-not $authorOk) { $problems += 'a newest-page row was not authored by the authenticated fixture SupportAdmin' }
        if (-not $instantsOk) { $problems += 'a newest-page row carries no creation instant' }
        if (-not $orderedOk) { $problems += 'the newest page is not ordered by creation instant descending with the note-id descending tiebreak' }
        if (-not $totalOk) { $problems += "totalCount is $totalCount, expected at least the $($expected.Count) notes this run appended" }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()
}

function New-E07SecondPageAssert {
    <#
        The second page (page 1 of size 3): no row may repeat the newest page,
        and the page's first row must still be one of this run's four notes.
        That last part is the real proof that the four rejected texts stored
        nothing: a stray row would sort between this run's oldest note and the
        retained history and would push the oldest append out of the top four.
    #>
    [CmdletBinding()]
    param(
        [string[]]$ExpectedIds,
        [string[]]$ExcludedIds,
        [string]$CaregiverId,
        [string]$AuthorUserId
    )

    $ordering = New-E07OrderingProbe

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
        if ($names -contains 'items' -and $null -ne $body.items) { $items = @($body.items) }
        $totalCount = if ($names -contains 'totalCount') { [int]$body.totalCount } else { -1 }

        $expected = @()
        foreach ($id in @($ExpectedIds)) { $expected += ([string]$id).ToLowerInvariant() }
        $excluded = @()
        foreach ($id in @($ExcludedIds)) { $excluded += ([string]$id).ToLowerInvariant() }

        $leaderOk = ($items.Count -gt 0) -and ($expected -contains ([string]$items[0].noteId).ToLowerInvariant())
        $overlapOk = $true
        $caregiverOk = ($items.Count -gt 0)
        $authorOk = ($items.Count -gt 0)
        foreach ($item in $items) {
            $id = ([string]$item.noteId).ToLowerInvariant()
            if ($excluded -contains $id) { $overlapOk = $false }
            if (-not ([string]$item.caregiverId -eq $CaregiverId)) { $caregiverOk = $false }
            if (-not ([string]$item.authorUserId -eq $AuthorUserId)) { $authorOk = $false }
        }

        $orderedOk = (& $ordering -Items $items)
        $envelopeOk = ($names.Count -eq 4) -and
            ($names -contains 'items') -and ($names -contains 'page') -and
            ($names -contains 'pageSize') -and ($names -contains 'totalCount')
        $totalOk = ($totalCount -ge $expected.Count)

        $actual = "status=$status envelopeKeysPinned=$envelopeOk items=$($items.Count) firstItemIsOneOfThisRun=$leaderOk newestPageRowsAbsent=$overlapOk caregiverMatches=$caregiverOk authorMatches=$authorOk newestFirstWithIdTiebreak=$orderedOk totalCount=$totalCount"

        if (-not $envelopeOk) { $problems += 'the list envelope does not carry exactly items/page/pageSize/totalCount' }
        if (-not $leaderOk) { $problems += 'the second page does not continue with one of the four notes this run appended, so a rejected request may have stored a row or the ordering is not newest first' }
        if (-not $overlapOk) { $problems += 'the second page repeats a row from the newest page' }
        if (-not $caregiverOk) { $problems += 'a second-page row is not attached to the addressed caregiver' }
        if (-not $authorOk) { $problems += 'a second-page row was not authored by the authenticated fixture SupportAdmin' }
        if (-not $orderedOk) { $problems += 'the second page is not ordered by creation instant descending with the note-id descending tiebreak' }
        if (-not $totalOk) { $problems += "totalCount is $totalCount, expected at least the $($expected.Count) notes this run appended" }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()
}

function New-E07ConsistencyResult {
    <#
        Cross-check over the four list reads and the first note's two observed
        states. It proves:
          - all four reads agree on the same totalCount and that the count covers
            at least the four notes this run appended;
          - the newest page's three rows plus the second page's first row are
            exactly this run's four notes, so no rejected request stored a row;
          - the first note (the 2000-character boundary note) is unchanged after
            three further appends: same id, same text byte for byte, same author
            and same creation instant (compared at the microsecond resolution
            the store keeps, because the create response echoes the caller's
            tick-precision clock while a later read returns the persisted value),
            which is the append-only guarantee and the verbatim (untruncated,
            unmasked) storage proof in one.

        No note text is ever echoed: problems report booleans, counts and
        lengths.
    #>
    [CmdletBinding()]
    param(
        [object[]]$Totals,
        [object[]]$NewestPageIds,
        [AllowNull()][string]$SecondPageFirstId,
        [string[]]$ExpectedIds,
        [AllowNull()][object]$CreationProbe,
        [AllowNull()][object]$LaterProbe,
        [string]$CaregiverId,
        [string]$AuthorUserId
    )

    $problems = @()

    $totalValues = @()
    foreach ($value in @($Totals)) {
        if ($null -eq $value) { continue }
        $totalValues += [int]$value
    }
    $totalsAgree = ($totalValues.Count -eq 4)
    if ($totalsAgree) {
        foreach ($value in $totalValues) {
            if ($value -ne $totalValues[0]) { $totalsAgree = $false }
        }
    }
    $totalCount = 0
    if ($totalValues.Count -gt 0) { $totalCount = $totalValues[0] }
    $totalCoversAppends = ($totalCount -ge 4)

    $observedSet = @()
    foreach ($id in @($NewestPageIds)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$id)) { $observedSet += ([string]$id).ToLowerInvariant() }
    }
    if (-not [string]::IsNullOrWhiteSpace($SecondPageFirstId)) {
        $observedSet += ([string]$SecondPageFirstId).ToLowerInvariant()
    }
    $expectedSet = @()
    foreach ($id in @($ExpectedIds)) { $expectedSet += ([string]$id).ToLowerInvariant() }

    $coveredCount = 0
    foreach ($id in $expectedSet) {
        if ($observedSet -contains $id) { $coveredCount++ }
    }
    $coversAllAppends = ($coveredCount -eq $expectedSet.Count) -and ($observedSet.Count -eq $expectedSet.Count)

    $idUnchanged = $false
    $textUnchanged = $false
    $authorUnchanged = $false
    $instantUnchanged = $false
    $caregiverUnchanged = $false
    $boundaryLength = 0
    $instantDeltaTicks = $null
    if ($null -ne $CreationProbe -and $null -ne $LaterProbe) {
        $creationText = [string]$CreationProbe.text
        $boundaryLength = $creationText.Length
        $idUnchanged = ([string]$CreationProbe.noteId -eq [string]$LaterProbe.noteId)
        $textUnchanged = [string]::Equals($creationText, [string]$LaterProbe.text, [System.StringComparison]::Ordinal)
        $authorUnchanged = ([string]$CreationProbe.authorUserId -eq $AuthorUserId) -and
            ([string]$LaterProbe.authorUserId -eq $AuthorUserId)
        $creationInstant = Get-E07InstantValue -Value $CreationProbe.createdOnUtc
        $laterInstant = Get-E07InstantValue -Value $LaterProbe.createdOnUtc
        $instantDeltaTicks = $null
        if ($null -ne $creationInstant -and $null -ne $laterInstant) {
            $instantDeltaTicks = [System.Math]::Abs(($creationInstant - $laterInstant).Ticks)
        }
        $instantUnchanged = ($null -ne $instantDeltaTicks) -and ($instantDeltaTicks -lt 10)
        $caregiverUnchanged = ([string]$LaterProbe.caregiverId -eq $CaregiverId)
    }

    if (-not $totalsAgree) {
        $problems += 'the four list reads did not agree on the same totalCount (observed ' + (($totalValues | ForEach-Object { $_.ToString() }) -join ',') + ')'
    }
    if (-not $totalCoversAppends) { $problems += "the caregiver's totalCount is $totalCount, expected at least the four notes this run appended" }
    if (-not $coversAllAppends) { $problems += "the newest page plus the second page's first row cover $coveredCount of this run's four notes (observed $($observedSet.Count) distinct rows), so a rejected request may have stored a row" }
    if ($null -eq $CreationProbe -or $null -eq $LaterProbe) {
        $problems += 'the first note could not be compared across the append and the later read, so the append-only invariant was not verified over HTTP'
    }
    else {
        if (-not $idUnchanged) { $problems += 'the first note changed its id between creation and the later read' }
        if (-not $textUnchanged) { $problems += "the first note text is not the appended text byte for byte after three further appends (observed length $(([string]$LaterProbe.text).Length), expected length $boundaryLength)" }
        if (-not $authorUnchanged) { $problems += 'the first note author is not the authenticated fixture SupportAdmin' }
        if (-not $instantUnchanged) {
            if ($null -eq $instantDeltaTicks) {
                $problems += 'the first note creation instant could not be compared across the append and the later read, so the append-only invariant was not verified over HTTP for that member'
            }
            else {
                $problems += "the first note changed its creation instant after further appends (observed difference $($instantDeltaTicks) ticks, which is beyond the 1-microsecond resolution the store keeps)"
            }
        }
        if (-not $caregiverUnchanged) { $problems += 'the first note is no longer attached to the addressed caregiver' }
    }

    return [ordered]@{
        name             = (Get-E07Route).Checks.CrossCheck
        method           = 'COMPUTED'
        path             = "$((Get-E07Route).Caregivers)/{caregiverId}/notes?page=1&pageSize=3 | ?page=2&pageSize=3 | ?page=0&pageSize=1000 | ?page=0&pageSize=0"
        actor            = 'support-admin'
        expected         = 'all four list reads report the same totalCount covering this run''s four appends; the newest page plus the second page''s first row are exactly those four notes, so no rejected text stored a row; and the 2000-character first note keeps its id, its text byte for byte, its author and its creation instant (within the microsecond the store keeps) after three further appends'
        actual           = "totalCountsAgree=$totalsAgree totalCount=$totalCount newestPageAndSecondPageCoverAllFour=$coversAllAppends distinctRows=$($observedSet.Count) appendOnlyIdUnchanged=$idUnchanged appendOnlyTextUnchanged=$textUnchanged appendOnlyAuthorUnchanged=$authorUnchanged appendOnlyInstantUnchanged=$instantUnchanged appendOnlyInstantDeltaTicks=$(if ($null -eq $instantDeltaTicks) { 'not-compared' } else { $instantDeltaTicks }) boundaryNoteLength=$boundaryLength"
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

function New-E07CheckPlanStageOne {
    <#
        Checks that need no captured runtime value: the authentication and role
        guards, the unknown-caregiver 404 pair, the four rejected texts, the four
        appends and the empty page for the note-free caregiver.
    #>
    [CmdletBinding()]
    param(
        [string]$AdminToken,
        [string]$ContentAdminToken,
        [string]$CaregiverId,
        [string]$NoteFreeCaregiverId,
        [string]$UnknownCaregiverId
    )

    $routes = Get-E07Route
    $checks = $routes.Checks
    $plain = New-E07AssertSet
    $notesPath = "$($routes.Caregivers)/$CaregiverId/notes"
    $notFound = New-E07ProblemAssert -ExpectedCode $routes.CaregiverNotFoundCode
    $invalidText = New-E07ProblemAssert -ExpectedCode $routes.InvalidTextCode
    $definition = Get-E07FixtureDefinition
    $adminUserId = $definition.SupportAdmin.UserId

    $plan = @(
        [ordered]@{
            Name           = $checks.UnauthenticatedList
            Method         = 'GET'
            Path           = $notesPath
            Actor          = 'anonymous'
            Token          = $null
            ExpectedStatus = 401
            Expected       = 'HTTP 401 because the review-note list takes its reader from a Normal JWT only'
            Assert         = $plain.Plain
        }
        [ordered]@{
            Name           = $checks.UnauthenticatedAdd
            Method         = 'POST'
            Path           = $notesPath
            Actor          = 'anonymous'
            Token          = $null
            Body           = (@{ text = $routes.AnonymousNoteText } | ConvertTo-Json -Compress)
            ExpectedStatus = 401
            Expected       = 'HTTP 401 because no bearer token is supplied on the append route, so an anonymous caller cannot author an administrative note'
            Assert         = $plain.Plain
        }
        [ordered]@{
            Name           = $checks.ContentAdminList
            Method         = 'GET'
            Path           = $notesPath
            Actor          = 'content-admin'
            Token          = $ContentAdminToken
            ExpectedStatus = 403
            Expected       = 'HTTP 403 because a ContentAdmin never carries the CaregiverReviewAdmin policy'
            Assert         = $plain.Plain
        }
        [ordered]@{
            Name           = $checks.ContentAdminAdd
            Method         = 'POST'
            Path           = $notesPath
            Actor          = 'content-admin'
            Token          = $ContentAdminToken
            Body           = (@{ text = $routes.AnonymousNoteText } | ConvertTo-Json -Compress)
            ExpectedStatus = 403
            Expected       = 'HTTP 403 because a ContentAdmin cannot append an administrative review note'
            Assert         = $plain.Plain
        }
        [ordered]@{
            Name           = $checks.UnknownAdd
            Method         = 'POST'
            Path           = "$($routes.Caregivers)/$UnknownCaregiverId/notes"
            Actor          = 'support-admin'
            Token          = $AdminToken
            Body           = (@{ text = 'E07 verification note against an unknown caregiver.' } | ConvertTo-Json -Compress)
            ExpectedStatus = 404
            Expected       = "HTTP 404 with code $($routes.CaregiverNotFoundCode) because the caregiver does not exist, and the 404 is reported before the text shape so a bad text cannot mask a missing caregiver"
            Assert         = $notFound
        }
        [ordered]@{
            Name           = $checks.UnknownList
            Method         = 'GET'
            Path           = "$($routes.Caregivers)/$UnknownCaregiverId/notes"
            Actor          = 'support-admin'
            Token          = $AdminToken
            ExpectedStatus = 404
            Expected       = "HTTP 404 with code $($routes.CaregiverNotFoundCode) instead of an empty page, because an unknown caregiver is not a caregiver without notes"
            Assert         = $notFound
        }
        [ordered]@{
            Name           = $checks.EmptyText
            Method         = 'POST'
            Path           = $notesPath
            Actor          = 'support-admin'
            Token          = $AdminToken
            Body           = (@{ text = '' } | ConvertTo-Json -Compress)
            ExpectedStatus = 400
            Expected       = "HTTP 400 with code $($routes.InvalidTextCode) because an empty text is not a note"
            Assert         = $invalidText
        }
        [ordered]@{
            Name           = $checks.WhitespaceText
            Method         = 'POST'
            Path           = $notesPath
            Actor          = 'support-admin'
            Token          = $AdminToken
            Body           = (@{ text = '   ' } | ConvertTo-Json -Compress)
            ExpectedStatus = 400
            Expected       = "HTTP 400 with code $($routes.InvalidTextCode) because a whitespace-only text carries no content"
            Assert         = $invalidText
        }
        [ordered]@{
            Name           = $checks.MissingText
            Method         = 'POST'
            Path           = $notesPath
            Actor          = 'support-admin'
            Token          = $AdminToken
            Body           = '{}'
            ExpectedStatus = 400
            Expected       = "HTTP 400 with code $($routes.InvalidTextCode) because the body carries no text member at all"
            Assert         = $invalidText
        }
        [ordered]@{
            Name           = $checks.OversizeText
            Method         = 'POST'
            Path           = $notesPath
            Actor          = 'support-admin'
            Token          = $AdminToken
            Body           = (@{ text = (New-E07NoteText -Length ($routes.MaxTextLength + 1)) } | ConvertTo-Json -Compress)
            ExpectedStatus = 400
            Expected       = "HTTP 400 with code $($routes.InvalidTextCode) because a 2001-character text is one character past the pinned maximum and must not be truncated silently"
            Assert         = $invalidText
        }
    )

    foreach ($note in @(Get-E07NoteDefinition)) {
        $body = [ordered]@{ text = $note.Text }
        if ($note.ClientTrap) {
            $body['authorUserId'] = $routes.TrapAuthorUserId
            $body['createdOnUtc'] = $routes.TrapCreatedOnUtc
        }

        $plan += [ordered]@{
            Name           = $note.Check
            Method         = 'POST'
            Path           = $notesPath
            Actor          = 'support-admin'
            Token          = $AdminToken
            Body           = ($body | ConvertTo-Json -Compress)
            ExpectedStatus = 201
            Expected       = if ($note.ClientTrap) {
                'HTTP 201 returning exactly the five pinned note members, the submitted text byte for byte, the addressed caregiver, the authenticated SupportAdmin as author and a server-clock instant - the client-supplied author and timestamp in the body are ignored'
            }
            else {
                "HTTP 201 returning exactly the five pinned note members, the submitted text byte for byte ($($note.Text.Length) characters, stored verbatim), the addressed caregiver and the authenticated SupportAdmin as author"
            }
            Assert         = (New-E07NoteAssert -ExpectedText $note.Text -CaregiverId $CaregiverId `
                -AuthorUserId $adminUserId -TrapAuthorUserId $(if ($note.ClientTrap) { $routes.TrapAuthorUserId } else { $null }) `
                -TrapCreatedOnUtc $(if ($note.ClientTrap) { $routes.TrapCreatedOnUtc } else { $null }))
        }
    }

    $plan += [ordered]@{
        Name           = $checks.EmptyList
        Method         = 'GET'
        Path           = "$($routes.Caregivers)/$NoteFreeCaregiverId/notes"
        Actor          = 'support-admin'
        Token          = $AdminToken
        ExpectedStatus = 200
        Expected       = 'HTTP 200 with an empty items array and totalCount 0 for a caregiver that exists but has never been annotated, and with the default page 1 / pageSize 20'
        Assert         = (New-E07EnvelopeAssert -ExpectedPage 1 -ExpectedPageSize $routes.DefaultPageSize `
            -ExpectedItemCount 0 -ExpectedTotalCount 0)
    }

    return $plan
}

function New-E07CheckPlanStageTwo {
    <#
        The newest page of the caregiver's notes. It runs alone because the next
        stage needs the ids this page actually returned: excluding them is what
        proves the second page never repeats a row, and excluding a pinned guess
        instead would turn a timestamp tie into a false failure.
    #>
    [CmdletBinding()]
    param(
        [string]$AdminToken,
        [string]$CaregiverId,
        [hashtable]$NoteIds
    )

    $routes = Get-E07Route
    $checks = $routes.Checks
    $notesPath = "$($routes.Caregivers)/$CaregiverId/notes"
    $definition = Get-E07FixtureDefinition
    $adminUserId = $definition.SupportAdmin.UserId
    $ids = @($NoteIds['note1'], $NoteIds['note2'], $NoteIds['note3'], $NoteIds['note4'])

    return @(
        [ordered]@{
            Name           = $checks.NewestPage
            Method         = 'GET'
            Path           = "${notesPath}?page=1&pageSize=3"
            Actor          = 'support-admin'
            Token          = $AdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 whose three rows are this run''s notes, newest first by creation instant with the note-id tiebreak, each carrying the addressed caregiver and the authenticated SupportAdmin'
            Assert         = (New-E07NewestPageAssert -ExpectedIds $ids -CaregiverId $CaregiverId -AuthorUserId $adminUserId)
        }
    )
}

function New-E07CheckPlanStageThree {
    <#
        Checks that depend on both the four note ids and the ids the newest page
        actually returned: the second page, the clamped pageSize and the default
        pageSize.
    #>
    [CmdletBinding()]
    param(
        [string]$AdminToken,
        [string]$CaregiverId,
        [hashtable]$NoteIds,
        [string[]]$NewestPageIds
    )

    $routes = Get-E07Route
    $checks = $routes.Checks
    $notesPath = "$($routes.Caregivers)/$CaregiverId/notes"
    $definition = Get-E07FixtureDefinition
    $adminUserId = $definition.SupportAdmin.UserId
    $ids = @($NoteIds['note1'], $NoteIds['note2'], $NoteIds['note3'], $NoteIds['note4'])

    return @(
        [ordered]@{
            Name           = $checks.SecondPage
            Method         = 'GET'
            Path           = "${notesPath}?page=2&pageSize=3"
            Actor          = 'support-admin'
            Token          = $AdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 whose first row is still one of this run''s notes and whose rows never repeat the newest page, proving no rejected text stored a row'
            Assert         = (New-E07SecondPageAssert -ExpectedIds $ids -ExcludedIds $NewestPageIds -CaregiverId $CaregiverId -AuthorUserId $adminUserId)
        }
        [ordered]@{
            Name           = $checks.ClampedPage
            Method         = 'GET'
            Path           = "${notesPath}?page=0&pageSize=1000"
            Actor          = 'support-admin'
            Token          = $AdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 with page clamped up to 1 and pageSize capped at 100 instead of rejected or answered with 1000 rows'
            Assert         = (New-E07EnvelopeAssert -ExpectedPage 1 -ExpectedPageSize $routes.MaxPageSize -MinTotalCount 4)
        }
        [ordered]@{
            Name           = $checks.DefaultPageSizeRead
            Method         = 'GET'
            Path           = "${notesPath}?page=0&pageSize=0"
            Actor          = 'support-admin'
            Token          = $AdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 with page 1 and the default pageSize 20 when the caller asks for page 0 and pageSize 0'
            Assert         = (New-E07EnvelopeAssert -ExpectedPage 1 -ExpectedPageSize $routes.DefaultPageSize -MinTotalCount 4)
        }
    )
}

function Get-E07SessionIdentityDefinition {
    <#
        The signed-in actors this run creates. Both are E07's own dedicated
        fixtures, so this gate never depends on how many sessions a shared
        identity happens to be holding.
    #>
    [CmdletBinding()]
    param()

    $definition = Get-E07FixtureDefinition
    return [ordered]@{
        'support-admin' = [ordered]@{
            Email      = $definition.SupportAdmin.Email
            DeviceName = 'e07-caregiver-review-notes-admin'
            NewSession = $true
        }
        'content-admin' = [ordered]@{
            Email      = $definition.ContentAdmin.Email
            DeviceName = 'e07-caregiver-review-notes-content'
            NewSession = $true
        }
    }
}

function New-E07LogoutChecks {
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

function Get-E07PsqlPath {
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

function Get-E07SafeOutputSummary {
    [CmdletBinding()]
    param([string[]]$Lines, [int]$MaxLength = 300)

    $all = @($Lines)
    for ($index = $all.Count - 1; $index -ge 0; $index--) {
        if ([string]::IsNullOrWhiteSpace($all[$index])) { continue }
        return (Get-TrimmedRedactedLine -Text $all[$index] -MaxLength $MaxLength)
    }
    return 'no client output'
}

function Invoke-E07Psql {
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

    $tempFile = Join-Path ([System.IO.Path]::GetTempPath()) ("sanad-e07-" + $Tag + "-" + [Guid]::NewGuid().ToString('N') + ".sql")
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
        return @{ Ok = $false; Value = $null; ErrorSummary = "psql exited with $exitCode`: $(Get-E07SafeOutputSummary -Lines $output)" }
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

function Get-E07CapturedNoteId {
    <#
        Reads the note id out of one create response without printing it.
    #>
    [CmdletBinding()]
    param([AllowNull()][object]$Response)

    if ($null -eq $Response) { return '' }
    if (@($Response.PSObject.Properties.Name) -notcontains 'noteId') { return '' }

    $noteId = $Response.noteId
    if ($null -ne $noteId -and @($noteId.PSObject.Properties.Name) -contains 'value') { return [string]$noteId.value }
    return [string]$noteId
}

function Invoke-E07PlannedChecks {
    <#
        Sends one planned check (GET or POST with an optional JSON body) and
        evaluates it with the L1 outcome evaluator. Parsed responses are
        returned separately so the caller can capture the four note ids and the
        list pages without printing any response body in the report.

        Unlike the L1 evaluator, a check whose assertion throws still reports its
        own name, method, path, expected and actual: the whole point of the run
        is to see every result.
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

function Write-E07Result {
    <#
        One JSON report for the run. It carries the target preflight, the fixture
        guard, the readback and the checks; it never carries tokens, credentials,
        connection strings, response bodies or note text. It also states which
        checks were executed over real HTTP and which are computed from those
        responses.
    #>
    [CmdletBinding()]
    param(
        [bool]$Passed,
        [string]$Phase,
        [AllowNull()][object]$FixtureReport
    )

    # The check results are ordered dictionaries, whose members are reachable
    # through member access but not through -ExpandProperty; project the names
    # explicitly so a report never renders empty.
    $httpExecuted = @($checkResults | Where-Object { $_.method -ne 'COMPUTED' } | ForEach-Object { $_.name })
    $computedOnly = @($checkResults | Where-Object { $_.method -eq 'COMPUTED' } | ForEach-Object { $_.name })

    return [ordered]@{
        workflow           = 'e07-caregiver-review-notes-verification'
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
        verificationScope  = [ordered]@{
            httpExecutedChecks = $httpExecuted
            computedFromHttp   = $computedOnly
        }
        checks             = $checkResults
        results            = [ordered]@{
            total  = $checkResults.Count
            passed = @($checkResults | Where-Object { $_.passed }).Count
            failed = @($checkResults | Where-Object { -not $_.passed }).Count
        }
        notes              = $failureNotes
        limitation         = @(
            'Startup migrations stay disabled for every process this script starts; migration state is only read, so the Caregivers AddCaregiverReviewNotes migration and the E07 source must already be applied/built locally or the fixture guard fails closed before any write.',
            'A local API start also starts the repository''s existing background services. SMS, SMTP, Firebase and Push inputs are cleared so no provider can be contacted, and push runs the Development no-op sender.',
            'HTTP evidence covers the two review-note routes only: create, list, the local 400/404 mapping, both denial paths, paging, the pageSize clamp and the empty page. Everything else in the phase (text bounds at the exact 2000-character boundary inside the domain, the append-only reflection proof that no mutator exists, migration shape) is covered by the focused unit tests instead.',
            'No review-note row is pre-seeded: every note this run reads back was produced by the POST route it calls. Four notes are appended per run and retained; nothing this script writes is updated, deleted or reset.',
            'The append-only cross-check reads the first note back from the pageSize-clamped list (page 1 of at most 100 rows), so it depends on the fixture caregiver holding no more than 100 retained notes; after roughly 25 runs of this adapter the cross-check would need a narrower read.',
            'The append-only cross-check compares the first note''s creation instant at the microsecond resolution PostgreSQL keeps. The create response echoes the caller''s tick-precision clock from the in-memory entity, so the same untouched row is reported with seven fractional digits by the POST route and six by every later read; the check allows a difference under one microsecond (10 ticks) and reports the observed delta as appendOnlyInstantDeltaTicks. This is a house-wide characteristic of returning an in-memory value, not an E07 behaviour, and the note id remains the stable key.',
            'Trace correlation is reported as unavailable when no captured local log line carries the trace id of a failed check.'
        )
    }
}

# ---------------------------------------------------------------------------

if ($e07LibraryOnly) { return }

# ---------------------------------------------------------------------------
# Main flow
# ---------------------------------------------------------------------------

$repoRoot = Get-RepositoryRoot

if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'AGENTS.md') -PathType Leaf)) {
    throw 'Run this script from the Sanad API repository checkout.'
}

$head = Get-PinnedHead $repoRoot
$uri = Assert-LoopbackBaseUrl $e07BaseUrl
$baseUrl = "$($uri.Scheme)://$($uri.Authority)"

$apiDirectory = Join-Path $repoRoot 'src/API/Sanad.API'
if ([string]::IsNullOrWhiteSpace($e07ApiAssemblyPath)) {
    $e07ApiAssemblyPath = Join-Path $apiDirectory 'bin/Release/net10.0/Sanad.API.dll'
}
$assemblyPath = Resolve-FullPath $e07ApiAssemblyPath
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw 'The built API assembly was not found. Build Release first (dotnet build Sanad.slnx -c Release).'
}

$seedEnvironment = Get-LocalFixtureEnvironment -Password $e07FixturePassword -EnableSeed
$serveEnvironment = Get-LocalFixtureEnvironment -Password $e07FixturePassword

$failureNotes = @()
$checkResults = @()
$tokens = @{}
$fixtureState = $null
$createdNoteIds = @{}
$apiLogPaths = @()
$apiProcess = $null
$canRunChecks = $false
$client = $null
$apiReused = Test-BaseUrlReachable -Url $baseUrl

# 1. Read-only target verification before any write.
$preflight = Invoke-TargetVerification -RepoRoot $repoRoot -AssemblyPath $assemblyPath -Environment $seedEnvironment -ApiDirectory $apiDirectory

$psqlPath = Get-E07PsqlPath
$connectionSource = Resolve-E07ConnectionStringSource -RepoRoot $repoRoot
$connection = ConvertFrom-E07PostgresConnectionString -ConnectionString $connectionSource.Value
if (@($connection.Problems).Count -gt 0) {
    $failureNotes += ('The local database connection could not be resolved: ' + (@($connection.Problems) -join '; '))
}

$guard = Test-E07FixtureTarget -VerificationPassed $preflight.passed -Report $preflight.report -Connection $connection -PsqlPath $psqlPath

try {
    if (-not $preflight.passed -or -not $guard.Ok) {
        $failureNotes += 'The read-only target verification or the fixture guard failed; no fixture write, no API start and no HTTP check occurred.'
        Write-E07Result -Passed $false -Phase 'target-verification' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    if ($e07SkipFixtureWrite) {
        $reportPath = Join-Path ([System.IO.Path]::GetTempPath()) ('sanad-e07-fixtures-' + [Guid]::NewGuid().ToString('N') + '.sql')
        [System.IO.File]::WriteAllText($reportPath, (Get-E07FixtureSql), (New-Object System.Text.UTF8Encoding($false)))
        $failureNotes += "SkipFixtureWrite is set: the provisioning SQL was written to $reportPath and nothing else ran."
        Write-E07Result -Passed $false -Phase 'report-only' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    # 1b. Fail-closed feature-table footprint guard (F2). The total E07
    #     feature-table footprint - the rows already retained plus this run's
    #     planned appends - is read read-only and weighed against the L1 cap
    #     before any fixture write. An over-cap table denies the run outright:
    #     no write, no deletion of a retained row and no cap relaxation.
    $footprintRead = Invoke-E07Psql -PsqlPath $psqlPath -Connection $connection -Sql (Get-E07FootprintQuery) -Tag 'footprint' -ExpectJson
    if (-not $footprintRead.Ok) {
        $failureNotes += ('The review-note footprint read failed: ' + $footprintRead.ErrorSummary)
        Write-E07Result -Passed $false -Phase 'footprint-guard' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    $existingNoteRows = 0
    if (-not [int]::TryParse(([string]$footprintRead.Value.reviewNoteRows).Trim(), [ref]$existingNoteRows)) {
        $failureNotes += 'The review-note footprint read returned no row count, so the run fails closed.'
        Write-E07Result -Passed $false -Phase 'footprint-guard' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    $footprint = Test-E07ReviewNoteFootprint -ExistingRows $existingNoteRows -PlannedWrites @(Get-E07NoteDefinition).Count
    if (-not $footprint.Allowed) {
        $failureNotes += ('The review-note footprint guard denied the run: ' + $footprint.Reason)
        Write-E07Result -Passed $false -Phase 'footprint-guard' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    # 2. Provision the fixtures, then read them back.
    $fixtureWrite = Invoke-E07Psql -PsqlPath $psqlPath -Connection $connection -Sql (Get-E07FixtureSql) -Tag 'provision'
    if (-not $fixtureWrite.Ok) {
        $failureNotes += ('The fixture provisioning failed: ' + $fixtureWrite.ErrorSummary)
        Write-E07Result -Passed $false -Phase 'fixture-provisioning' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    $stateRead = Invoke-E07Psql -PsqlPath $psqlPath -Connection $connection -Sql (Get-E07StateQuery) -Tag 'state' -ExpectJson
    if (-not $stateRead.Ok) {
        $failureNotes += ('The fixture readback failed: ' + $stateRead.ErrorSummary)
        Write-E07Result -Passed $false -Phase 'fixture-readback' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    $fixtureState = $stateRead.Value
    $definition = Get-E07FixtureDefinition
    $adminAccountTypes = @(([string]$fixtureState.supportAdminAccountTypes) -split ',')
    $contentAccountTypes = @(([string]$fixtureState.contentAdminAccountTypes) -split ',')
    if ([string]$fixtureState.supportAdminUserId -ne $definition.SupportAdmin.UserId -or
        [int]$fixtureState.supportAdminStatus -ne 2 -or
        $adminAccountTypes -notcontains [string]$definition.SupportAdmin.AccountType -or
        [string]$fixtureState.contentAdminUserId -ne $definition.ContentAdmin.UserId -or
        [int]$fixtureState.contentAdminStatus -ne 2 -or
        $contentAccountTypes -notcontains [string]$definition.ContentAdmin.AccountType -or
        [string]$fixtureState.caregiverId -ne $definition.CaregiverProfile.Id -or
        [int]$fixtureState.caregiverStatus -ne [int]$definition.CaregiverProfile.Status -or
        [string]$fixtureState.noteFreeCaregiverId -ne $definition.NoteFreeProfile.Id -or
        [int]$fixtureState.noteFreeCaregiverStatus -ne [int]$definition.NoteFreeProfile.Status -or
        [string]::IsNullOrWhiteSpace([string]$fixtureState.passwordSourceUserId)) {
        $failureNotes += 'The fixture readback does not match the provisioned definition.'
        Write-E07Result -Passed $false -Phase 'fixture-readback' -FixtureReport $fixtureState | ConvertTo-Json -Depth 8
        exit 1
    }

    # The empty page is only evidence while the note-free caregiver really holds
    # no note; a retained row would make the check pass for the wrong reason.
    if ([int]$fixtureState.noteFreeCaregiverNoteCount -ne 0) {
        $failureNotes += "The note-free fixture caregiver already holds $($fixtureState.noteFreeCaregiverNoteCount) review notes, so the empty-page check would not be evidence."
        Write-E07Result -Passed $false -Phase 'fixture-readback' -FixtureReport $fixtureState | ConvertTo-Json -Depth 8
        exit 1
    }

    # 3. Start or reuse the API once.
    if (-not $apiReused -and -not $e07SkipApiStart) {
        $logDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sanad-e07-verify-' + [Guid]::NewGuid().ToString('N'))
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
            Write-E07Result -Passed $false -Phase 'api-start' -FixtureReport $fixtureState | ConvertTo-Json -Depth 8
            exit 1
        }

        $apiLogPaths = @($outputLog, $errorLog)
    }

    $canRunChecks = $true
    $client = New-SanadHttpClient

    # 4. Login preflight for every actor this run needs.
    $sessions = Get-E07SessionIdentityDefinition
    foreach ($actor in $sessions.Keys) {
        $tokens[$actor] = Get-FixtureToken -Client $client -Url $baseUrl `
            -Email $sessions[$actor].Email -Password $e07FixturePassword -DeviceName $sessions[$actor].DeviceName
    }

    # 5. Stage one: the guards, the four rejected texts, the four appends and the
    #    empty page.
    $stageOne = Invoke-E07PlannedChecks -Client $client -Url $baseUrl -LogPath $apiLogPaths -Plan @(
        New-E07CheckPlanStageOne `
            -AdminToken $tokens['support-admin'] `
            -ContentAdminToken $tokens['content-admin'] `
            -CaregiverId $definition.CaregiverProfile.Id `
            -NoteFreeCaregiverId $definition.NoteFreeProfile.Id `
            -UnknownCaregiverId ([Guid]::NewGuid().ToString())
    )
    $checkResults += $stageOne.Results

    foreach ($note in @(Get-E07NoteDefinition)) {
        $createdNoteIds[$note.Key] = Get-E07CapturedNoteId -Response $stageOne.Responses[$note.Check]
    }

    $missingIds = @($createdNoteIds.Keys | Where-Object { [string]::IsNullOrWhiteSpace([string]$createdNoteIds[$_]) })

    # 6. Stage two: the newest page over the appended notes, then stage three:
    #    the second page, the pageSize clamp and the default pageSize, which need
    #    the ids the newest page actually returned.
    if ($missingIds.Count -gt 0) {
        $failureNotes += ('A create response returned no note id (' + ($missingIds -join ', ') + '), so the list, paging and consistency checks were not run.')
    }
    else {
        $routes = Get-E07Route
        $checks = $routes.Checks
        $caregiverId = $definition.CaregiverProfile.Id

        $stageTwo = Invoke-E07PlannedChecks -Client $client -Url $baseUrl -LogPath $apiLogPaths -Plan @(
            New-E07CheckPlanStageTwo `
                -AdminToken $tokens['support-admin'] `
                -CaregiverId $caregiverId `
                -NoteIds $createdNoteIds
        )
        $checkResults += $stageTwo.Results

        $newestPage = $stageTwo.Responses[$checks.NewestPage]
        $newestIds = @()
        if ($null -ne $newestPage -and @($newestPage.items).Count -gt 0) {
            $newestIds = @($newestPage.items | Select-Object -ExpandProperty noteId)
        }

        if ($newestIds.Count -eq 0) {
            $failureNotes += 'The newest page returned no parseable rows, so the second page, the paging clamp and the consistency cross-check were not run.'
        }
        else {
            $stageThree = Invoke-E07PlannedChecks -Client $client -Url $baseUrl -LogPath $apiLogPaths -Plan @(
                New-E07CheckPlanStageThree `
                    -AdminToken $tokens['support-admin'] `
                    -CaregiverId $caregiverId `
                    -NoteIds $createdNoteIds `
                    -NewestPageIds $newestIds
            )
            $checkResults += $stageThree.Results

            # 7. The cross-check: the four reads agree on totalCount; the newest
            #    page plus the second page's first row are exactly this run's four
            #    notes; and the boundary note is unchanged after three further
            #    appends.
            $secondPage = $stageThree.Responses[$checks.SecondPage]
            $clampedPage = $stageThree.Responses[$checks.ClampedPage]
            $defaultPage = $stageThree.Responses[$checks.DefaultPageSizeRead]

            if ($null -eq $secondPage -or $null -eq $clampedPage -or $null -eq $defaultPage) {
                $failureNotes += 'A list read returned no parseable body, so the total-count and append-only cross-check was not run.'
            }
            else {
                $totals = @(
                    $newestPage.totalCount
                    $secondPage.totalCount
                    $clampedPage.totalCount
                    $defaultPage.totalCount
                )
                $secondPageFirstId = ''
                if (@($secondPage.items).Count -gt 0) { $secondPageFirstId = [string]$secondPage.items[0].noteId }

                $boundaryId = [string]$createdNoteIds['note1']
                $laterProbe = $null
                foreach ($item in @($clampedPage.items)) {
                    if ([string]$item.noteId -eq $boundaryId) { $laterProbe = $item }
                }

                $checkResults += New-E07ConsistencyResult `
                    -Totals $totals `
                    -NewestPageIds $newestIds `
                    -SecondPageFirstId $secondPageFirstId `
                    -ExpectedIds @($createdNoteIds['note1'], $createdNoteIds['note2'], $createdNoteIds['note3'], $createdNoteIds['note4']) `
                    -CreationProbe $stageOne.Responses[$checks.CreateBoundary] `
                    -LaterProbe $laterProbe `
                    -CaregiverId $caregiverId `
                    -AuthorUserId $definition.SupportAdmin.UserId
            }
        }
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
            $logout = Invoke-E07PlannedChecks -Client $client -Url $baseUrl -LogPath $apiLogPaths -Plan @(New-E07LogoutChecks -Tokens $tokens)
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

Write-E07Result -Passed $overallPassed -Phase 'complete' -FixtureReport $fixtureState | ConvertTo-Json -Depth 8

if (-not $overallPassed) { exit 1 }
