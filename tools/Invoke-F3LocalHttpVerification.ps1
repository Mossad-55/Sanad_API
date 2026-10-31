#requires -Version 5.1
<#
.SYNOPSIS
    F3 approved-facility revision carry-forward - small local HTTP verification
    adapter over the L1 fixture library.

.DESCRIPTION
    A bounded adapter (not a second harness). It:

      1. Runs the L1 read-only target verification for all nine module contexts,
         migration state and non-live integration state. No write happens until
         that report passes.
      2. Provisions three dedicated synthetic identities - a care-home owner, a
         second care-home owner (isolation probe) and a SupportAdmin reviewer -
         with INSERT-only, guarded, idempotent SQL through the local psql
         client. The shared fixture password hash is copied, never invented, and
         the shared `content.admin@test.sanad.local` identity is left untouched.
      3. Creates ONE approved care-home facility through the real owner/admin
         HTTP flows (owner onboarding, document upload, media upload, submit,
         Admin verify and review). A rerun reuses the same facility.
      4. Proves revision carry-forward end to end: nullable openRevisionId,
         GET creates nothing, phone-only edit inherits documents/media, re-save
         adds no duplicates, stale versions are refused, owner isolation holds,
         submit needs no re-upload, removing an inherited draft image keeps the
         shared approved file and never resurrects it, and a replacement goes
         through the normal submit/review path.
      5. Logs out every session it created through the existing logout route.

    Safety properties:
      - The base URL must be a loopback HTTP(S) listener; there is no remote
        fallback and no way to pass one.
      - Live SMS/SMTP/Push provider configuration is cleared for every process
        this script starts; migrations stay disabled.
      - Fixture identities are INSERT-only, guarded and idempotent: nothing is
        updated, deleted or reset by the fixture SQL.
      - Mutations are never retried automatically.
      - Tokens, credentials, connection strings and payloads are never printed.
      - Offline tests dot-source this file with -LibraryOnly and exercise only
        the pure functions; no API, database or HTTP call occurs there.

.PARAMETER BaseUrl
    Loopback HTTP(S) base URL of the API. Defaults to the F3-only port 5241 so
    it cannot collide with the other local verification adapters.

.PARAMETER ExpectedWorktreeRoot
    Optional. When supplied it must equal this script's repository root.

.PARAMETER ApiAssemblyPath
    Optional. Built API assembly. Defaults to the Release output.

.PARAMETER FixturePassword
    Synthetic fixture password, shared with the existing L1 fixture identities.
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
    [string]$BaseUrl = 'http://localhost:5241',

    [string]$ExpectedWorktreeRoot,

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
$f3LibraryOnly      = [bool]$LibraryOnly
$f3BaseUrl          = $BaseUrl
$f3ApiAssemblyPath  = $ApiAssemblyPath
$f3FixturePassword  = $FixturePassword
$f3SkipApiStart     = [bool]$SkipApiStart
$f3SkipFixtureWrite = [bool]$SkipFixtureWrite

. (Join-Path $PSScriptRoot 'Invoke-LocalFixtureVerification.ps1') -LibraryOnly

# ---------------------------------------------------------------------------
# Fixture definition and pure helpers
# ---------------------------------------------------------------------------

function Get-F3FixtureDefinition {
    <#
        The three dedicated synthetic identities plus the fixed references they
        need. Account types: CareHomeOwner = 8, SupportAdmin = 7. UserStatus
        Active = 2. The password hash is copied from the shared L1 fixture
        identity, so no credential is ever stored in this repository.
    #>
    [CmdletBinding()]
    param()

    return [ordered]@{
        Owner       = [ordered]@{
            Email         = 'carehome.owner.f3@test.sanad.local'
            UserId        = '019a3f3a-0001-7000-8000-000000000001'
            PhoneNumber   = '+201000000021'
            ArabicName    = 'F3 care-home owner (arabic name slot)'
            EnglishName   = 'F3 Carry-Forward Care Home Owner'
            AccountType   = 8
            AccountTypeId = '019a3f3a-0002-7000-8000-000000000001'
        }
        OwnerSecond = [ordered]@{
            Email         = 'carehome.owner2.f3@test.sanad.local'
            UserId        = '019a3f3a-0001-7000-8000-000000000002'
            PhoneNumber   = '+201000000022'
            ArabicName    = 'F3 isolation owner (arabic name slot)'
            EnglishName   = 'F3 Isolation Care Home Owner'
            AccountType   = 8
            AccountTypeId = '019a3f3a-0002-7000-8000-000000000002'
        }
        Admin       = [ordered]@{
            Email         = 'carehome.admin.f3@test.sanad.local'
            UserId        = '019a3f3a-0001-7000-8000-000000000003'
            PhoneNumber   = '+201000000023'
            ArabicName    = 'F3 care-home reviewer (arabic name slot)'
            EnglishName   = 'F3 Care-Home Operational Reviewer'
            AccountType   = 7
            AccountTypeId = '019a3f3a-0002-7000-8000-000000000003'
        }
        PasswordSource = 'content.admin@test.sanad.local'
    }
}

function Get-F3ProfileDraft {
    <#
        The deterministic bilingual profile every save sends. Only the contact
        phone changes between cycles, so every carry-forward assertion is about
        inherited assets rather than edited content. All values stay ASCII so
        the script is encoding-independent.
    #>
    [CmdletBinding()]
    param([string]$Phone = '+201000000099')

    return [ordered]@{
        arabicName                    = 'F3 arabic name slot for the carry-forward care home'
        englishName                   = 'F3 Carry-Forward Care Home'
        arabicDescription             = 'F3 arabic description slot for local verification.'
        englishDescription            = 'Deterministic local verification profile.'
        contactName                   = 'F3 Verification Contact'
        contactPhone                  = $Phone
        contactEmail                  = $null
        governorate                   = 'Cairo'
        city                          = 'Cairo'
        area                          = 'Nasr City'
        address                       = '1 Verification Street'
        arabicAdmissionConditions     = 'F3 arabic admission conditions slot.'
        englishAdmissionConditions    = 'Local verification admission conditions.'
        amenities                     = @()
        medicalServices               = @()
    }
}

function ConvertFrom-F3PostgresConnectionString {
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

function Resolve-F3ConnectionStringSource {
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

function Test-F3FixtureTarget {
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

function Get-F3FixtureSql {
    <#
        Idempotent, INSERT-only fixture provisioning. Every statement is
        guarded: a rerun inserts nothing, and nothing is ever updated or
        deleted. The shared fixture password hash is copied, never invented.
    #>
    [CmdletBinding()]
    param()

    $definition = Get-F3FixtureDefinition

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

    $ownerSql = $identityInsert -f $definition.Owner.UserId, $definition.Owner.ArabicName, $definition.Owner.EnglishName, $definition.Owner.PhoneNumber, $definition.Owner.Email, $definition.PasswordSource, $definition.Owner.AccountTypeId, $definition.Owner.AccountType
    $ownerSecondSql = $identityInsert -f $definition.OwnerSecond.UserId, $definition.OwnerSecond.ArabicName, $definition.OwnerSecond.EnglishName, $definition.OwnerSecond.PhoneNumber, $definition.OwnerSecond.Email, $definition.PasswordSource, $definition.OwnerSecond.AccountTypeId, $definition.OwnerSecond.AccountType
    $adminSql = $identityInsert -f $definition.Admin.UserId, $definition.Admin.ArabicName, $definition.Admin.EnglishName, $definition.Admin.PhoneNumber, $definition.Admin.Email, $definition.PasswordSource, $definition.Admin.AccountTypeId, $definition.Admin.AccountType

    return @"
\set ON_ERROR_STOP on
BEGIN;

-- Fail closed when the shared fixture password hash is missing: a fixture
-- identity without a usable hash could never authenticate, and emitting one
-- would silently break every later check.
DO `$f3guard`$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM identity.users
        WHERE email = '$($definition.PasswordSource)' AND password_hash IS NOT NULL)
    THEN
        RAISE EXCEPTION 'F3 fixture guard: the shared fixture password hash is missing';
    END IF;
END
`$f3guard`$;

$ownerSql
$ownerSecondSql
$adminSql

COMMIT;
"@
}

function Get-F3IdentityStateQuery {
    <#
        Read-only readback of the three fixture identities and their live
        sessions, so the HTTP proof starts from verified ids and the run can
        prove it left no fixture session behind.
    #>
    [CmdletBinding()]
    param()

    $definition = Get-F3FixtureDefinition
    $emails = @($definition.Owner.Email, $definition.OwnerSecond.Email, $definition.Admin.Email)
    $list = (@($emails | ForEach-Object { "'" + $_ + "'" }) -join ', ')

    return @"
SELECT json_build_object(
    'users', coalesce((
        SELECT json_agg(json_build_object(
            'email', u.email,
            'id', u.id::text,
            'status', u.status,
            'accountTypes', coalesce((SELECT string_agg(a.account_type::text, ',' ORDER BY a.account_type) FROM identity.user_accounts a WHERE a.user_id = u.id), '')
        ) ORDER BY u.email)
        FROM identity.users u WHERE u.email IN ($list)), '[]'::json),
    'activeSessions', coalesce((
        SELECT json_agg(json_build_object('email', u.email, 'deviceName', s.device_name) ORDER BY u.email)
        FROM identity.device_sessions s JOIN identity.users u ON u.id = s.user_id
        WHERE u.email IN ($list) AND s.revoked_on_utc IS NULL AND s.expires_on_utc > now()), '[]'::json)
);
"@
}

function Get-F3FacilityStateQuery {
    <#
        Read-only readback of the fixture facility, its revisions and their
        assets. Storage keys and per-revision asset sets are not exposed by the
        API, so carry-forward equivalence is asserted from this readback.
    #>
    [CmdletBinding()]
    param([string]$OwnerEmail)

    return @"
SELECT COALESCE((
    SELECT json_build_object(
        'exists', true,
        'facilityId', f.id::text,
        'status', f.status,
        'version', f.version,
        'approvedRevisionId', coalesce(f.approved_revision_id::text, ''),
        'submittedRevisionId', coalesce(f.submitted_revision_id::text, ''),
        'openRevisionId', coalesce((SELECT r.id::text FROM care_homes.profile_revisions r WHERE r.care_home_id = f.id AND r.submitted_on_utc IS NULL ORDER BY r.revision_number DESC LIMIT 1), ''),
        'revisionCount', (SELECT count(*) FROM care_homes.profile_revisions r WHERE r.care_home_id = f.id),
        'documentCount', (SELECT count(*) FROM care_homes.documents d WHERE d.care_home_id = f.id),
        'mediaCount', (SELECT count(*) FROM care_homes.profile_media m WHERE m.profile_revision_id IN (SELECT r.id FROM care_homes.profile_revisions r WHERE r.care_home_id = f.id)),
        'approvedDocuments', coalesce((
            SELECT json_agg(json_build_object('type', d.type, 'key', d.private_storage_key, 'status', d.status, 'nonExpiring', d.verified_non_expiring) ORDER BY d.type)
            FROM care_homes.documents d WHERE d.profile_revision_id = f.approved_revision_id), '[]'::json),
        'approvedMedia', coalesce((
            SELECT json_agg(json_build_object('id', m.id::text, 'kind', m.kind, 'position', m.position, 'key', m.storage_key) ORDER BY m.kind, m.position)
            FROM care_homes.profile_media m WHERE m.profile_revision_id = f.approved_revision_id), '[]'::json),
        'openDocuments', coalesce((
            SELECT json_agg(json_build_object('type', d.type, 'key', d.private_storage_key, 'status', d.status, 'nonExpiring', d.verified_non_expiring) ORDER BY d.type)
            FROM care_homes.documents d WHERE d.profile_revision_id = (SELECT r.id FROM care_homes.profile_revisions r WHERE r.care_home_id = f.id AND r.submitted_on_utc IS NULL ORDER BY r.revision_number DESC LIMIT 1)), '[]'::json),
        'openMedia', coalesce((
            SELECT json_agg(json_build_object('id', m.id::text, 'kind', m.kind, 'position', m.position, 'key', m.storage_key) ORDER BY m.kind, m.position)
            FROM care_homes.profile_media m WHERE m.profile_revision_id = (SELECT r.id FROM care_homes.profile_revisions r WHERE r.care_home_id = f.id AND r.submitted_on_utc IS NULL ORDER BY r.revision_number DESC LIMIT 1)), '[]'::json)
    )
    FROM care_homes.facilities f
    WHERE f.owner_user_id = (SELECT u.id FROM identity.users u WHERE u.email = '$OwnerEmail')
), json_build_object('exists', false));
"@
}

function Get-F3AssetSignature {
    <#
        A stable, order-independent signature of one asset set from the psql
        readback. Documents are keyed by type, media by kind+position; the
        private storage key is the payload that must survive carry-forward.
    #>
    [CmdletBinding()]
    param([AllowNull()][object]$Assets, [string]$Kind = 'document')

    $entries = @()
    foreach ($asset in @($Assets)) {
        if ($null -eq $asset) { continue }
        if ($Kind -eq 'document') {
            $entries += ('{0}|{1}|{2}|{3}' -f [int]$asset.type, [string]$asset.key, [int]$asset.status, [bool]$asset.nonExpiring)
        }
        else {
            $entries += ('{0}|{1}|{2}' -f [int]$asset.kind, [int]$asset.position, [string]$asset.key)
        }
    }

    return (@($entries | Sort-Object) -join '; ')
}

# ---------------------------------------------------------------------------
# Runtime helpers
# ---------------------------------------------------------------------------

function Get-F3PsqlPath {
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

function Invoke-F3PsqlProcess {
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

    $tempFile = Join-Path ([System.IO.Path]::GetTempPath()) ("ahlena-f3-" + $Tag + "-" + [Guid]::NewGuid().ToString('N') + ".sql")
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

function Get-F3SafeOutputSummary {
    [CmdletBinding()]
    param([string[]]$Lines, [int]$MaxLength = 300)

    $all = @($Lines)
    for ($index = $all.Count - 1; $index -ge 0; $index--) {
        if ([string]::IsNullOrWhiteSpace($all[$index])) { continue }
        return (Get-TrimmedRedactedLine -Text $all[$index] -MaxLength $MaxLength)
    }
    return 'no client output'
}

function Invoke-F3PsqlQuery {
    [CmdletBinding()]
    param(
        [string]$PsqlPath,
        [hashtable]$Connection,
        [string]$Sql,
        [string]$Tag
    )

    $run = Invoke-F3PsqlProcess -PsqlPath $PsqlPath -Connection $Connection -Sql $Sql -Tag $Tag
    if ($run.ExitCode -ne 0) {
        return @{ Ok = $false; Value = $null; ErrorSummary = "psql exited with $($run.ExitCode): $(Get-F3SafeOutputSummary -Lines $run.Lines)" }
    }

    $text = (@($run.Lines) -join "`n").Trim()
    $value = ConvertFrom-SafeJson $text
    if ($null -eq $value) {
        return @{ Ok = $false; Value = $null; ErrorSummary = 'the read-only query did not return parseable JSON' }
    }

    return @{ Ok = $true; Value = $value; ErrorSummary = '' }
}

function Invoke-F3PsqlScript {
    [CmdletBinding()]
    param(
        [string]$PsqlPath,
        [hashtable]$Connection,
        [string]$Sql,
        [string]$Tag
    )

    $run = Invoke-F3PsqlProcess -PsqlPath $PsqlPath -Connection $Connection -Sql $Sql -Tag $Tag
    if ($run.ExitCode -ne 0) {
        return @{ Ok = $false; ErrorSummary = "psql exited with $($run.ExitCode): $(Get-F3SafeOutputSummary -Lines $run.Lines)" }
    }

    return @{ Ok = $true; ErrorSummary = '' }
}

function Invoke-F3Json {
    [CmdletBinding()]
    param(
        [System.Net.Http.HttpClient]$Client,
        [string]$Method,
        [string]$Url,
        [AllowNull()][AllowEmptyString()][string]$Body,
        [AllowNull()][AllowEmptyString()][string]$Token
    )

    $response = Invoke-AhlenaHttpRequest -Client $Client -Method $Method -Url $Url -Body $Body -Token $Token
    return @{
        Status = [int]$response.Status
        Body   = [string]$response.Body
        Parsed = (ConvertFrom-SafeJson ([string]$response.Body))
    }
}

function Invoke-F3Multipart {
    <#
        One multipart/form-data POST carrying the numeric form fields and the
        single private file the care-home upload routes expect.
    #>
    [CmdletBinding()]
    param(
        [System.Net.Http.HttpClient]$Client,
        [string]$Url,
        [hashtable]$Fields,
        [hashtable]$File,
        [string]$Token
    )

    Enable-HttpClientType

    $content = New-Object System.Net.Http.MultipartFormDataContent
    try {
        foreach ($key in $Fields.Keys) {
            $field = New-Object System.Net.Http.StringContent -ArgumentList ([string]$Fields[$key])
            $content.Add($field, $key)
        }
        if ($null -ne $File) {
            $payload = New-Object System.Net.Http.ByteArrayContent -ArgumentList (,[byte[]]$File.Bytes)
            $payload.Headers.ContentType = New-Object System.Net.Http.Headers.MediaTypeHeaderValue -ArgumentList ([string]$File.ContentType)
            $content.Add($payload, [string]$File.Name, [string]$File.FileName)
        }

        $request = New-Object System.Net.Http.HttpRequestMessage -ArgumentList ([System.Net.Http.HttpMethod]::Post), $Url
        try {
            if ($Token) {
                $request.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue -ArgumentList ('Bearer', $Token)
            }
            $request.Content = $content
            $response = $Client.SendAsync($request, [System.Net.Http.HttpCompletionOption]::ResponseContentRead).GetAwaiter().GetResult()
            try {
                return @{
                    Status = [int]$response.StatusCode
                    Body   = [string]$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                }
            }
            finally { $response.Dispose() }
        }
        finally { $request.Dispose() }
    }
    finally { $content.Dispose() }
}

function Get-F3Array {
    <# PowerShell treats @($null) as a one-element array; this returns a real empty array instead. #>
    [CmdletBinding()]
    param([AllowNull()][object]$Value)

    $items = @()
    if ($null -eq $Value) { return $items }
    foreach ($item in @($Value)) {
        if ($null -ne $item) { $items += $item }
    }
    return $items
}

function Add-F3HttpCheck {
    <#
        Runs one planned check through the L1 outcome evaluator. The response is
        already produced, so a matching captured log line is looked up here.
    #>
    [CmdletBinding()]
    param(
        [string]$Name,
        [string]$Method,
        [string]$Path,
        [string]$Actor,
        [string]$Expected,
        [int]$ExpectedStatus,
        [AllowNull()][object]$Response,
        [AllowNull()][scriptblock]$Assert
    )

    $check = [ordered]@{
        Name           = $Name
        Method         = $Method
        Path           = $Path
        Actor          = $Actor
        Expected       = $Expected
        ExpectedStatus = $ExpectedStatus
    }
    if ($null -ne $Assert) { $check['Assert'] = $Assert }

    $parsed = $null
    $traceId = $null
    $status = 0
    $bodyText = ''
    if ($null -ne $Response) {
        $status = [int]$Response.Status
        $bodyText = [string]$Response.Body
        $parsed = ConvertFrom-SafeJson $bodyText
        if ($null -ne $parsed -and $parsed.PSObject.Properties.Name -contains 'traceId') { $traceId = [string]$parsed.traceId }
    }

    $location = $null
    if ($traceId -and @($script:f3LogPaths).Count -gt 0) {
        $location = Get-TraceLocation -TraceId $traceId -LogLines (Get-CapturedLogLines -Path $script:f3LogPaths)
    }

    $script:f3Checks += Get-CheckOutcome -Check $check -Status $status -BodyText $bodyText -TraceId $traceId -Location $location
}

function Add-F3EvidenceCheck {
    <#
        One read-only evidence check whose facts come from the psql readback or
        from values the HTTP responses already produced. It uses the same
        evaluator shape so the report stays uniform.
    #>
    [CmdletBinding()]
    param(
        [string]$Name,
        [string]$Expected,
        [bool]$Passed,
        [string]$Evidence,
        [AllowNull()][object]$Problems
    )

    $problemList = @()
    foreach ($problem in @($Problems)) {
        if ($null -ne $problem -and -not [string]::IsNullOrWhiteSpace([string]$problem)) { $problemList += [string]$problem }
    }

    $assert = ({
        param($status, $body)
        return @{ Actual = $Evidence; Problems = @($problemList) }
    }).GetNewClosure()

    Add-F3HttpCheck -Name $Name -Method 'SQL' -Path 'care_homes/identity (read-only)' -Actor 'verifier' `
        -Expected $Expected -ExpectedStatus 0 -Response $null -Assert $assert
}

function New-F3LogoutChecks {
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

function Write-F3Result {
    <#
        One JSON report for the run. It carries the target preflight, the
        fixture guard, the fixture state, the checks and the notes; it never
        carries tokens, credentials, connection strings or response bodies.
    #>
    [CmdletBinding()]
    param(
        [bool]$Passed,
        [string]$Phase,
        [AllowNull()][object]$IdentityState,
        [AllowNull()][object]$FacilityState
    )

    return [ordered]@{
        workflow           = 'f3-approved-facility-carry-forward-verification'
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
        fixtureIdentities  = $IdentityState
        fixtureFacility    = $FacilityState
        checks             = $script:f3Checks
        results            = [ordered]@{
            total  = $script:f3Checks.Count
            passed = @($script:f3Checks | Where-Object { $_.passed }).Count
            failed = @($script:f3Checks | Where-Object { -not $_.passed }).Count
        }
        notes              = $script:failureNotes
        limitation         = @(
            'Startup migrations stay disabled for every process this script starts; migration state is only read.',
            'A local API start also starts the repository''s existing outbox/notification background services. SMS, SMTP and Firebase inputs are cleared so no provider can be contacted, and push runs the Development no-op sender.',
            'Trace correlation is reported as unavailable when no captured local log line carries the trace id of a failed check.',
            'The facility itself and its revisions/documents/media are created through the real owner/admin HTTP flows, not by fixture SQL; only the three synthetic identities are inserted directly, guarded and INSERT-only. Nothing the run writes is reset.',
            'Private storage keys and per-revision asset sets are read back read-only from PostgreSQL because the API does not expose them.'
        )
    }
}

# ---------------------------------------------------------------------------
# Main flow
# ---------------------------------------------------------------------------

if ($f3LibraryOnly) { return }

$repoRoot = Get-RepositoryRoot

if ($ExpectedWorktreeRoot) {
    $expected = Resolve-FullPath $ExpectedWorktreeRoot
    if (-not $expected.Equals($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'ExpectedWorktreeRoot does not match this script''s repository root.'
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'AGENTS.md') -PathType Leaf)) {
    throw 'Run this script from the Ahlena API repository checkout.'
}

$head = Get-PinnedHead $repoRoot
$uri = Assert-LoopbackBaseUrl $f3BaseUrl
$baseUrl = "$($uri.Scheme)://$($uri.Authority)"

$apiDirectory = Join-Path $repoRoot 'src/API/Ahlena.API'
if ([string]::IsNullOrWhiteSpace($f3ApiAssemblyPath)) {
    $f3ApiAssemblyPath = Join-Path $apiDirectory 'bin/Release/net10.0/Ahlena.API.dll'
}
$assemblyPath = Resolve-FullPath $f3ApiAssemblyPath
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw 'The built API assembly was not found. Build Release first (dotnet build Ahlena.slnx -c Release).'
}

$seedEnvironment = Get-LocalFixtureEnvironment -Password $f3FixturePassword -EnableSeed
$serveEnvironment = Get-LocalFixtureEnvironment -Password $f3FixturePassword

$script:failureNotes = @()
$script:f3Checks = @()
$script:f3LogPaths = @()
$script:apiProcess = $null
$script:canRunChecks = $false
$script:f3Tokens = @{}
$client = $null
$identityState = $null
$facilityState = $null
$apiReused = Test-BaseUrlReachable -Url $baseUrl

# 1. Read-only target verification before any write.
$preflight = Invoke-TargetVerification -RepoRoot $repoRoot -AssemblyPath $assemblyPath -Environment $seedEnvironment -ApiDirectory $apiDirectory

$psqlPath = Get-F3PsqlPath
$connectionSource = Resolve-F3ConnectionStringSource -RepoRoot $repoRoot
$connection = ConvertFrom-F3PostgresConnectionString -ConnectionString $connectionSource.Value
if (@($connection.Problems).Count -gt 0) {
    $script:failureNotes += ('The local database connection could not be resolved: ' + (@($connection.Problems) -join '; '))
}

$guard = Test-F3FixtureTarget -VerificationPassed $preflight.passed -Report $preflight.report -Connection $connection -PsqlPath $psqlPath

try {
    if (-not $preflight.passed -or -not $guard.Ok) {
        $script:failureNotes += 'The read-only target verification or the fixture guard failed; no fixture write, no API start and no database write occurred.'
        Write-F3Result -Passed $false -Phase 'target-verification' | ConvertTo-Json -Depth 8
        exit 1
    }

    if ($f3SkipFixtureWrite) {
        $reportPath = Join-Path ([System.IO.Path]::GetTempPath()) ('ahlena-f3-fixtures-' + [Guid]::NewGuid().ToString('N') + '.sql')
        [System.IO.File]::WriteAllText($reportPath, (Get-F3FixtureSql), (New-Object System.Text.UTF8Encoding($false)))
        $script:failureNotes += "SkipFixtureWrite is set: the provisioning SQL was written to $reportPath and nothing else ran."
        Write-F3Result -Passed $false -Phase 'report-only' | ConvertTo-Json -Depth 8
        exit 1
    }

    # 2. Provision the fixtures (INSERT-only, guarded), then read them back.
    $fixtureWrite = Invoke-F3PsqlScript -PsqlPath $psqlPath -Connection $connection -Sql (Get-F3FixtureSql) -Tag 'provision'
    if (-not $fixtureWrite.Ok) {
        $script:failureNotes += ('The fixture provisioning failed: ' + $fixtureWrite.ErrorSummary)
        Write-F3Result -Passed $false -Phase 'fixture-provisioning' | ConvertTo-Json -Depth 8
        exit 1
    }

    $identityRead = Invoke-F3PsqlQuery -PsqlPath $psqlPath -Connection $connection -Sql (Get-F3IdentityStateQuery) -Tag 'identities'
    if (-not $identityRead.Ok) {
        $script:failureNotes += ('The fixture identity readback failed: ' + $identityRead.ErrorSummary)
        Write-F3Result -Passed $false -Phase 'fixture-readback' | ConvertTo-Json -Depth 8
        exit 1
    }
    $identityState = $identityRead.Value

    $definition = Get-F3FixtureDefinition
    $expectedIdentities = @(
        @{ Email = $definition.Owner.Email; UserId = $definition.Owner.UserId; AccountType = $definition.Owner.AccountType }
        @{ Email = $definition.OwnerSecond.Email; UserId = $definition.OwnerSecond.UserId; AccountType = $definition.OwnerSecond.AccountType }
        @{ Email = $definition.Admin.Email; UserId = $definition.Admin.UserId; AccountType = $definition.Admin.AccountType }
    )
    $identityProblems = @()
    foreach ($expected in $expectedIdentities) {
        $match = @(Get-F3Array $identityState.users | Where-Object { [string]$_.email -eq $expected.Email })
        if ($match.Count -ne 1) {
            $identityProblems += "the fixture identity $($expected.Email) is not present exactly once"
            continue
        }
        if ([string]$match[0].id -ne $expected.UserId) { $identityProblems += "identity $($expected.Email) has an unexpected id" }
        if ([int]$match[0].status -ne 2) { $identityProblems += "identity $($expected.Email) is not Active" }
        $accounts = @(([string]$match[0].accountTypes) -split ',')
        if ($accounts -notcontains [string]$expected.AccountType) { $identityProblems += "identity $($expected.Email) lacks account type $($expected.AccountType)" }
    }
    Add-F3EvidenceCheck -Name 'fixture.identities-provisioned' `
        -Expected 'Three dedicated Active fixture identities exist with the expected ids and account types (INSERT-only, guarded)' `
        -Passed ($identityProblems.Count -eq 0) `
        -Evidence ("identities=" + (Get-F3Array $identityState.users).Count) `
        -Problems $identityProblems

    if ($identityProblems.Count -gt 0) {
        $script:failureNotes += 'The fixture identity readback does not match the provisioned definition; no API start and no HTTP check occurred.'
        Write-F3Result -Passed $false -Phase 'fixture-readback' -IdentityState $identityState | ConvertTo-Json -Depth 8
        exit 1
    }

    # 3. Start or reuse the API once.
    if (-not $apiReused -and -not $f3SkipApiStart) {
        $logDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('ahlena-f3-verify-' + [Guid]::NewGuid().ToString('N'))
        [System.IO.Directory]::CreateDirectory($logDirectory) | Out-Null
        $outputLog = Join-Path $logDirectory 'api.stdout.log'
        $errorLog = Join-Path $logDirectory 'api.stderr.log'

        $script:apiProcess = Start-FixtureApiProcess -AssemblyPath $assemblyPath -WorkingDirectory $apiDirectory -Url $baseUrl -Environment $serveEnvironment -OutputLogPath $outputLog -ErrorLogPath $errorLog

        $ready = Wait-ForEndpoint -Url $baseUrl -TimeoutSeconds 90 -Process $script:apiProcess
        if (-not $ready) {
            if ($script:apiProcess -and -not $script:apiProcess.HasExited) {
                Stop-Process -Id $script:apiProcess.Id -Force
                Wait-Process -Id $script:apiProcess.Id -Timeout 15 -ErrorAction SilentlyContinue
            }
            $script:apiProcess = $null
            $logLines = @(Get-CapturedLogLines -Path @($outputLog, $errorLog))
            $lastLogLine = $logLines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1
            if ($lastLogLine) { $script:failureNotes += ('Last captured API log line: ' + (Get-TrimmedRedactedLine $lastLogLine)) }
            $script:failureNotes += 'The Development API did not become reachable within 90 seconds.'
            Write-F3Result -Passed $false -Phase 'api-start' -IdentityState $identityState | ConvertTo-Json -Depth 8
            exit 1
        }

        $script:f3LogPaths = @($outputLog, $errorLog)
    }

    $script:canRunChecks = $true
    $client = New-AhlenaHttpClient

    # 4. Login preflight for every actor this run needs.
    $sessionDefinition = [ordered]@{
        'owner'        = [ordered]@{ Email = $definition.Owner.Email; DeviceName = 'f3-carry-forward-owner' }
        'owner-second' = [ordered]@{ Email = $definition.OwnerSecond.Email; DeviceName = 'f3-carry-forward-owner-two' }
        'admin'        = [ordered]@{ Email = $definition.Admin.Email; DeviceName = 'f3-carry-forward-admin' }
    }
    foreach ($actor in $sessionDefinition.Keys) {
        $script:f3Tokens[$actor] = Get-FixtureToken -Client $client -Url $baseUrl `
            -Email $sessionDefinition[$actor].Email -Password $f3FixturePassword -DeviceName $sessionDefinition[$actor].DeviceName
    }

    $ownerToken = $script:f3Tokens['owner']
    $ownerSecondToken = $script:f3Tokens['owner-second']
    $adminToken = $script:f3Tokens['admin']

    $png = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/F5sAAAAASUVORK5CYII=')
    $mineUrl = "$baseUrl/api/v1/care-homes/facilities/mine"
    $saveProfileUrl = "$mineUrl/profile"
    $uploadDocumentUrl = "$mineUrl/documents"
    $uploadMediaUrl = "$mineUrl/media"
    $submitUrl = "$mineUrl/submit"
    $adminBase = "$baseUrl/api/v1/admin/care-homes"

    function Get-F3OwnerProfile {
        return Invoke-F3Json -Client $client -Method 'GET' -Url $mineUrl -Body $null -Token $ownerToken
    }

    # 5. Ensure the facility exists and is Approved with no editable draft.
    $facilityRead = Invoke-F3PsqlQuery -PsqlPath $psqlPath -Connection $connection -Sql (Get-F3FacilityStateQuery -OwnerEmail $definition.Owner.Email) -Tag 'facility'
    if (-not $facilityRead.Ok) {
        throw ('The fixture facility readback failed: ' + $facilityRead.ErrorSummary)
    }
    $facilityState = $facilityRead.Value
    $facilityId = ''
    $bootstrapSource = 'reused-existing-approved-facility'
    $bootstrapProblems = @()

    if (-not [bool]$facilityState.exists) {
        $bootstrapSource = 'created-through-the-owner-and-admin-flows'

        $created = Invoke-F3Json -Client $client -Method 'POST' -Url "$baseUrl/api/v1/care-homes/facilities" -Body $null -Token $ownerToken
        if ($created.Status -ne 201 -or $null -eq $created.Parsed) {
            $bootstrapProblems += "the facility could not be created: status=$($created.Status) $(Get-SafeErrorSummary $created.Parsed)"
        }
        else {
            $facilityId = [string]$created.Parsed.id
        }

        if ($bootstrapProblems.Count -eq 0) {
            $mine = Get-F3OwnerProfile
            if ($mine.Status -ne 200) { $bootstrapProblems += "the fresh facility could not be read back: status=$($mine.Status)" }
        }

        if ($bootstrapProblems.Count -eq 0) {
            $saveBody = (@{ expectedVersion = [int]$mine.Parsed.version; draft = (Get-F3ProfileDraft) } | ConvertTo-Json -Depth 6 -Compress)
            $save = Invoke-F3Json -Client $client -Method 'PUT' -Url $saveProfileUrl -Body $saveBody -Token $ownerToken
            if ($save.Status -ne 200) { $bootstrapProblems += "the profile draft could not be saved: status=$($save.Status) $(Get-SafeErrorSummary $save.Parsed)" }
        }

        foreach ($documentType in 1..4) {
            if ($bootstrapProblems.Count -gt 0) { break }
            $mine = Get-F3OwnerProfile
            $upload = Invoke-F3Multipart -Client $client -Url $uploadDocumentUrl `
                -Fields (@{ expectedVersion = [string][int]$mine.Parsed.version; type = [string]$documentType }) `
                -File (@{ Name = 'file'; FileName = 'document.png'; ContentType = 'image/png'; Bytes = $png }) `
                -Token $ownerToken
            if ($upload.Status -ne 200) {
                $bootstrapProblems += "document type $documentType could not be uploaded: status=$($upload.Status) $(Get-SafeErrorSummary (ConvertFrom-SafeJson $upload.Body))"
            }
        }

        foreach ($kind in 1..2) {
            if ($bootstrapProblems.Count -gt 0) { break }
            $mine = Get-F3OwnerProfile
            $upload = Invoke-F3Multipart -Client $client -Url $uploadMediaUrl `
                -Fields (@{ expectedVersion = [string][int]$mine.Parsed.version; kind = [string]$kind }) `
                -File (@{ Name = 'file'; FileName = 'media.png'; ContentType = 'image/png'; Bytes = $png }) `
                -Token $ownerToken
            if ($upload.Status -ne 200) {
                $bootstrapProblems += "profile media kind $kind could not be uploaded: status=$($upload.Status) $(Get-SafeErrorSummary (ConvertFrom-SafeJson $upload.Body))"
            }
        }

        if ($bootstrapProblems.Count -eq 0) {
            $mine = Get-F3OwnerProfile
            $submitBody = (@{ expectedVersion = [int]$mine.Parsed.version } | ConvertTo-Json -Compress)
            $submit = Invoke-F3Json -Client $client -Method 'POST' -Url $submitUrl -Body $submitBody -Token $ownerToken
            if ($submit.Status -ne 200 -or [int]$submit.Parsed.status -ne 2) {
                $bootstrapProblems += "the first submission did not reach PendingReview: status=$($submit.Status) $(Get-SafeErrorSummary $submit.Parsed)"
            }
        }

        if ($bootstrapProblems.Count -eq 0) {
            $detail = Invoke-F3Json -Client $client -Method 'GET' -Url "$adminBase/$facilityId" -Body $null -Token $adminToken
            if ($detail.Status -ne 200) {
                $bootstrapProblems += "the Admin application detail could not be read: status=$($detail.Status)"
            }
            else {
                $pendingDocuments = @(Get-F3Array $detail.Parsed.documents | Where-Object { [int]$_.status -eq 1 })
                if ($pendingDocuments.Count -ne 4) {
                    $bootstrapProblems += "the submitted revision exposes $($pendingDocuments.Count) pending documents, expected 4"
                }
                else {
                    $version = [int]$detail.Parsed.version
                    foreach ($document in $pendingDocuments) {
                        $verifyBody = (@{ expectedVersion = $version; expiryDate = $null; confirmNonExpiring = $true } | ConvertTo-Json -Compress)
                        $verify = Invoke-F3Json -Client $client -Method 'POST' -Url "$adminBase/$facilityId/documents/$($document.id)/verify" -Body $verifyBody -Token $adminToken
                        if ($verify.Status -ne 200) {
                            $bootstrapProblems += "document $($document.id) could not be verified: status=$($verify.Status) $(Get-SafeErrorSummary $verify.Parsed)"
                            break
                        }
                        $version = [int]$verify.Parsed.version
                    }
                }
            }
        }

        if ($bootstrapProblems.Count -eq 0) {
            $detail = Invoke-F3Json -Client $client -Method 'GET' -Url "$adminBase/$facilityId" -Body $null -Token $adminToken
            $reviewBody = (@{ expectedVersion = [int]$detail.Parsed.version; action = 1; reason = $null } | ConvertTo-Json -Compress)
            $review = Invoke-F3Json -Client $client -Method 'POST' -Url "$adminBase/$facilityId/review" -Body $reviewBody -Token $adminToken
            if ($review.Status -ne 200 -or [int]$review.Parsed.status -ne 5) {
                $bootstrapProblems += "the first application could not be approved: status=$($review.Status) $(Get-SafeErrorSummary $review.Parsed)"
            }
        }
    }
    else {
        if ([int]$facilityState.status -ne 5 -or -not [string]::IsNullOrWhiteSpace([string]$facilityState.openRevisionId)) {
            $bootstrapProblems += "the existing fixture facility is not in the stable Approved state (status=$($facilityState.status), openRevisionId='$($facilityState.openRevisionId)'); a previous run did not finish, so this run stops before mutating it"
        }
        else {
            $facilityId = [string]$facilityState.facilityId
        }
    }

    Add-F3EvidenceCheck -Name 'fixture.facility-approved' `
        -Expected 'Exactly one dedicated facility exists and is either created to Approved through the real owner/admin flows or already stable' `
        -Passed ($bootstrapProblems.Count -eq 0) `
        -Evidence "source=$bootstrapSource facilityId=$facilityId problems=$($bootstrapProblems.Count)" `
        -Problems $bootstrapProblems

    if ($bootstrapProblems.Count -gt 0) {
        $script:failureNotes += 'The fixture facility could not be brought to a stable Approved state; the carry-forward checks were not run.'
    }
    else {
        # Re-read the stable state the checks start from.
        $facilityRead = Invoke-F3PsqlQuery -PsqlPath $psqlPath -Connection $connection -Sql (Get-F3FacilityStateQuery -OwnerEmail $definition.Owner.Email) -Tag 'facility'
        $facilityState = $facilityRead.Value

        # ---------------- Cycle A: phone-only edit, resave, stale, isolation, submit, approve.
        $mine = Get-F3OwnerProfile
        $assertOpenRevisionNull = ({
            param($status, $body)
            $actual = "status=$status"
            $problems = @()
            if ($null -eq $body) { $problems += 'response body is not valid JSON' }
            else {
                $actual = "status=$status facilityStatus=$($body.status) version=$($body.version) openRevisionId=$($body.openRevisionId)"
                if ([int]$body.status -ne 5) { $problems += "facility status is $($body.status), expected Approved (5)" }
                if ($null -ne $body.openRevisionId) { $problems += "openRevisionId is '$($body.openRevisionId)', expected null for an approved facility with no editable draft" }
                if ([string]::IsNullOrWhiteSpace([string]$body.approvedRevisionId)) { $problems += 'approvedRevisionId is missing' }
            }
            return @{ Actual = $actual; Problems = $problems }
        })
        Add-F3HttpCheck -Name 'read.open-revision-null' -Method 'GET' -Path '/api/v1/care-homes/facilities/mine' -Actor 'owner' `
            -Expected 'HTTP 200 whose openRevisionId is null while the facility is Approved' -ExpectedStatus 200 -Response $mine -Assert $assertOpenRevisionNull

        $baselineVersion = [int]$mine.Parsed.version
        $baselineDocumentCount = (Get-F3Array $mine.Parsed.documents).Count
        $baselineMediaCount = (Get-F3Array $mine.Parsed.profileMedia).Count
        $approvedRevisionId = [string]$mine.Parsed.approvedRevisionId

        $secondRead = Get-F3OwnerProfile
        $assertNoMutation = ({
            param($status, $body)
            $actual = "status=$status version=$($body.version)"
            $problems = @()
            if ($null -eq $body) { $problems += 'response body is not valid JSON' }
            else {
                $docs = @(Get-F3Array $body.documents).Count
                $media = @(Get-F3Array $body.profileMedia).Count
                $actual = "status=$status version=$($body.version) documents=$docs media=$media"
                if ([int]$body.version -ne $baselineVersion) { $problems += "a plain GET changed the version from $baselineVersion to $($body.version)" }
                if ($docs -ne $baselineDocumentCount) { $problems += "a plain GET changed the document count from $baselineDocumentCount to $docs" }
                if ($media -ne $baselineMediaCount) { $problems += "a plain GET changed the profile media count from $baselineMediaCount to $media" }
                if ([string]$body.approvedRevisionId -ne $approvedRevisionId) { $problems += 'a plain GET changed the approved revision' }
            }
            return @{ Actual = $actual; Problems = $problems }
        })
        Add-F3HttpCheck -Name 'read.get-creates-nothing' -Method 'GET' -Path '/api/v1/care-homes/facilities/mine' -Actor 'owner (second read)' `
            -Expected 'HTTP 200 with the same version, document count and media count as the previous GET (no revision or asset backfill)' -ExpectedStatus 200 -Response $secondRead -Assert $assertNoMutation

        # Phone-only edit of the approved facility.
        $phoneEditBody = (@{ expectedVersion = $baselineVersion; draft = (Get-F3ProfileDraft -Phone '+201000000101') } | ConvertTo-Json -Depth 6 -Compress)
        $edit = Invoke-F3Json -Client $client -Method 'PUT' -Url $saveProfileUrl -Body $phoneEditBody -Token $ownerToken
        $editRevisionId = ''
        if ($edit.Status -eq 200 -and $null -ne $edit.Parsed) { $editRevisionId = [string]$edit.Parsed.openRevisionId }
        $assertCarryForwardEdit = ({
            param($status, $body)
            $actual = "status=$status"
            $problems = @()
            if ($null -eq $body) { $problems += 'response body is not valid JSON' }
            else {
                $actual = "status=$status status=$($body.status) openRevisionId=$($body.openRevisionId) approvedRevisionId=$($body.approvedRevisionId)"
                if ([int]$body.status -ne 5) { $problems += "a phone-only edit changed the facility status to $($body.status), expected Approved (5) while editing" }
                if ([string]::IsNullOrWhiteSpace([string]$body.openRevisionId)) { $problems += 'a phone-only edit of an approved facility returned a null openRevisionId' }
                if ([string]$body.openRevisionId -eq $approvedRevisionId) { $problems += 'the open revision is the approved revision' }
                if ([string]$body.approvedRevisionId -ne $approvedRevisionId) { $problems += 'the approved revision changed during a phone-only edit' }
            }
            return @{ Actual = $actual; Problems = $problems }
        })
        Add-F3HttpCheck -Name 'edit.phone-only-creates-open-revision' -Method 'PUT' -Path '/api/v1/care-homes/facilities/mine/profile' -Actor 'owner' `
            -Expected 'HTTP 200 with a non-null openRevisionId distinct from the unchanged approvedRevisionId' -ExpectedStatus 200 -Response $edit -Assert $assertCarryForwardEdit

        # psql readback: inherited documents/media share the approved storage keys; approved revision unchanged.
        $stateAfterEdit = (Invoke-F3PsqlQuery -PsqlPath $psqlPath -Connection $connection -Sql (Get-F3FacilityStateQuery -OwnerEmail $definition.Owner.Email) -Tag 'after-edit').Value
        $carryForwardProblems = @()
        if ([string]$stateAfterEdit.openRevisionId -ne $editRevisionId) { $carryForwardProblems += 'the open revision read back from PostgreSQL does not match the API response' }
        $approvedDocSignatureBefore = Get-F3AssetSignature -Assets $facilityState.approvedDocuments -Kind 'document'
        $approvedMediaSignatureBefore = Get-F3AssetSignature -Assets $facilityState.approvedMedia -Kind 'media'
        if ((Get-F3AssetSignature -Assets $stateAfterEdit.approvedDocuments -Kind 'document') -ne $approvedDocSignatureBefore) {
            $carryForwardProblems += 'the approved revision documents changed during a phone-only edit'
        }
        if ((Get-F3AssetSignature -Assets $stateAfterEdit.approvedMedia -Kind 'media') -ne $approvedMediaSignatureBefore) {
            $carryForwardProblems += 'the approved revision media changed during a phone-only edit'
        }
        $openDocuments = Get-F3Array $stateAfterEdit.openDocuments
        $approvedDocuments = Get-F3Array $stateAfterEdit.approvedDocuments
        if ($openDocuments.Count -ne $approvedDocuments.Count) {
            $carryForwardProblems += "the new revision inherited $($openDocuments.Count) documents, expected $($approvedDocuments.Count)"
        }
        foreach ($approvedDocument in $approvedDocuments) {
            $match = @($openDocuments | Where-Object { [int]$_.type -eq [int]$approvedDocument.type })
            if ($match.Count -ne 1) {
                $carryForwardProblems += "the new revision did not inherit exactly one document of type $($approvedDocument.type)"
            }
            elseif ([string]$match[0].key -ne [string]$approvedDocument.key) {
                $carryForwardProblems += "the inherited document of type $($approvedDocument.type) points at a different private storage key"
            }
            elseif ([int]$match[0].status -ne [int]$approvedDocument.status) {
                $carryForwardProblems += "the inherited document of type $($approvedDocument.type) changed verification status"
            }
        }
        $openMedia = Get-F3Array $stateAfterEdit.openMedia
        $approvedMedia = Get-F3Array $stateAfterEdit.approvedMedia
        if ($openMedia.Count -ne $approvedMedia.Count) {
            $carryForwardProblems += "the new revision inherited $($openMedia.Count) media rows, expected $($approvedMedia.Count)"
        }
        foreach ($approvedImage in $approvedMedia) {
            $match = @($openMedia | Where-Object { [int]$_.kind -eq [int]$approvedImage.kind -and [int]$_.position -eq [int]$approvedImage.position })
            if ($match.Count -ne 1) {
                $carryForwardProblems += "the new revision did not inherit exactly one media row for kind $($approvedImage.kind) position $($approvedImage.position)"
            }
            elseif ([string]$match[0].key -ne [string]$approvedImage.key) {
                $carryForwardProblems += "the inherited media row for kind $($approvedImage.kind) position $($approvedImage.position) points at a different private storage key"
            }
        }
        Add-F3EvidenceCheck -Name 'edit.inherited-assets-share-approved-files' `
            -Expected 'The new revision inherits every document and media row referencing the same private storage keys with the same kind/position/status, while the approved revision is unchanged' `
            -Passed ($carryForwardProblems.Count -eq 0) `
            -Evidence "openRevisionId=$($stateAfterEdit.openRevisionId) openDocuments=$($openDocuments.Count) openMedia=$($openMedia.Count)" `
            -Problems $carryForwardProblems

        # Re-saving the same edit must not add duplicate inherited rows.
        $mineAfterEdit = Get-F3OwnerProfile
        $resaveBody = (@{ expectedVersion = [int]$mineAfterEdit.Parsed.version; draft = (Get-F3ProfileDraft -Phone '+201000000101') } | ConvertTo-Json -Depth 6 -Compress)
        $resave = Invoke-F3Json -Client $client -Method 'PUT' -Url $saveProfileUrl -Body $resaveBody -Token $ownerToken
        $resaveRevisionId = ''
        if ($resave.Status -eq 200 -and $null -ne $resave.Parsed) { $resaveRevisionId = [string]$resave.Parsed.openRevisionId }
        $stateAfterResave = (Invoke-F3PsqlQuery -PsqlPath $psqlPath -Connection $connection -Sql (Get-F3FacilityStateQuery -OwnerEmail $definition.Owner.Email) -Tag 'after-resave').Value
        $resaveProblems = @()
        if ($resave.Status -ne 200) { $resaveProblems += "re-saving the draft failed: status=$($resave.Status) $(Get-SafeErrorSummary $resave.Parsed)" }
        if ($resaveRevisionId -ne $editRevisionId) { $resaveProblems += 're-saving the same draft created a different open revision' }
        if ($resaveRevisionId -ne [string]$stateAfterResave.openRevisionId) { $resaveProblems += 'the open revision read back from PostgreSQL does not match the re-save response' }
        if ((Get-F3Array $stateAfterResave.openDocuments).Count -ne $openDocuments.Count) {
            $resaveProblems += "re-saving added inherited documents ($((Get-F3Array $stateAfterResave.openDocuments).Count) now, $($openDocuments.Count) before)"
        }
        if ((Get-F3Array $stateAfterResave.openMedia).Count -ne $openMedia.Count) {
            $resaveProblems += "re-saving added inherited media rows ($((Get-F3Array $stateAfterResave.openMedia).Count) now, $($openMedia.Count) before)"
        }
        if ((Get-F3AssetSignature -Assets $stateAfterResave.openDocuments -Kind 'document') -ne (Get-F3AssetSignature -Assets $stateAfterEdit.openDocuments -Kind 'document')) {
            $resaveProblems += 're-saving changed the inherited document set'
        }
        if ((Get-F3AssetSignature -Assets $stateAfterResave.openMedia -Kind 'media') -ne (Get-F3AssetSignature -Assets $stateAfterEdit.openMedia -Kind 'media')) {
            $resaveProblems += 're-saving changed the inherited media set'
        }
        Add-F3EvidenceCheck -Name 'edit.resave-adds-no-duplicate-inheritance' `
            -Expected 'Re-saving the same edit keeps the same open revision and adds no duplicate inherited document or media rows' `
            -Passed ($resaveProblems.Count -eq 0) `
            -Evidence "openRevisionId=$resaveRevisionId openDocuments=$((Get-F3Array $stateAfterResave.openDocuments).Count) openMedia=$((Get-F3Array $stateAfterResave.openMedia).Count)" `
            -Problems $resaveProblems

        # A stale expectedVersion is refused.
        $staleBody = (@{ expectedVersion = $baselineVersion; draft = (Get-F3ProfileDraft -Phone '+201000000102') } | ConvertTo-Json -Depth 6 -Compress)
        $stale = Invoke-F3Json -Client $client -Method 'PUT' -Url $saveProfileUrl -Body $staleBody -Token $ownerToken
        $assertStale = ({
            param($status, $body)
            $actual = "status=$status $(Get-SafeErrorSummary $body)"
            $problems = @()
            if ([string]$body.code -ne 'CareHomes.Facility.Conflict') { $problems += "expected code CareHomes.Facility.Conflict, got '$($body.code)'" }
            return @{ Actual = $actual; Problems = $problems }
        })
        Add-F3HttpCheck -Name 'edit.stale-version-refused' -Method 'PUT' -Path '/api/v1/care-homes/facilities/mine/profile' -Actor 'owner' `
            -Expected 'HTTP 409 with business code CareHomes.Facility.Conflict for a stale expectedVersion' -ExpectedStatus 409 -Response $stale -Assert $assertStale

        # Owner isolation: another owner cannot see this facility or its files.
        $otherOwnerRead = Invoke-F3Json -Client $client -Method 'GET' -Url $mineUrl -Body $null -Token $ownerSecondToken
        $assertOtherOwner = ({
            param($status, $body)
            $actual = "status=$status $(Get-SafeErrorSummary $body)"
            $problems = @()
            if ([string]$body.code -ne 'CareHomes.Facility.NotFound') { $problems += "expected code CareHomes.Facility.NotFound, got '$($body.code)'" }
            return @{ Actual = $actual; Problems = $problems }
        })
        Add-F3HttpCheck -Name 'isolation.other-owner-profile-not-found' -Method 'GET' -Path '/api/v1/care-homes/facilities/mine' -Actor 'owner-second' `
            -Expected 'HTTP 404 with business code CareHomes.Facility.NotFound because the facility belongs to another owner' -ExpectedStatus 404 -Response $otherOwnerRead -Assert $assertOtherOwner

        $approvedGalleryId = ''
        foreach ($image in $approvedMedia) {
            if ([int]$image.kind -eq 2) { $approvedGalleryId = [string]$image.id }
        }
        $otherOwnerFileUrl = "$mineUrl/media/$approvedGalleryId/file"
        $otherOwnerFile = Invoke-F3Json -Client $client -Method 'GET' -Url $otherOwnerFileUrl -Body $null -Token $ownerSecondToken
        Add-F3HttpCheck -Name 'isolation.other-owner-media-not-found' -Method 'GET' -Path "/api/v1/care-homes/facilities/mine/media/{approved-gallery-id}/file" -Actor 'owner-second' `
            -Expected 'HTTP 404 because another owner cannot read this facility''s private media file' -ExpectedStatus 404 -Response $otherOwnerFile

        # Submit the edited revision without re-uploading any asset.
        $mineBeforeSubmit = Get-F3OwnerProfile
        $submitBody = (@{ expectedVersion = [int]$mineBeforeSubmit.Parsed.version } | ConvertTo-Json -Compress)
        $submit = Invoke-F3Json -Client $client -Method 'POST' -Url $submitUrl -Body $submitBody -Token $ownerToken
        $assertSubmit = ({
            param($status, $body)
            $actual = "status=$status"
            $problems = @()
            if ($null -eq $body) { $problems += 'response body is not valid JSON' }
            else {
                $actual = "status=$status status=$($body.status) submittedRevisionId=$($body.submittedRevisionId)"
                if ([int]$body.status -ne 2) { $problems += "the submit result status is $($body.status), expected PendingReview (2)" }
                if ([string]$body.submittedRevisionId -ne $editRevisionId) { $problems += 'the submitted revision is not the inherited open revision' }
            }
            return @{ Actual = $actual; Problems = $problems }
        })
        Add-F3HttpCheck -Name 'submit.without-reuploading-assets' -Method 'POST' -Path '/api/v1/care-homes/facilities/mine/submit' -Actor 'owner' `
            -Expected 'HTTP 200 reaching PendingReview with the inherited revision submitted and no asset re-upload' -ExpectedStatus 200 -Response $submit -Assert $assertSubmit

        # Admin approves the carried-forward revision; inherited verification is reused.
        $submitVersion = 0
        if ($null -ne $submit.Parsed) { $submitVersion = [int]$submit.Parsed.version }
        $reviewBody = (@{ expectedVersion = $submitVersion; action = 1; reason = $null } | ConvertTo-Json -Compress)
        $review = Invoke-F3Json -Client $client -Method 'POST' -Url "$adminBase/$facilityId/review" -Body $reviewBody -Token $adminToken
        $assertReview = ({
            param($status, $body)
            $actual = "status=$status"
            $problems = @()
            if ($null -eq $body) { $problems += 'response body is not valid JSON' }
            else {
                $actual = "status=$status status=$($body.status) approvedRevisionId=$($body.approvedRevisionId)"
                if ([int]$body.status -ne 5) { $problems += "the review result status is $($body.status), expected Approved (5)" }
                if ([string]$body.approvedRevisionId -ne $editRevisionId) { $problems += 'the inherited revision was not approved' }
            }
            return @{ Actual = $actual; Problems = $problems }
        })
        Add-F3HttpCheck -Name 'review.approve-carried-forward-revision' -Method 'POST' -Path '/api/v1/admin/care-homes/{id}/review' -Actor 'admin' `
            -Expected 'HTTP 200 Approved with the inherited revision as the new approved revision (verification carried forward, no re-verify)' -ExpectedStatus 200 -Response $review -Assert $assertReview

        # ---------------- Cycle B: remove an inherited draft image, prove the approved file survives.
        $mineApproved = Get-F3OwnerProfile
        $approvedGalleryIdCurrent = ''
        foreach ($image in (Get-F3Array $mineApproved.Parsed.profileMedia)) {
            if ([int]$image.kind -eq 2 -and [string]$image.profileRevisionId -eq $editRevisionId) { $approvedGalleryIdCurrent = [string]$image.id }
        }

        $cycleBEditBody = (@{ expectedVersion = [int]$mineApproved.Parsed.version; draft = (Get-F3ProfileDraft -Phone '+201000000103') } | ConvertTo-Json -Depth 6 -Compress)
        $cycleBEdit = Invoke-F3Json -Client $client -Method 'PUT' -Url $saveProfileUrl -Body $cycleBEditBody -Token $ownerToken
        $cycleBRevisionId = ''
        if ($cycleBEdit.Status -eq 200 -and $null -ne $cycleBEdit.Parsed) { $cycleBRevisionId = [string]$cycleBEdit.Parsed.openRevisionId }

        $mineCycleB = Get-F3OwnerProfile
        $draftGalleryId = ''
        foreach ($image in (Get-F3Array $mineCycleB.Parsed.profileMedia)) {
            if ([int]$image.kind -eq 2 -and [string]$image.profileRevisionId -eq $cycleBRevisionId) { $draftGalleryId = [string]$image.id }
        }

        $removeStatus = -1
        $removeProblems = @()
        if ([string]::IsNullOrWhiteSpace($draftGalleryId)) {
            $removeProblems += 'no inherited draft gallery image was found to remove'
        }
        else {
            $removeUrl = $mineUrl + '/media/' + $draftGalleryId + '?expectedVersion=' + [string][int]$mineCycleB.Parsed.version
            $remove = Invoke-F3Json -Client $client -Method 'DELETE' -Url $removeUrl -Body $null -Token $ownerToken
            $removeStatus = [int]$remove.Status
            if ($remove.Status -ne 204) { $removeProblems += "removing the inherited draft image failed: status=$($remove.Status) $(Get-SafeErrorSummary $remove.Parsed)" }
        }
        Add-F3EvidenceCheck -Name 'media.remove-inherited-draft-image' `
            -Expected 'HTTP 204 removing only the editable draft media row' `
            -Passed ($removeProblems.Count -eq 0) `
            -Evidence "status=$removeStatus draftGalleryId=$draftGalleryId" `
            -Problems $removeProblems

        # The shared private file must survive: the approved revision still references it.
        $approvedFile = Invoke-F3Json -Client $client -Method 'GET' -Url "$mineUrl/media/$approvedGalleryIdCurrent/file" -Body $null -Token $ownerToken
        $approvedFileProblems = @()
        if ($approvedFile.Status -ne 200) { $approvedFileProblems += "the approved revision media file is no longer readable: status=$($approvedFile.Status)" }
        if ([string]::IsNullOrEmpty([string]$approvedFile.Body)) { $approvedFileProblems += 'the approved revision media file read back empty' }
        Add-F3EvidenceCheck -Name 'media.approved-file-survives-inherited-removal' `
            -Expected 'The approved revision media file is still readable after the inherited draft row is removed (no shared-file deletion)' `
            -Passed ($approvedFileProblems.Count -eq 0) `
            -Evidence "status=$($approvedFile.Status) bytes=$(([string]$approvedFile.Body).Length)" `
            -Problems $approvedFileProblems

        $mineAfterRemove = Get-F3OwnerProfile
        $resurrectProblems = @()
        $remainingDraftGalleries = @(Get-F3Array $mineAfterRemove.Parsed.profileMedia | Where-Object { [int]$_.kind -eq 2 -and [string]$_.profileRevisionId -eq $cycleBRevisionId })
        $stillPresent = @(Get-F3Array $mineAfterRemove.Parsed.profileMedia | Where-Object { [string]$_.id -eq $draftGalleryId })
        $approvedGalleryStillPresent = @(Get-F3Array $mineAfterRemove.Parsed.profileMedia | Where-Object { [string]$_.id -eq $approvedGalleryIdCurrent })
        if ($stillPresent.Count -ne 0) { $resurrectProblems += 'the removed draft media id is still returned by the read path' }
        if ($remainingDraftGalleries.Count -ne 0) { $resurrectProblems += "the read path resurrected $($remainingDraftGalleries.Count) gallery rows in the draft revision" }
        if ($approvedGalleryStillPresent.Count -ne 1) { $resurrectProblems += 'the approved revision gallery row disappeared' }
        Add-F3EvidenceCheck -Name 'media.read-does-not-resurrect-removed-asset' `
            -Expected 'The read path reports no removed draft media and no resurrected gallery row while the approved gallery row remains' `
            -Passed ($resurrectProblems.Count -eq 0) `
            -Evidence "draftGalleries=$($remainingDraftGalleries.Count) approvedGalleries=$($approvedGalleryStillPresent.Count)" `
            -Problems $resurrectProblems

        # A replacement uploaded through the normal flow returns the facility to Approved.
        $replacement = Invoke-F3Multipart -Client $client -Url $uploadMediaUrl `
            -Fields (@{ expectedVersion = [string][int]$mineAfterRemove.Parsed.version; kind = '2' }) `
            -File (@{ Name = 'file'; FileName = 'replacement.png'; ContentType = 'image/png'; Bytes = $png }) `
            -Token $ownerToken
        $replacementParsed = ConvertFrom-SafeJson $replacement.Body
        $replacementProblems = @()
        if ($replacement.Status -ne 200) { $replacementProblems += "the replacement upload failed: status=$($replacement.Status) $(Get-SafeErrorSummary $replacementParsed)" }
        elseif ([string]$replacementParsed.id -eq $draftGalleryId) { $replacementProblems += 'the replacement reused the removed draft media id' }
        elseif ([int]$replacementParsed.kind -ne 2) { $replacementProblems += 'the replacement is not a gallery image' }
        Add-F3EvidenceCheck -Name 'media.replacement-upload' `
            -Expected 'HTTP 200 uploading one replacement gallery image with a new media id' `
            -Passed ($replacementProblems.Count -eq 0) `
            -Evidence "status=$($replacement.Status) mediaId=$($replacementParsed.id)" `
            -Problems $replacementProblems

        $mineBeforeReplacementSubmit = Get-F3OwnerProfile
        $replacementSubmitBody = (@{ expectedVersion = [int]$mineBeforeReplacementSubmit.Parsed.version } | ConvertTo-Json -Compress)
        $replacementSubmit = Invoke-F3Json -Client $client -Method 'POST' -Url $submitUrl -Body $replacementSubmitBody -Token $ownerToken
        Add-F3HttpCheck -Name 'media.replacement-submit' -Method 'POST' -Path '/api/v1/care-homes/facilities/mine/submit' -Actor 'owner' `
            -Expected 'HTTP 200 reaching PendingReview for the revision that carries the replacement image' -ExpectedStatus 200 -Response $replacementSubmit

        $replacementSubmitVersion = 0
        if ($null -ne $replacementSubmit.Parsed) { $replacementSubmitVersion = [int]$replacementSubmit.Parsed.version }
        $replacementReviewBody = (@{ expectedVersion = $replacementSubmitVersion; action = 1; reason = $null } | ConvertTo-Json -Compress)
        $replacementReview = Invoke-F3Json -Client $client -Method 'POST' -Url "$adminBase/$facilityId/review" -Body $replacementReviewBody -Token $adminToken
        $assertReplacementReview = ({
            param($status, $body)
            $actual = "status=$status"
            $problems = @()
            if ($null -eq $body) { $problems += 'response body is not valid JSON' }
            else {
                $actual = "status=$status status=$($body.status) approvedRevisionId=$($body.approvedRevisionId)"
                if ([int]$body.status -ne 5) { $problems += "the review result status is $($body.status), expected Approved (5)" }
                if ([string]$body.approvedRevisionId -ne $cycleBRevisionId) { $problems += 'the replacement revision was not approved' }
            }
            return @{ Actual = $actual; Problems = $problems }
        })
        Add-F3HttpCheck -Name 'media.replacement-approve' -Method 'POST' -Path '/api/v1/admin/care-homes/{id}/review' -Actor 'admin' `
            -Expected 'HTTP 200 Approved with the replacement revision approved, restoring the stable cover-plus-one-gallery state' -ExpectedStatus 200 -Response $replacementReview -Assert $assertReplacementReview
    }
}
catch {
    $script:failureNotes += ('The verification could not complete: ' + (Get-TrimmedRedactedLine $_.Exception.Message))
}
finally {
    # 6. Log out every session this run created, then stop any API this script
    #    started so a failed run cannot leave an orphaned listener behind.
    if ($client -and $script:f3Tokens.Count -gt 0) {
        try {
            foreach ($check in @(New-F3LogoutChecks -Tokens $script:f3Tokens)) {
                $logoutResponse = Invoke-AhlenaHttpRequest -Client $client -Method $check.Method -Url "$baseUrl$($check.Path)" -Body $null -Token $check.Token
                Add-F3HttpCheck -Name $check.Name -Method $check.Method -Path $check.Path -Actor $check.Actor `
                    -Expected $check.Expected -ExpectedStatus $check.ExpectedStatus -Response $logoutResponse -Assert $check.Assert
            }
        }
        catch {
            $script:failureNotes += ('Session logout could not complete: ' + (Get-TrimmedRedactedLine $_.Exception.Message))
        }
    }

    if ($client) { $client.Dispose() }
    if ($script:apiProcess -and -not $script:apiProcess.HasExited) {
        Stop-Process -Id $script:apiProcess.Id -Force
        Wait-Process -Id $script:apiProcess.Id -Timeout 15 -ErrorAction SilentlyContinue
    }
}

# 7. Prove no fixture session was left behind (read-only).
if ($script:canRunChecks -and -not [string]::IsNullOrWhiteSpace($psqlPath) -and $null -ne $identityState) {
    $sessionRead = Invoke-F3PsqlQuery -PsqlPath $psqlPath -Connection $connection -Sql (Get-F3IdentityStateQuery) -Tag 'sessions'
    $sessionProblems = @()
    $activeSessionCount = -1
    if (-not $sessionRead.Ok) {
        $sessionProblems += ('The session readback failed: ' + $sessionRead.ErrorSummary)
    }
    else {
        $activeSessionCount = (Get-F3Array $sessionRead.Value.activeSessions).Count
        if ($activeSessionCount -ne 0) {
            $sessionProblems += "the run left $activeSessionCount live fixture session(s) behind"
        }
    }
    Add-F3EvidenceCheck -Name 'session.no-live-fixture-session' `
        -Expected 'No fixture identity has a live device session after the run logged out' `
        -Passed ($sessionProblems.Count -eq 0) `
        -Evidence "activeFixtureSessions=$activeSessionCount" `
        -Problems $sessionProblems
}

$failedCount = @($script:f3Checks | Where-Object { -not $_.passed }).Count
$overallPassed = ($script:failureNotes.Count -eq 0) -and ($failedCount -eq 0) -and $script:canRunChecks

$facilityFinal = $null
if ($script:canRunChecks -and -not [string]::IsNullOrWhiteSpace($psqlPath)) {
    $finalRead = Invoke-F3PsqlQuery -PsqlPath $psqlPath -Connection $connection -Sql (Get-F3FacilityStateQuery -OwnerEmail (Get-F3FixtureDefinition).Owner.Email) -Tag 'final'
    if ($finalRead.Ok) { $facilityFinal = $finalRead.Value }
}

Write-F3Result -Passed $overallPassed -Phase 'complete' -IdentityState $identityState -FacilityState $facilityFinal | ConvertTo-Json -Depth 8

if (-not $overallPassed) { exit 1 }
