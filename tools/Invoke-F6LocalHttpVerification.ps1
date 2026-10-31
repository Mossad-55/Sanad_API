#requires -Version 5.1
<#
.SYNOPSIS
    F6 minimal operational reads (A8/A15) - small local HTTP verification adapter
    over the L1 fixture library.

.DESCRIPTION
    A bounded adapter, not a second harness. It reuses the L1 library's helpers
    (target verification, process environment overrides, API start/stop, HTTP
    request, outcome evaluation and trace correlation) and adds only what F6
    needs:

      1. The L1 read-only preflight report is re-checked fail-closed: all nine
         module contexts must target the loopback PersistentLocal Development
         database with no pending migration, and no live Paymob, SMS, SMTP or
         Push input may be configured. Nothing is written before that gate
         passes.
      2. INSERT-only, guarded, idempotent SQL through the local psql client adds
         two dedicated synthetic identities (a SupportAdmin admin actor and a
         caregiver), one active family with two members, one deleted family, and
         one facility with two visits (an approved visit carrying recorded
         decision facts and a family-cancelled visit carrying recorded
         cancellation facts and a reschedule link). Nothing is updated, deleted,
         reset or bulk seeded, and every statement is guarded so a rerun inserts
         nothing.
      3. Starts (or reuses) the Development API once against the verified target
         on a free non-default loopback port.
      4. Proves the F6 reads: unauthenticated 401; family/caregiver/ContentAdmin
         role isolation on both admin routes; the rejected visit filters (empty
         facility id, inverted window, undefined status); clamped paging; empty
         reads; unknown-id 404 on both details; the family list/detail
         projection; the visit filters and detail facts; the published OpenAPI
         shapes (including the five new BookingDetailResponse members) and that
         both new paths publish GET only.
      5. Logs out every session this run created.

    Safety properties:
      - The base URL must be a loopback HTTP(S) listener; there is no remote
        fallback and no way to pass one.
      - Live SMS/SMTP/Push provider configuration is cleared for every process
        this script starts; migrations stay disabled
        (`Database:ApplyMigrationsOnStartup=false`).
      - Fixture SQL is INSERT-only, guarded and idempotent, and ASCII only.
      - Checks are read-only GETs plus the session logout POST; no F6 write route
        exists to call.
      - Tokens, credentials, connection strings and payloads are never printed;
        a failed check reports only status, safe business code, field names,
        trace id and a redacted local log line.
      - Offline tests dot-source this file with -LibraryOnly and exercise only
        the pure helpers; no API, database or HTTP call occurs there.

.PARAMETER BaseUrl
    Loopback HTTP(S) base URL of the API. Defaults to a free port that does not
    collide with the other local adapters (5235/5237/5241). Also used as
    ASPNETCORE_URLS when the script starts the API.

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
    [string]$BaseUrl = 'http://localhost:5243',

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
$f6LibraryOnly      = [bool]$LibraryOnly
$f6BaseUrl          = $BaseUrl
$f6ApiAssemblyPath  = $ApiAssemblyPath
$f6FixturePassword  = $FixturePassword
$f6SkipApiStart     = [bool]$SkipApiStart
$f6SkipFixtureWrite = [bool]$SkipFixtureWrite

. (Join-Path $PSScriptRoot 'Invoke-LocalFixtureVerification.ps1') -LibraryOnly

# ---------------------------------------------------------------------------
# F6 fixture definition
# ---------------------------------------------------------------------------

function Get-F6FixtureDefinition {
    <#
        Four dedicated synthetic identities plus the fixed references they need.
        Account types: SupportAdmin = 7, MedicalCaregiver = 2 (Identity
        AccountType). UserStatus Active = 2. FamilyRole Owner = 1 / Viewer = 3.
        FamilyRelationshipType Son = 3 / Other = 99. CareHomeStatus Approved =
        5. CareHomeVisitKind Prospective = 1 / Resident = 2.
        CareHomeVisitStatus Approved = 2 / FamilyCancelled = 4.

        The password hash is copied from the shared L1 Minimal fixture identity,
        so no credential is ever stored in this repository; the guard fails
        closed when that identity (and therefore the L1 Minimal seed) is absent.

        The visit window is a fixed pair of UTC days so the `from`/`to` filter
        check is deterministic: the approved visit sits entirely inside
        2027-01-15 and the cancelled visit sits entirely inside 2027-01-16.
    #>
    [CmdletBinding()]
    param()

    return [ordered]@{
        SupportAdmin = [ordered]@{
            Email         = 'support.admin.f6@test.sanad.local'
            UserId        = '019a2f6a-0001-7000-8000-000000000001'
            PhoneNumber   = '+201000000061'
            EnglishName   = 'F6 Support Operator'
            ArabicName    = 'F6 Support Operator (arabic name slot)'
            AccountType   = 7
            AccountTypeId = '019a2f6a-0002-7000-8000-000000000001'
        }
        Caregiver    = [ordered]@{
            Email         = 'caregiver.f6@test.sanad.local'
            UserId        = '019a2f6a-0001-7000-8000-000000000002'
            PhoneNumber   = '+201000000062'
            EnglishName   = 'F6 Caregiver'
            ArabicName    = 'F6 Caregiver (arabic name slot)'
            AccountType   = 2
            AccountTypeId = '019a2f6a-0002-7000-8000-000000000002'
        }
        # The ContentAdmin (6) and app Family (1) identities exist only so the two
        # role-denial checks prove a real 403 from an account of that type. They
        # are dedicated fixtures on purpose: the shared L1 Minimal identities can
        # already sit at the five-session policy cap, which made this gate depend
        # on how many times an earlier verification happened to log in.
        ContentAdmin = [ordered]@{
            Email         = 'content.admin.f6@test.sanad.local'
            UserId        = '019a2f6a-0001-7000-8000-00000000000b'
            PhoneNumber   = '+201000000064'
            EnglishName   = 'F6 Content Operator'
            ArabicName    = 'F6 Content Operator (arabic name slot)'
            AccountType   = 6
            AccountTypeId = '019a2f6a-0002-7000-8000-00000000000b'
        }
        FamilyViewer = [ordered]@{
            Email         = 'family.viewer.f6@test.sanad.local'
            UserId        = '019a2f6a-0001-7000-8000-00000000000c'
            PhoneNumber   = '+201000000065'
            EnglishName   = 'F6 Family Viewer'
            ArabicName    = 'F6 Family Viewer (arabic name slot)'
            AccountType   = 1
            AccountTypeId = '019a2f6a-0002-7000-8000-00000000000c'
        }
        FamilyActive  = [ordered]@{
            Id        = '019a2f6a-0003-7000-8000-000000000003'
            Name      = 'F6 verification active family'
            MemberCount = 2
        }
        FamilyDeleted = [ordered]@{
            Id            = '019a2f6a-0003-7000-8000-000000000004'
            Name          = 'F6 verification deleted family'
            SyntheticOwnerUserId = '019a2f6a-0001-7000-8000-000000000005'
            DeletionReason        = 'F6 local verification deletion reason slot'
            DeletionMessage       = 'F6 local verification deletion message slot'
        }
        Facility      = [ordered]@{
            Id      = '019a2f6a-0004-7000-8000-000000000006'
            OwnerId = '019a2f6a-0001-7000-8000-000000000007'
        }
        VisitApproved = [ordered]@{
            Id            = '019a2f6a-0005-7000-8000-000000000008'
            Kind          = 1
            Status        = 2
            StartsAtUtc   = '2027-01-15T09:00:00Z'
            EndsAtUtc     = '2027-01-15T10:00:00Z'
            DecisionReason = 'F6 verification decision reason slot'
            Day           = '2027-01-15'
        }
        VisitCancelled = [ordered]@{
            Id                 = '019a2f6a-0005-7000-8000-000000000009'
            Kind               = 2
            Status             = 4
            StartsAtUtc        = '2027-01-16T09:00:00Z'
            EndsAtUtc          = '2027-01-16T10:00:00Z'
            ElderlyId          = '019a2f6a-0001-7000-8000-00000000000a'
            CancellationReason = 'F6 verification cancellation reason slot'
            Day                = '2027-01-16'
        }
        PasswordSource = 'content.admin@test.sanad.local'
        MinimalFamilyViewer = 'family.viewer@test.sanad.local'
    }
}

function Get-F6FixtureSql {
    <#
        Idempotent, INSERT-only fixture provisioning. Every statement is
        guarded: a rerun inserts nothing, and nothing is ever updated, deleted
        or reset. The shared fixture password hash is copied, never invented.
    #>
    [CmdletBinding()]
    param()

    $definition = Get-F6FixtureDefinition
    $admin = $definition.SupportAdmin
    $caregiver = $definition.Caregiver
    $contentAdmin = $definition.ContentAdmin
    $familyViewerSession = $definition.FamilyViewer
    $familyActive = $definition.FamilyActive
    $familyDeleted = $definition.FamilyDeleted
    $facility = $definition.Facility
    $approved = $definition.VisitApproved
    $cancelled = $definition.VisitCancelled
    $source = $definition.PasswordSource
    $familyViewer = $definition.MinimalFamilyViewer

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

    $adminSql = $identityInsert -f $admin.UserId, $admin.ArabicName, $admin.EnglishName, $admin.PhoneNumber, $admin.Email, $source, $admin.AccountTypeId, $admin.AccountType
    $caregiverSql = $identityInsert -f $caregiver.UserId, $caregiver.ArabicName, $caregiver.EnglishName, $caregiver.PhoneNumber, $caregiver.Email, $source, $caregiver.AccountTypeId, $caregiver.AccountType
    $contentAdminSql = $identityInsert -f $contentAdmin.UserId, $contentAdmin.ArabicName, $contentAdmin.EnglishName, $contentAdmin.PhoneNumber, $contentAdmin.Email, $source, $contentAdmin.AccountTypeId, $contentAdmin.AccountType
    $familyViewerSessionSql = $identityInsert -f $familyViewerSession.UserId, $familyViewerSession.ArabicName, $familyViewerSession.EnglishName, $familyViewerSession.PhoneNumber, $familyViewerSession.Email, $source, $familyViewerSession.AccountTypeId, $familyViewerSession.AccountType

    $activeOwner = $caregiver.UserId
    $activeSecondMember = $admin.UserId

    return @"
\set ON_ERROR_STOP on
BEGIN;

-- Fail closed when the shared L1 Minimal fixture identities are missing: a
-- fixture identity without a usable password hash could never authenticate, and
-- every role check needs the Minimal family viewer.
DO `$f6guard`$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM identity.users
        WHERE email = '$source' AND password_hash IS NOT NULL)
    THEN
        RAISE EXCEPTION 'F6 fixture guard: the shared fixture password hash is missing';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM identity.users WHERE email = '$familyViewer')
    THEN
        RAISE EXCEPTION 'F6 fixture guard: the L1 Minimal fixture identities are missing';
    END IF;
END
`$f6guard`$;

$adminSql

$caregiverSql

$contentAdminSql

$familyViewerSessionSql

-- One active family owned by the F6 caregiver identity, with the owner member
-- the real Family.Create adds plus one invited viewer member.
INSERT INTO families.families
    (id, owner_user_id, name, created_on_utc, updated_on_utc, deleted_on_utc, deletion_reason, deletion_message)
SELECT '$($familyActive.Id)', '$activeOwner', '$($familyActive.Name)', now(), now(), NULL, NULL, NULL
WHERE NOT EXISTS (SELECT 1 FROM families.families WHERE id = '$($familyActive.Id)');

INSERT INTO families.family_members
    (family_id, user_id, added_by_user_id, relationship_type, role, joined_on_utc)
SELECT '$($familyActive.Id)', '$activeOwner', '$activeOwner', 99, 1, now()
WHERE NOT EXISTS (
    SELECT 1 FROM families.family_members
    WHERE family_id = '$($familyActive.Id)' AND user_id = '$activeOwner');

INSERT INTO families.family_members
    (family_id, user_id, added_by_user_id, relationship_type, role, joined_on_utc)
SELECT '$($familyActive.Id)', '$activeSecondMember', '$activeOwner', 3, 3, now()
WHERE NOT EXISTS (
    SELECT 1 FROM families.family_members
    WHERE family_id = '$($familyActive.Id)' AND user_id = '$activeSecondMember');

-- One deleted family: retained by the read, with a stored deletion reason and
-- message that the projection must never publish.
INSERT INTO families.families
    (id, owner_user_id, name, created_on_utc, updated_on_utc, deleted_on_utc, deletion_reason, deletion_message)
SELECT '$($familyDeleted.Id)', '$($familyDeleted.SyntheticOwnerUserId)', '$($familyDeleted.Name)', now(), now(), now(), '$($familyDeleted.DeletionReason)', '$($familyDeleted.DeletionMessage)'
WHERE NOT EXISTS (SELECT 1 FROM families.families WHERE id = '$($familyDeleted.Id)');

INSERT INTO families.family_members
    (family_id, user_id, added_by_user_id, relationship_type, role, joined_on_utc)
SELECT '$($familyDeleted.Id)', '$($familyDeleted.SyntheticOwnerUserId)', '$($familyDeleted.SyntheticOwnerUserId)', 99, 1, now()
WHERE NOT EXISTS (
    SELECT 1 FROM families.family_members
    WHERE family_id = '$($familyDeleted.Id)' AND user_id = '$($familyDeleted.SyntheticOwnerUserId)');

-- One approved facility with an approved visit (recorded decision facts) and a
-- family-cancelled visit (recorded cancellation facts plus a reschedule link).
INSERT INTO care_homes.facilities
    (id, owner_user_id, status, submitted_revision_id, approved_revision_id, version, created_on_utc, updated_on_utc)
SELECT '$($facility.Id)', '$($facility.OwnerId)', 5, NULL, NULL, 1, now(), now()
WHERE NOT EXISTS (SELECT 1 FROM care_homes.facilities WHERE id = '$($facility.Id)');

INSERT INTO care_homes.visits
    (id, facility_id, family_id, family_user_id, kind, elderly_id, visitor_name, visitor_phone,
     visitor_count, starts_at_utc, ends_at_utc, status, reschedule_of_visit_id, decision_expires_on_utc,
     decided_by_user_id, decided_on_utc, decision_reason, cancelled_by_user_id, cancelled_on_utc,
     cancellation_reason, version, created_on_utc, updated_on_utc)
SELECT '$($approved.Id)', '$($facility.Id)', '$($familyActive.Id)', viewer.id, $($approved.Kind), NULL,
       'F6 verification visitor name slot', '+201000000063', 2,
       TIMESTAMPTZ '$($approved.StartsAtUtc)', TIMESTAMPTZ '$($approved.EndsAtUtc)', $($approved.Status), NULL,
       TIMESTAMPTZ '$($approved.StartsAtUtc)' - interval '2 hours',
       '$($admin.UserId)', TIMESTAMPTZ '$($approved.StartsAtUtc)' - interval '1 hour', '$($approved.DecisionReason)',
       NULL, NULL, NULL, 1, now(), now()
FROM identity.users AS viewer
WHERE viewer.email = '$familyViewer'
  AND NOT EXISTS (SELECT 1 FROM care_homes.visits WHERE id = '$($approved.Id)');

INSERT INTO care_homes.visits
    (id, facility_id, family_id, family_user_id, kind, elderly_id, visitor_name, visitor_phone,
     visitor_count, starts_at_utc, ends_at_utc, status, reschedule_of_visit_id, decision_expires_on_utc,
     decided_by_user_id, decided_on_utc, decision_reason, cancelled_by_user_id, cancelled_on_utc,
     cancellation_reason, version, created_on_utc, updated_on_utc)
SELECT '$($cancelled.Id)', '$($facility.Id)', '$($familyActive.Id)', viewer.id, $($cancelled.Kind), '$($cancelled.ElderlyId)',
       'F6 verification visitor name slot', '+201000000063', 2,
       TIMESTAMPTZ '$($cancelled.StartsAtUtc)', TIMESTAMPTZ '$($cancelled.EndsAtUtc)', $($cancelled.Status), '$($approved.Id)',
       TIMESTAMPTZ '$($cancelled.StartsAtUtc)' - interval '2 hours',
       NULL, NULL, NULL,
       viewer.id, TIMESTAMPTZ '$($cancelled.StartsAtUtc)' - interval '3 hours', '$($cancelled.CancellationReason)',
       1, now(), now()
FROM identity.users AS viewer
WHERE viewer.email = '$familyViewer'
  AND NOT EXISTS (SELECT 1 FROM care_homes.visits WHERE id = '$($cancelled.Id)');

COMMIT;
"@
}

function Get-F6StateQuery {
    <#
        Read-only readback of the fixtures, so the HTTP proof starts from
        verified ids instead of assumptions.
    #>
    [CmdletBinding()]
    param()

    $definition = Get-F6FixtureDefinition
    $adminEmail = $definition.SupportAdmin.Email
    $caregiverEmail = $definition.Caregiver.Email
    $contentAdminEmail = $definition.ContentAdmin.Email
    $familyViewerSessionEmail = $definition.FamilyViewer.Email
    $familyViewer = $definition.MinimalFamilyViewer
    $source = $definition.PasswordSource

    return @"
SELECT json_build_object(
    'supportAdminUserId', (SELECT id::text FROM identity.users WHERE email = '$adminEmail'),
    'supportAdminAccountTypes', (SELECT coalesce(string_agg(account.account_type::text, ','), '') FROM identity.user_accounts AS account JOIN identity.users AS u ON u.id = account.user_id WHERE u.email = '$adminEmail'),
    'supportAdminStatus', (SELECT status FROM identity.users WHERE email = '$adminEmail'),
    'caregiverUserId', (SELECT id::text FROM identity.users WHERE email = '$caregiverEmail'),
    'caregiverAccountTypes', (SELECT coalesce(string_agg(account.account_type::text, ','), '') FROM identity.user_accounts AS account JOIN identity.users AS u ON u.id = account.user_id WHERE u.email = '$caregiverEmail'),
    'caregiverStatus', (SELECT status FROM identity.users WHERE email = '$caregiverEmail'),
    'passwordSourceUserId', (SELECT id::text FROM identity.users WHERE email = '$source'),
    'familyViewerUserId', (SELECT id::text FROM identity.users WHERE email = '$familyViewer'),
    'minimalFixturePresentCount', (SELECT count(*)::int FROM identity.users WHERE email IN ('$source', '$familyViewer')),
    'contentAdminUserId', (SELECT id::text FROM identity.users WHERE email = '$contentAdminEmail'),
    'contentAdminAccountTypes', (SELECT coalesce(string_agg(account.account_type::text, ','), '') FROM identity.user_accounts AS account JOIN identity.users AS u ON u.id = account.user_id WHERE u.email = '$contentAdminEmail'),
    'contentAdminStatus', (SELECT status FROM identity.users WHERE email = '$contentAdminEmail'),
    'familyViewerSessionUserId', (SELECT id::text FROM identity.users WHERE email = '$familyViewerSessionEmail'),
    'familyViewerSessionAccountTypes', (SELECT coalesce(string_agg(account.account_type::text, ','), '') FROM identity.user_accounts AS account JOIN identity.users AS u ON u.id = account.user_id WHERE u.email = '$familyViewerSessionEmail'),
    'familyViewerSessionStatus', (SELECT status FROM identity.users WHERE email = '$familyViewerSessionEmail'),
    'activeFamilyId', (SELECT id::text FROM families.families WHERE id = '$($definition.FamilyActive.Id)'),
    'activeFamilyMemberCount', (SELECT count(*)::int FROM families.family_members WHERE family_id = '$($definition.FamilyActive.Id)'),
    'deletedFamilyId', (SELECT id::text FROM families.families WHERE id = '$($definition.FamilyDeleted.Id)' AND deleted_on_utc IS NOT NULL),
    'facilityId', (SELECT id::text FROM care_homes.facilities WHERE id = '$($definition.Facility.Id)'),
    'visitCount', (SELECT count(*)::int FROM care_homes.visits WHERE facility_id = '$($definition.Facility.Id)'),
    'approvedVisitId', (SELECT id::text FROM care_homes.visits WHERE id = '$($definition.VisitApproved.Id)'),
    'cancelledVisitId', (SELECT id::text FROM care_homes.visits WHERE id = '$($definition.VisitCancelled.Id)')
);
"@
}

# ---------------------------------------------------------------------------
# Fail-closed guards (pure)
# ---------------------------------------------------------------------------

function ConvertFrom-F6PostgresConnectionString {
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

function Resolve-F6ConnectionStringSource {
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

function Test-F6FixtureTarget {
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

function New-F6AssertSet {
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

function New-F6CodeAssert {
    <# Asserts HTTP status plus the published business code of a problem details body. #>
    [CmdletBinding()]
    param([string]$Code)

    return ({
        param($status, $body)
        # No Get-SafeErrorSummary call here on purpose: this assertion is bound
        # with .GetNewClosure() to capture $Code, and a closure resolves only the
        # global scope, so a script-scope library helper would throw. The L1
        # Get-CheckOutcome wrapper still records the safe error summary whenever
        # the HTTP status itself does not match.
        $code = 'none'
        if ($null -ne $body -and $body.PSObject.Properties.Name -contains 'code') { $code = [string]$body.code }
        $actual = "status=$status code=$code"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
        }
        elseif ($body.PSObject.Properties.Name -notcontains 'code') {
            $problems += 'the problem details body carries no business code'
        }
        elseif ([string]$body.code -ne $Code) {
            $problems += "expected code $Code, got '$($body.code)'"
        }
        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()
}

function New-F6PageAssert {
    <#
        Asserts the shared Items/Page/PageSize/TotalCount page shape, the exact
        published item property set, that no item exposes a forbidden field, and
        any per-item check the caller supplies.
    #>
    [CmdletBinding()]
    param(
        [int]$Page = -1,
        [int]$PageSize = -1,
        [int]$TotalCount = -1,
        [string[]]$ItemProperty = @(),
        [switch]$ExactItemProperties,
        [string[]]$ForbiddenToken = @(),
        [scriptblock]$ItemCheck = $null,
        [scriptblock]$PageCheck = $null,
        [int]$MaxItemCount = -1
    )

    return ({
        param($status, $body)
        $actual = "status=$status"
        $problems = @()
        if ($null -eq $body) {
            $problems += 'response body is not valid JSON'
            return @{ Actual = $actual; Problems = $problems }
        }

        $items = @($body.items)
        $actual = "status=$status items=$($items.Count) page=$($body.page) pageSize=$($body.pageSize) totalCount=$($body.totalCount)"
        if ($body.PSObject.Properties.Name -notcontains 'items') { $problems += 'items array is missing' }
        if ($body.PSObject.Properties.Name -notcontains 'totalCount') { $problems += 'totalCount is missing' }
        if ($Page -ge 0 -and [int]$body.page -ne $Page) { $problems += "page is $($body.page), expected $Page" }
        if ($PageSize -ge 0 -and [int]$body.pageSize -ne $PageSize) { $problems += "pageSize is $($body.pageSize), expected $PageSize" }
        if ($TotalCount -ge 0 -and [int]$body.totalCount -ne $TotalCount) { $problems += "totalCount is $($body.totalCount), expected $TotalCount" }
        if ($MaxItemCount -ge 0 -and $items.Count -gt $MaxItemCount) { $problems += "the page returned $($items.Count) items, more than $MaxItemCount" }
        if ($PageCheck) {
            foreach ($problem in @(& $PageCheck $body)) { if ($problem) { $problems += $problem } }
        }

        foreach ($item in $items) {
            $names = @($item.PSObject.Properties.Name)
            if ($ExactItemProperties -and ($names.Count -ne $ItemProperty.Count)) {
                $problems += "an item publishes $($names.Count) properties ($($names -join ','))"
            }
            foreach ($name in $ItemProperty) {
                if ($names -notcontains $name) { $problems += "an item misses the published property $name" }
            }
            foreach ($token in $ForbiddenToken) {
                foreach ($name in $names) {
                    if ($name.ToLowerInvariant().Contains($token)) { $problems += "an item publishes a forbidden field: $name" }
                }
            }
            if ($ItemCheck) {
                foreach ($problem in @(& $ItemCheck $item)) { if ($problem) { $problems += $problem } }
            }
        }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()
}

function New-F6DetailAssert {
    <#
        Asserts a detail shape: required properties present, no property whose
        name contains a forbidden token, expected string values, expected null
        values, and any bespoke check the caller supplies.
    #>
    [CmdletBinding()]
    param(
        [string[]]$RequiredProperty = @(),
        [string[]]$ForbiddenToken = @(),
        [hashtable]$ExpectedValue = @{},
        [string[]]$ExpectedNullProperty = @(),
        [scriptblock]$Check = $null
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
        $actual = "status=$status properties=$($names.Count)"
        foreach ($name in $RequiredProperty) {
            if ($names -notcontains $name) { $problems += "the detail misses the published property $name" }
        }
        foreach ($token in $ForbiddenToken) {
            foreach ($name in $names) {
                if ($name.ToLowerInvariant().Contains($token)) { $problems += "the detail publishes a forbidden field: $name" }
            }
        }
        foreach ($key in @($ExpectedValue.Keys)) {
            if ($names -notcontains $key) { $problems += "$key is missing" }
            elseif ([string]$body.$key -ne [string]$ExpectedValue[$key]) { $problems += "$key is '$($body.$key)', expected '$($ExpectedValue[$key])'" }
        }
        foreach ($key in $ExpectedNullProperty) {
            if ($names -notcontains $key) { $problems += "$key is missing" }
            elseif ($null -ne $body.$key) { $problems += "$key is not null" }
        }
        if ($Check) {
            foreach ($problem in @(& $Check $body)) { if ($problem) { $problems += $problem } }
        }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()
}

# ---------------------------------------------------------------------------
# Check plan
# ---------------------------------------------------------------------------

function Get-F6Route {
    [CmdletBinding()]
    param()
    return @{
        Families = '/api/v1/admin/families'
        Visits   = '/api/v1/admin/care-homes/visits'
    }
}

function New-F6CheckPlan {
    <#
        The whole F6 check set. Every entry is a read-only GET except the final
        session logout POSTs, so one evaluation pass over the plan is enough:
        nothing in the plan depends on a value produced by an earlier check.
    #>
    [CmdletBinding()]
    param(
        [hashtable]$Tokens,
        [string]$UnknownGuid
    )

    $definition = Get-F6FixtureDefinition
    $routes = Get-F6Route
    $helpers = New-F6AssertSet
    $families = $routes.Families
    $visits = $routes.Visits
    $supportAdminToken = $Tokens['support-admin']
    $familyViewerToken = $Tokens['family-viewer']
    $caregiverToken = $Tokens['caregiver']
    $contentAdminToken = $Tokens['content-admin']

    $familyListItemProperties = @('familyId', 'name', 'ownerUserId', 'memberCount', 'createdOnUtc', 'deletedOnUtc')
    $visitListItemProperties = @(
        'visitId', 'facilityId', 'familyId', 'elderlyId', 'kind', 'status', 'startsAtUtc', 'endsAtUtc',
        'rescheduleOfVisitId', 'decidedByUserId', 'decidedOnUtc', 'decisionReason',
        'cancelledByUserId', 'cancelledOnUtc', 'cancellationReason', 'createdOnUtc'
    )

    # no phone, address, medical, health, emergency, notes, avatar, e-mail,
    # display-name, deletion reason/message, visitor identity, pending decision
    # deadline or concurrency token may ever appear in an F6 read.
    $familyForbidden = @('phone', 'address', 'medical', 'health', 'emergency', 'notes', 'avatar', 'email', 'deletionreason', 'deletionmessage', 'arabic', 'english')
    $visitForbidden = @('visitor', 'phone', 'familyuser', 'decisionexpires', 'version')

    $activeFamilyItemCheck = ({
        param($item)
        $problems = @()
        if ([string]$item.familyId -ne $definition.FamilyActive.Id) { $problems += "familyId is '$($item.familyId)', expected the active fixture family" }
        if ([string]$item.name -ne $definition.FamilyActive.Name) { $problems += "name is '$($item.name)', expected the fixture family name" }
        if ([string]$item.ownerUserId -ne $definition.Caregiver.UserId) { $problems += "ownerUserId is '$($item.ownerUserId)', expected the fixture caregiver" }
        if ([int]$item.memberCount -ne $definition.FamilyActive.MemberCount) { $problems += "memberCount is $($item.memberCount), expected $($definition.FamilyActive.MemberCount)" }
        if ($null -ne $item.deletedOnUtc) { $problems += 'an active family reports a deletion instant' }
        return $problems
    }).GetNewClosure()

    $deletedFamilyItemCheck = ({
        param($item)
        $problems = @()
        if ([string]$item.familyId -ne $definition.FamilyDeleted.Id) { $problems += "familyId is '$($item.familyId)', expected the deleted fixture family" }
        if ($null -eq $item.deletedOnUtc) { $problems += 'the retained deleted family reports no deletion instant' }
        if ([int]$item.memberCount -ne 1) { $problems += "memberCount is $($item.memberCount), expected the single owner member" }
        return $problems
    }).GetNewClosure()

    $focusedVisitItemCheck = ({
        param($item)
        $problems = @()
        if ([string]$item.facilityId -ne $definition.Facility.Id) { $problems += "facilityId is '$($item.facilityId)', expected the fixture facility" }
        if ([string]$item.familyId -ne $definition.FamilyActive.Id) { $problems += "familyId is '$($item.familyId)', expected the fixture family" }
        if ([int]$item.kind -ne $definition.VisitApproved.Kind) { $problems += "kind is $($item.kind), expected $($definition.VisitApproved.Kind)" }
        if ([int]$item.status -ne $definition.VisitApproved.Status) { $problems += "status is $($item.status), expected $($definition.VisitApproved.Status)" }
        if ([string]$item.visitId -ne $definition.VisitApproved.Id) { $problems += "visitId is '$($item.visitId)', expected the approved fixture visit" }
        if ($null -eq $item.decidedOnUtc) { $problems += 'the approved visit lost its recorded decision instant' }
        if ([string]$item.decidedByUserId -ne $definition.SupportAdmin.UserId) { $problems += "decidedByUserId is '$($item.decidedByUserId)', expected the fixture operator" }
        if ([string]$item.decisionReason -ne $definition.VisitApproved.DecisionReason) { $problems += 'the approved visit lost its recorded decision reason' }
        return $problems
    }).GetNewClosure()

    $facilityPageItemCheck = ({
        param($item)
        $problems = @()
        $known = @($definition.VisitApproved.Id, $definition.VisitCancelled.Id)
        if ($known -notcontains [string]$item.visitId) { $problems += "the facility filter returned an unexpected visit '$($item.visitId)'" }
        if ([string]$item.facilityId -ne $definition.Facility.Id) { $problems += 'the facility filter returned a visit of another facility' }
        return $problems
    }).GetNewClosure()

    $facilityOrderCheck = ({
        param($body)
        $problems = @()
        $ids = @($body.items | ForEach-Object { [string]$_.visitId })
        $cancelledIndex = [array]::IndexOf($ids, $definition.VisitCancelled.Id)
        $approvedIndex = [array]::IndexOf($ids, $definition.VisitApproved.Id)
        if ($cancelledIndex -lt 0 -or $approvedIndex -lt 0) {
            $problems += 'the facility page does not contain both fixture visits'
        }
        elseif ($cancelledIndex -gt $approvedIndex) {
            $problems += 'the visit page is not newest-first by the scheduled window'
        }
        return $problems
    }).GetNewClosure()

    $familyDetailCheck = ({
        param($body)
        $problems = @()
        $members = @($body.members)
        $dependents = @($body.dependents)
        if ($members.Count -ne $definition.FamilyActive.MemberCount) { $problems += "the detail publishes $($members.Count) members, expected $($definition.FamilyActive.MemberCount)" }
        if ($dependents.Count -ne 0) { $problems += "the fixture family has no dependent, but the detail publishes $($dependents.Count)" }

        $memberProperties = @('userId', 'role', 'relationshipType', 'addedByUserId', 'joinedOnUtc', 'status')
        foreach ($member in $members) {
            $names = @($member.PSObject.Properties.Name)
            if ($names.Count -ne $memberProperties.Count) { $problems += "a member publishes $($names.Count) properties ($($names -join ','))" }
            foreach ($name in $memberProperties) {
                if ($names -notcontains $name) { $problems += "a member misses the published property $name" }
            }
            foreach ($name in $names) {
                if ($name.ToLowerInvariant().Contains('name') -or $name.ToLowerInvariant().Contains('email') -or $name.ToLowerInvariant().Contains('avatar') -or $name.ToLowerInvariant().Contains('phone')) {
                    $problems += "a member publishes a forbidden field: $name"
                }
            }
        }

        $owner = @($members | Where-Object { [string]$_.userId -eq $definition.Caregiver.UserId })
        if ($owner.Count -ne 1) {
            $problems += 'the detail does not publish the fixture owner member exactly once'
        }
        else {
            if ([int]$owner[0].role -ne 1) { $problems += "the owner member role is $($owner[0].role), expected 1 (Owner)" }
            if ([int]$owner[0].status -ne 2) { $problems += "the owner member status is $($owner[0].status), expected the resolved Identity Active status 2" }
        }

        $invited = @($members | Where-Object { [string]$_.userId -eq $definition.SupportAdmin.UserId })
        if ($invited.Count -ne 1) {
            $problems += 'the detail does not publish the invited fixture member exactly once'
        }
        else {
            if ([int]$invited[0].role -ne 3) { $problems += "the invited member role is $($invited[0].role), expected 3 (Viewer)" }
            if ([int]$invited[0].relationshipType -ne 3) { $problems += "the invited member relationshipType is $($invited[0].relationshipType), expected 3 (Son)" }
            if ([string]$invited[0].addedByUserId -ne $definition.Caregiver.UserId) { $problems += "the invited member addedByUserId is '$($invited[0].addedByUserId)', expected the fixture owner" }
            if ([int]$invited[0].status -ne 2) { $problems += "the invited member status is $($invited[0].status), expected the resolved Identity Active status 2" }
        }

        return $problems
    }).GetNewClosure()

    $visitDetailCheck = ({
        param($body)
        $problems = @()
        if ([string]$body.visitId -ne $definition.VisitCancelled.Id) { $problems += "visitId is '$($body.visitId)', expected the cancelled fixture visit" }
        if ([int]$body.status -ne $definition.VisitCancelled.Status) { $problems += "status is $($body.status), expected $($definition.VisitCancelled.Status) (FamilyCancelled)" }
        if ([int]$body.kind -ne $definition.VisitCancelled.Kind) { $problems += "kind is $($body.kind), expected $($definition.VisitCancelled.Kind) (Resident)" }
        if ([string]$body.facilityId -ne $definition.Facility.Id) { $problems += 'the detail lost the fixture facility' }
        if ([string]$body.familyId -ne $definition.FamilyActive.Id) { $problems += 'the detail lost the fixture family' }
        if ([string]$body.elderlyId -ne $definition.VisitCancelled.ElderlyId) { $problems += "elderlyId is '$($body.elderlyId)', expected the fixture dependent link" }
        if ([string]$body.rescheduleOfVisitId -ne $definition.VisitApproved.Id) { $problems += "rescheduleOfVisitId is '$($body.rescheduleOfVisitId)', expected the approved fixture visit" }
        if ($null -ne $body.decidedOnUtc -or $null -ne $body.decidedByUserId) { $problems += 'the cancelled visit reports a decision fact it never recorded' }
        if ($null -eq $body.cancelledOnUtc) { $problems += 'the cancelled visit lost its recorded cancellation instant' }
        if ([string]$body.cancellationReason -ne $definition.VisitCancelled.CancellationReason) { $problems += 'the cancelled visit lost its recorded cancellation reason' }
        if ($null -eq $body.cancelledByUserId) { $problems += 'the cancelled visit lost its recorded cancelling actor' }
        if ($null -eq $body.updatedOnUtc) { $problems += 'the detail lost the persisted last-updated instant' }
        return $problems
    }).GetNewClosure()

    $openApiCheck = ({
        param($body)
        $problems = @()
        if ($null -eq $body) { return @('response body is not valid JSON') }

        $paths = @($body.paths.PSObject.Properties.Name)
        $schemas = @($body.components.schemas.PSObject.Properties.Name)

        foreach ($published in @("$families", "$families/{familyId}", "$visits", "$visits/{visitId}")) {
            if ($paths -notcontains $published) {
                $problems += "the published OpenAPI document has no path $published"
            }
        }

        foreach ($path in @("$families", "$families/{familyId}", "$visits", "$visits/{visitId}")) {
            if ($paths -notcontains $path) { continue }
            $methods = @($body.paths.$path.PSObject.Properties.Name | ForEach-Object { $_.ToLowerInvariant() })
            if ($methods -notcontains 'get') { $problems += "$path publishes no GET operation" }
            foreach ($method in $methods) {
                if ($method -ne 'get' -and $method -ne 'parameters') { $problems += "$path publishes a non-read operation: $method" }
            }
        }

        foreach ($schema in @('AdminFamilyListItem', 'AdminFamilyMemberItem', 'AdminFamilyDependentItem', 'AdminFamilyPage', 'AdminFamilyDetail',
                              'AdminCareHomeVisitListItem', 'AdminCareHomeVisitDetail', 'AdminCareHomeVisitPage', 'BookingDetailResponse')) {
            if ($schemas -notcontains $schema) { $problems += "$schema is missing from the published schemas" }
        }

        if ($schemas -contains 'AdminFamilyListItem') {
            $properties = @($body.components.schemas.AdminFamilyListItem.properties.PSObject.Properties.Name)
            if ($properties.Count -ne $familyListItemProperties.Count) { $problems += "AdminFamilyListItem publishes $($properties.Count) properties, expected $($familyListItemProperties.Count)" }
            foreach ($name in $familyListItemProperties) {
                if ($properties -notcontains $name) { $problems += "AdminFamilyListItem.$name is not published" }
            }
            if ($properties -contains 'deletionReason' -or $properties -contains 'deletionMessage') { $problems += 'AdminFamilyListItem publishes the family deletion reason or message' }
        }

        if ($schemas -contains 'AdminCareHomeVisitListItem') {
            $properties = @($body.components.schemas.AdminCareHomeVisitListItem.properties.PSObject.Properties.Name)
            if ($properties.Count -ne $visitListItemProperties.Count) { $problems += "AdminCareHomeVisitListItem publishes $($properties.Count) properties, expected $($visitListItemProperties.Count)" }
            foreach ($name in $visitListItemProperties) {
                if ($properties -notcontains $name) { $problems += "AdminCareHomeVisitListItem.$name is not published" }
            }
            foreach ($name in @('visitorName', 'visitorPhone', 'visitorCount', 'familyUserId', 'decisionExpiresOnUtc', 'version')) {
                if ($properties -contains $name) { $problems += "AdminCareHomeVisitListItem publishes the withheld field $name" }
            }
        }

        if ($schemas -contains 'BookingDetailResponse') {
            $properties = @($body.components.schemas.BookingDetailResponse.properties.PSObject.Properties.Name)
            foreach ($name in @('cancellationActorSide', 'cancellationAction', 'cancellationReasonCategory', 'refundEntitlement', 'refundDecisionReason')) {
                if ($properties -notcontains $name) { $problems += "BookingDetailResponse.$name is not published" }
            }
        }

        return $problems
    }).GetNewClosure()

    $plan = @(
        [ordered]@{
            Name           = 'auth.unauthenticated-families-list'
            Method         = 'GET'
            Path           = $families
            Actor          = 'anonymous'
            Token          = $null
            ExpectedStatus = 401
            Expected       = 'HTTP 401 because no bearer token is supplied on the admin family read'
            Assert         = $helpers.Plain
        }
        [ordered]@{
            Name           = 'auth.unauthenticated-visits-list'
            Method         = 'GET'
            Path           = $visits
            Actor          = 'anonymous'
            Token          = $null
            ExpectedStatus = 401
            Expected       = 'HTTP 401 because no bearer token is supplied on the admin visit read'
            Assert         = $helpers.Plain
        }
        [ordered]@{
            Name           = 'role.family-denied-families-list'
            Method         = 'GET'
            Path           = $families
            Actor          = 'family-viewer'
            Token          = $familyViewerToken
            ExpectedStatus = 403
            Expected       = 'HTTP 403 because a Family account lacks the operational-read admin policy'
            Assert         = $helpers.Plain
        }
        [ordered]@{
            Name           = 'role.family-denied-visits-list'
            Method         = 'GET'
            Path           = $visits
            Actor          = 'family-viewer'
            Token          = $familyViewerToken
            ExpectedStatus = 403
            Expected       = 'HTTP 403 because a Family account never reaches the admin visit register'
            Assert         = $helpers.Plain
        }
        [ordered]@{
            Name           = 'role.caregiver-denied-family-detail'
            Method         = 'GET'
            Path           = "$families/$($definition.FamilyActive.Id)"
            Actor          = 'caregiver'
            Token          = $caregiverToken
            ExpectedStatus = 403
            Expected       = 'HTTP 403 because a caregiver token must never read family data'
            Assert         = $helpers.Plain
        }
        [ordered]@{
            Name           = 'role.content-admin-denied-visits-list'
            Method         = 'GET'
            Path           = $visits
            Actor          = 'content-admin'
            Token          = $contentAdminToken
            ExpectedStatus = 403
            Expected       = 'HTTP 403 because ContentAdmin is not an operational-read admin account type'
            Assert         = $helpers.Plain
        }
        [ordered]@{
            Name           = 'invalid.visits-empty-facility-id'
            Method         = 'GET'
            Path           = "${visits}?facilityId=00000000-0000-0000-0000-000000000000"
            Actor          = 'support-admin'
            Token          = $supportAdminToken
            ExpectedStatus = 400
            Expected       = 'HTTP 400 with the reused admin-read invalid-query code for an empty facility id'
            Assert         = (New-F6CodeAssert -Code 'CareHomes.Admin.InvalidQuery')
        }
        [ordered]@{
            Name           = 'invalid.visits-inverted-window'
            Method         = 'GET'
            Path           = "${visits}?from=2027-01-16&to=2027-01-15"
            Actor          = 'support-admin'
            Token          = $supportAdminToken
            ExpectedStatus = 400
            Expected       = 'HTTP 400 with the reused admin-read invalid-query code for an inverted scheduled window'
            Assert         = (New-F6CodeAssert -Code 'CareHomes.Admin.InvalidQuery')
        }
        [ordered]@{
            Name           = 'invalid.visits-undefined-status'
            Method         = 'GET'
            Path           = "${visits}?status=99"
            Actor          = 'support-admin'
            Token          = $supportAdminToken
            ExpectedStatus = 400
            Expected       = 'HTTP 400 for an undefined visit status (binding or the reused invalid-query code)'
            Assert         = $helpers.Problem
        }
        [ordered]@{
            Name           = 'paging.families-clamp'
            Method         = 'GET'
            Path           = "${families}?page=0&pageSize=500"
            Actor          = 'support-admin'
            Token          = $supportAdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 with page clamped to 1 and pageSize clamped to 100 (never a 400)'
            Assert         = (New-F6PageAssert -Page 1 -PageSize 100 -MaxItemCount 100)
        }
        [ordered]@{
            Name           = 'paging.visits-clamp'
            Method         = 'GET'
            Path           = "${visits}?page=0&pageSize=500"
            Actor          = 'support-admin'
            Token          = $supportAdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 with page clamped to 1 and pageSize clamped to 100 (never a 400)'
            Assert         = (New-F6PageAssert -Page 1 -PageSize 100 -MaxItemCount 100)
        }
        [ordered]@{
            Name           = 'read.families-search-no-match'
            Method         = 'GET'
            Path           = "${families}?search=$UnknownGuid"
            Actor          = 'support-admin'
            Token          = $supportAdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 empty page with totalCount 0 for a search that matches nothing (never a 404)'
            Assert         = (New-F6PageAssert -Page 1 -PageSize 25 -TotalCount 0)
        }
        [ordered]@{
            Name           = 'read.visits-unknown-facility'
            Method         = 'GET'
            Path           = "${visits}?facilityId=$UnknownGuid"
            Actor          = 'support-admin'
            Token          = $supportAdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 empty page with totalCount 0 for a facility with no visit'
            Assert         = (New-F6PageAssert -Page 1 -PageSize 25 -TotalCount 0)
        }
        [ordered]@{
            Name           = 'unknown.family-detail'
            Method         = 'GET'
            Path           = "$families/$UnknownGuid"
            Actor          = 'support-admin'
            Token          = $supportAdminToken
            ExpectedStatus = 404
            Expected       = 'HTTP 404 with the existing Families.Family.NotFound business code'
            Assert         = (New-F6CodeAssert -Code 'Families.Family.NotFound')
        }
        [ordered]@{
            Name           = 'unknown.visit-detail'
            Method         = 'GET'
            Path           = "$visits/$UnknownGuid"
            Actor          = 'support-admin'
            Token          = $supportAdminToken
            ExpectedStatus = 404
            Expected       = 'HTTP 404 with the existing CareHomes.Visits.NotFound business code'
            Assert         = (New-F6CodeAssert -Code 'CareHomes.Visits.NotFound')
        }
        [ordered]@{
            Name           = 'read.families-by-exact-id'
            Method         = 'GET'
            Path           = "${families}?search=$($definition.FamilyActive.Id)"
            Actor          = 'support-admin'
            Token          = $supportAdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 exact-id search returning the fixture family with its member count and no forbidden field'
            Assert         = (New-F6PageAssert -Page 1 -PageSize 25 -TotalCount 1 -ItemProperty $familyListItemProperties -ExactItemProperties -ForbiddenToken $familyForbidden -ItemCheck $activeFamilyItemCheck)
        }
        [ordered]@{
            Name           = 'read.families-deleted-retained'
            Method         = 'GET'
            Path           = "${families}?search=$($definition.FamilyDeleted.Id)"
            Actor          = 'support-admin'
            Token          = $supportAdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200: the deleted family is retained with its deletion instant and neither the stored deletion reason nor message'
            Assert         = (New-F6PageAssert -Page 1 -PageSize 25 -TotalCount 1 -ItemProperty $familyListItemProperties -ExactItemProperties -ForbiddenToken $familyForbidden -ItemCheck $deletedFamilyItemCheck)
        }
        [ordered]@{
            Name           = 'read.family-detail-members'
            Method         = 'GET'
            Path           = "$families/$($definition.FamilyActive.Id)"
            Actor          = 'support-admin'
            Token          = $supportAdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 detail with the exact list fields plus members (role, relationship, inviter, join instant, resolved Identity status) and the empty dependents array'
            Assert         = (New-F6DetailAssert `
                -RequiredProperty @('familyId', 'name', 'ownerUserId', 'memberCount', 'createdOnUtc', 'deletedOnUtc', 'members', 'dependents') `
                -ForbiddenToken $familyForbidden `
                -ExpectedValue @{ familyId = $definition.FamilyActive.Id; name = $definition.FamilyActive.Name; ownerUserId = $definition.Caregiver.UserId; memberCount = '2' } `
                -ExpectedNullProperty @('deletedOnUtc') `
                -Check $familyDetailCheck)
        }
        [ordered]@{
            Name           = 'read.visits-facility-filter'
            Method         = 'GET'
            Path           = "${visits}?facilityId=$($definition.Facility.Id)&page=1&pageSize=5"
            Actor          = 'support-admin'
            Token          = $supportAdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 with exactly the two fixture visits of the facility, newest-first, and no visitor identity field'
            Assert         = (New-F6PageAssert -Page 1 -PageSize 5 -TotalCount 2 -ItemProperty $visitListItemProperties -ExactItemProperties -ForbiddenToken $visitForbidden -ItemCheck $facilityPageItemCheck -PageCheck $facilityOrderCheck)
        }
        [ordered]@{
            Name           = 'read.visits-status-filter'
            Method         = 'GET'
            Path           = "${visits}?facilityId=$($definition.Facility.Id)&status=2&page=1&pageSize=5"
            Actor          = 'support-admin'
            Token          = $supportAdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 with only the approved fixture visit and its recorded decision facts'
            Assert         = (New-F6PageAssert -Page 1 -PageSize 5 -TotalCount 1 -ItemProperty $visitListItemProperties -ExactItemProperties -ForbiddenToken $visitForbidden -ItemCheck $focusedVisitItemCheck)
        }
        [ordered]@{
            Name           = 'read.visits-window-filter'
            Method         = 'GET'
            Path           = "${visits}?facilityId=$($definition.Facility.Id)&from=$($definition.VisitApproved.Day)&to=$($definition.VisitApproved.Day)&page=1&pageSize=5"
            Actor          = 'support-admin'
            Token          = $supportAdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 where the inclusive UTC day window returns only the visit scheduled inside that day'
            Assert         = (New-F6PageAssert -Page 1 -PageSize 5 -TotalCount 1 -ItemProperty $visitListItemProperties -ExactItemProperties -ForbiddenToken $visitForbidden -ItemCheck $focusedVisitItemCheck)
        }
        [ordered]@{
            Name           = 'read.visit-detail-facts'
            Method         = 'GET'
            Path           = "$visits/$($definition.VisitCancelled.Id)"
            Actor          = 'support-admin'
            Token          = $supportAdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 detail with the recorded cancellation actor, instant and reason, the reschedule link and the persisted updated instant, and no visitor identity field'
            Assert         = (New-F6DetailAssert `
                -RequiredProperty @('visitId', 'facilityId', 'familyId', 'elderlyId', 'kind', 'status', 'startsAtUtc', 'endsAtUtc', 'rescheduleOfVisitId', 'decidedByUserId', 'decidedOnUtc', 'decisionReason', 'cancelledByUserId', 'cancelledOnUtc', 'cancellationReason', 'createdOnUtc', 'updatedOnUtc') `
                -ForbiddenToken $visitForbidden `
                -ExpectedValue @{ visitId = $definition.VisitCancelled.Id; status = '4'; kind = '2' } `
                -ExpectedNullProperty @('decidedByUserId', 'decidedOnUtc', 'decisionReason') `
                -Check $visitDetailCheck)
        }
        [ordered]@{
            Name           = 'openapi.f6-reads-published'
            Method         = 'GET'
            Path           = '/openapi/v1.json'
            Actor          = 'support-admin'
            Token          = $supportAdminToken
            ExpectedStatus = 200
            Expected       = 'HTTP 200 whose document publishes both F6 read paths as GET-only, the new schemas with exactly the pinned properties, and the five new BookingDetailResponse members'
            Assert         = (New-F6DetailAssert -RequiredProperty @('paths', 'components') -Check $openApiCheck)
        }
    )

    foreach ($actor in $Tokens.Keys) {
        $plan += [ordered]@{
            Name           = "session.logout.$actor"
            Method         = 'POST'
            Path           = '/api/v1/auth/sessions/logout'
            Actor          = $actor
            Token          = $Tokens[$actor]
            ExpectedStatus = 204
            Expected       = 'HTTP 204, revoking the session this run created'
            Assert         = $helpers.Plain
        }
    }

    return $plan
}

function Get-F6SessionIdentityDefinition {
    <#
        The signed-in actors this run creates. Every one is logged out again.
        All four are F6's own dedicated fixtures (the L1 Minimal identities stay
        read-only data references), so this gate never depends on how many
        sessions a shared identity happens to be holding.
    #>
    [CmdletBinding()]
    param()

    $definition = Get-F6FixtureDefinition
    return [ordered]@{
        'support-admin' = [ordered]@{
            Email      = $definition.SupportAdmin.Email
            DeviceName = 'f6-operational-reads-admin'
            NewSession = $true
        }
        'caregiver'     = [ordered]@{
            Email      = $definition.Caregiver.Email
            DeviceName = 'f6-operational-reads-caregiver'
            NewSession = $true
        }
        'family-viewer' = [ordered]@{
            Email      = $definition.FamilyViewer.Email
            DeviceName = 'f6-operational-reads-viewer'
            NewSession = $true
        }
        'content-admin' = [ordered]@{
            Email      = $definition.ContentAdmin.Email
            DeviceName = 'f6-operational-reads-content-admin'
            NewSession = $true
        }
    }
}

# ---------------------------------------------------------------------------
# psql helpers (runtime)
# ---------------------------------------------------------------------------

function Get-F6PsqlPath {
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

function Get-F6SafeOutputSummary {
    [CmdletBinding()]
    param([string[]]$Lines, [int]$MaxLength = 300)

    $all = @($Lines)
    for ($index = $all.Count - 1; $index -ge 0; $index--) {
        if ([string]::IsNullOrWhiteSpace($all[$index])) { continue }
        return (Get-TrimmedRedactedLine -Text $all[$index] -MaxLength $MaxLength)
    }
    return 'no client output'
}

function Invoke-F6Psql {
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

    $tempFile = Join-Path ([System.IO.Path]::GetTempPath()) ("ahlena-f6-" + $Tag + "-" + [Guid]::NewGuid().ToString('N') + ".sql")
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
        return @{ Ok = $false; Value = $null; ErrorSummary = "psql exited with $exitCode`: $(Get-F6SafeOutputSummary -Lines $output)" }
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

# ---------------------------------------------------------------------------
# Result writer
# ---------------------------------------------------------------------------

function Write-F6Result {
    <#
        One JSON report for the run. It carries the target preflight, the
        fixture guard, the readback and the checks; it never carries tokens,
        credentials, connection strings or response bodies.
    #>
    [CmdletBinding()]
    param(
        [bool]$Passed,
        [string]$Phase,
        [AllowNull()][object]$FixtureReport
    )

    return [ordered]@{
        workflow           = 'f6-operational-reads-verification'
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
            'Startup migrations stay disabled for every process this script starts; migration state is only read.',
            'A local API start also starts the repository''s existing outbox/notification background services. SMS, SMTP, Firebase and Push inputs are cleared so no provider can be contacted, and push runs the Development no-op sender.',
            'The fixture rows are retained verification data: nothing this script writes is updated, deleted or reset.',
            'Every check is read-only plus this run''s own session logout; the F6 phase publishes no write route to exercise.',
            'The caregiver booking detail delta (the five additive BookingDetailResponse members) is proven by the focused unit tests and by the published OpenAPI schema; a caregiver-owned booking portfolio is outside this minimal fixture set, so no caregiver booking read is sent over HTTP.',
            'Trace correlation is reported as unavailable when no captured local log line carries the trace id of a failed check.'
        )
    }
}

# ---------------------------------------------------------------------------

if ($f6LibraryOnly) { return }

# ---------------------------------------------------------------------------
# Main flow
# ---------------------------------------------------------------------------

$repoRoot = Get-RepositoryRoot

if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'AGENTS.md') -PathType Leaf)) {
    throw 'Run this script from the Ahlena API repository checkout.'
}

$head = Get-PinnedHead $repoRoot
$uri = Assert-LoopbackBaseUrl $f6BaseUrl
$baseUrl = "$($uri.Scheme)://$($uri.Authority)"

$apiDirectory = Join-Path $repoRoot 'src/API/Ahlena.API'
if ([string]::IsNullOrWhiteSpace($f6ApiAssemblyPath)) {
    $f6ApiAssemblyPath = Join-Path $apiDirectory 'bin/Release/net10.0/Ahlena.API.dll'
}
$assemblyPath = Resolve-FullPath $f6ApiAssemblyPath
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw 'The built API assembly was not found. Build Release first (dotnet build Ahlena.slnx -c Release).'
}

$seedEnvironment = Get-LocalFixtureEnvironment -Password $f6FixturePassword -EnableSeed
$serveEnvironment = Get-LocalFixtureEnvironment -Password $f6FixturePassword

$failureNotes = @()
$checkResults = @()
$tokens = @{}
$fixtureState = $null
$apiLogPaths = @()
$apiProcess = $null
$canRunChecks = $false
$client = $null
$apiReused = Test-BaseUrlReachable -Url $baseUrl

# 1. Read-only target verification before any write.
$preflight = Invoke-TargetVerification -RepoRoot $repoRoot -AssemblyPath $assemblyPath -Environment $seedEnvironment -ApiDirectory $apiDirectory

$psqlPath = Get-F6PsqlPath
$connectionSource = Resolve-F6ConnectionStringSource -RepoRoot $repoRoot
$connection = ConvertFrom-F6PostgresConnectionString -ConnectionString $connectionSource.Value
if (@($connection.Problems).Count -gt 0) {
    $failureNotes += ('The local database connection could not be resolved: ' + (@($connection.Problems) -join '; '))
}

$guard = Test-F6FixtureTarget -VerificationPassed $preflight.passed -Report $preflight.report -Connection $connection -PsqlPath $psqlPath

try {
    if (-not $preflight.passed -or -not $guard.Ok) {
        $failureNotes += 'The read-only target verification or the fixture guard failed; no fixture write, no API start and no HTTP check occurred.'
        Write-F6Result -Passed $false -Phase 'target-verification' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    if ($f6SkipFixtureWrite) {
        $reportPath = Join-Path ([System.IO.Path]::GetTempPath()) ('ahlena-f6-fixtures-' + [Guid]::NewGuid().ToString('N') + '.sql')
        [System.IO.File]::WriteAllText($reportPath, (Get-F6FixtureSql), (New-Object System.Text.UTF8Encoding($false)))
        $failureNotes += "SkipFixtureWrite is set: the provisioning SQL was written to $reportPath and nothing else ran."
        Write-F6Result -Passed $false -Phase 'report-only' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    # 2. Provision the fixtures, then read them back.
    $fixtureWrite = Invoke-F6Psql -PsqlPath $psqlPath -Connection $connection -Sql (Get-F6FixtureSql) -Tag 'provision'
    if (-not $fixtureWrite.Ok) {
        $failureNotes += ('The fixture provisioning failed: ' + $fixtureWrite.ErrorSummary)
        Write-F6Result -Passed $false -Phase 'fixture-provisioning' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    $stateRead = Invoke-F6Psql -PsqlPath $psqlPath -Connection $connection -Sql (Get-F6StateQuery) -Tag 'state' -ExpectJson
    if (-not $stateRead.Ok) {
        $failureNotes += ('The fixture readback failed: ' + $stateRead.ErrorSummary)
        Write-F6Result -Passed $false -Phase 'fixture-readback' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    $fixtureState = $stateRead.Value
    $definition = Get-F6FixtureDefinition
    $adminAccountTypes = @(([string]$fixtureState.supportAdminAccountTypes) -split ',')
    $caregiverAccountTypes = @(([string]$fixtureState.caregiverAccountTypes) -split ',')
    $contentAdminAccountTypes = @(([string]$fixtureState.contentAdminAccountTypes) -split ',')
    $familyViewerSessionAccountTypes = @(([string]$fixtureState.familyViewerSessionAccountTypes) -split ',')
    if ([string]$fixtureState.supportAdminUserId -ne $definition.SupportAdmin.UserId -or
        [string]$fixtureState.caregiverUserId -ne $definition.Caregiver.UserId -or
        [int]$fixtureState.supportAdminStatus -ne 2 -or
        [int]$fixtureState.caregiverStatus -ne 2 -or
        [int]$fixtureState.minimalFixturePresentCount -ne 2 -or
        [string]$fixtureState.contentAdminUserId -ne $definition.ContentAdmin.UserId -or
        [int]$fixtureState.contentAdminStatus -ne 2 -or
        [string]$fixtureState.familyViewerSessionUserId -ne $definition.FamilyViewer.UserId -or
        [int]$fixtureState.familyViewerSessionStatus -ne 2 -or
        $adminAccountTypes -notcontains [string]$definition.SupportAdmin.AccountType -or
        $caregiverAccountTypes -notcontains [string]$definition.Caregiver.AccountType -or
        $contentAdminAccountTypes -notcontains [string]$definition.ContentAdmin.AccountType -or
        $familyViewerSessionAccountTypes -notcontains [string]$definition.FamilyViewer.AccountType -or
        [string]$fixtureState.activeFamilyId -ne $definition.FamilyActive.Id -or
        [int]$fixtureState.activeFamilyMemberCount -ne $definition.FamilyActive.MemberCount -or
        [string]$fixtureState.deletedFamilyId -ne $definition.FamilyDeleted.Id -or
        [string]$fixtureState.facilityId -ne $definition.Facility.Id -or
        [int]$fixtureState.visitCount -ne 2 -or
        [string]$fixtureState.approvedVisitId -ne $definition.VisitApproved.Id -or
        [string]$fixtureState.cancelledVisitId -ne $definition.VisitCancelled.Id) {
        $failureNotes += 'The fixture readback does not match the provisioned definition.'
        Write-F6Result -Passed $false -Phase 'fixture-readback' -FixtureReport $fixtureState | ConvertTo-Json -Depth 8
        exit 1
    }

    # 3. Start or reuse the API once.
    $apiLogPaths = @()
    if (-not $apiReused -and -not $f6SkipApiStart) {
        $logDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('ahlena-f6-verify-' + [Guid]::NewGuid().ToString('N'))
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
            Write-F6Result -Passed $false -Phase 'api-start' -FixtureReport $fixtureState | ConvertTo-Json -Depth 8
            exit 1
        }

        $apiLogPaths = @($outputLog, $errorLog)
    }

    $canRunChecks = $true
    $client = New-AhlenaHttpClient

    # 4. Login preflight for every actor this run needs, then one read-only pass
    #    over the whole plan (logout checks last).
    $sessions = Get-F6SessionIdentityDefinition
    foreach ($actor in $sessions.Keys) {
        $tokens[$actor] = Get-FixtureToken -Client $client -Url $baseUrl `
            -Email $sessions[$actor].Email -Password $f6FixturePassword -DeviceName $sessions[$actor].DeviceName
    }

    $plan = New-F6CheckPlan -Tokens $tokens -UnknownGuid ([Guid]::NewGuid().ToString())
    $checkResults += Invoke-PlannedChecks -Client $client -Url $baseUrl -Plan $plan -LogPath $apiLogPaths
}
catch {
    $failureNotes += ('The verification could not complete: ' + (Get-TrimmedRedactedLine $_.Exception.Message))
    if ($canRunChecks -and $tokens.Count -gt 0 -and $checkResults.Count -eq 0) {
        # The plan never ran: log out what was created so a failed run cannot
        # leave the fixture identities holding sessions.
        try {
            $logoutOnly = @(New-F6CheckPlan -Tokens $tokens -UnknownGuid ([Guid]::NewGuid().ToString()) |
                Where-Object { $_.Name -like 'session.logout.*' })
            $checkResults += Invoke-PlannedChecks -Client $client -Url $baseUrl -Plan $logoutOnly -LogPath $apiLogPaths
        }
        catch {
            $failureNotes += ('Session logout could not complete: ' + (Get-TrimmedRedactedLine $_.Exception.Message))
        }
    }
}
finally {
    if ($client) { $client.Dispose() }

    # 5. Stop any API this script started, so a failed run cannot leave an
    #    orphaned listener behind.
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force
        Wait-Process -Id $apiProcess.Id -Timeout 15 -ErrorAction SilentlyContinue
    }
}

$failedCount = @($checkResults | Where-Object { -not $_.passed }).Count
$overallPassed = ($failureNotes.Count -eq 0) -and ($failedCount -eq 0) -and $canRunChecks

Write-F6Result -Passed $overallPassed -Phase 'complete' -FixtureReport $fixtureState | ConvertTo-Json -Depth 8

if (-not $overallPassed) { exit 1 }
