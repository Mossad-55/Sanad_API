#requires -Version 5.1
<#
.SYNOPSIS
    E05 family-home CMS - localhost HTTP verification adapter over the L1 fixture
    library.

.DESCRIPTION
    A bounded adapter, not a second harness. It reuses the L1 library's helpers
    (repository root and pinned head, loopback target assertion, process
    environment overrides, API start/stop, HTTP request, outcome evaluation and
    trace correlation) and the F7 library's connection/psql guards, and adds only
    what E05 needs:

      1. The L1 read-only preflight report is re-checked fail-closed: all nine
         module contexts must target the loopback PersistentLocal Development
         database with no pending migration, no live Paymux/SMS/SMTP/Push input
         may be configured, and the resolved psql target must never be
         SanadBrunoTestDb. Nothing is written before that gate passes; when the
         phase migration is not applied locally the adapter refuses (exit 1) and
         reports the pending module rather than running a half-fixtured check set.
      2. INSERT-only, guarded, idempotent SQL through the local psql client adds
         five dedicated synthetic identities (ContentAdmin, a family owner, a
         member of that family, a Family account without a family, and a
         Family-role actor used for the Admin role denial), one synthetic family
         with one member row, one active and one inactive synthetic catalog
         service, three directly inserted published sections (a featured section
         over the active service, a featured section over the inactive service
         and a quick link whose target is that inactive service) and one draft
         banner whose stored media key is a private key. Nothing is updated,
         deleted or reset and every statement is guarded, so a rerun inserts
         nothing.
      3. Writes one guarded synthetic PNG under the verified public storage root
         (`landing/e05-adapter-verification.png`) so the media-key rule can be
         proved positively as well as negatively. An unrelated file already
         occupying that key fails the run closed instead of being overwritten or
         deleted.
      4. Starts exactly one Development API against the verified target on the
         E05 loopback port 5253 and drives the real routes: the eight Admin
         routes, the Family read, the role/authentication boundaries, paging and
         kind/status/search filters, the typed (non-JSON-string) item payload,
         the content and media-key rules, the draft-twin lifecycle, deletion
         semantics (a deleted draft twin appends its history row and never
         touches the published row; a published or archived row is a 409), the
         supersede-then-archive path, archived rows being immutable, version
         preconditions and the reorder rules.
      5. Two read-only psql readbacks prove the storage-level effects the HTTP
         status cannot show: the deleted twin is gone while exactly one Deleted
         history row for it remains, the superseded published row is archived in
         storage, and the pinned fixture rows and family are still present.
      6. Logs out every session this run created.

    Encoding: this file is ASCII only and needs no BOM. No Arabic literal is
    used: the one search predicate assertion runs over an ASCII token, and the
    Arabic-copy storage assertion that the relaxed JSON encoder exists for is
    covered by the focused unit tests instead (see the `unitOnly` block of the
    report).

    Safety properties:
      - The base URL must be a loopback HTTP(S) listener; there is no remote
        fallback and no way to pass one.
      - Live provider configuration is cleared for every process this script
        starts; migrations stay disabled
        (`Database:ApplyMigrationsOnStartup=false`).
      - Fixture SQL is INSERT-only, guarded and idempotent, and ASCII only.
      - The only rows this run adds are the ones the real HTTP routes create;
        nothing is pre-seeded to look like route output, and no row is ever
        updated or deleted outside a real route.
      - A persistent malformed-content row is deliberately NOT inserted: the
        Admin list read projects every row in its page, so such a row would break
        that read for every later run against the shared persistent database.
        That branch is proved by the focused unit tests and is declared in the
        `unitOnly` block.
      - Tokens, credentials, connection strings and payloads are never printed;
        a failed check reports only status, safe business code, field names,
        trace id and a redacted local log line.
      - Offline tests dot-source this file with -LibraryOnly and exercise only
        the pure helpers; no API, database or HTTP call occurs there.

.PARAMETER BaseUrl
    Loopback HTTP(S) base URL of the API. Defaults to the E05 port 5253, which
    does not collide with the other local adapters (5235/5243/5245/5247/5249/
    5251). Also used as ASPNETCORE_URLS when the script starts the API.

.PARAMETER ApiAssemblyPath
    Optional. Built API assembly. Defaults to the Release output.

.PARAMETER FixturePassword
    Synthetic fixture password whose hash is copied from the shared L1 Minimal
    fixture identity. Never a real credential and never emitted in output.

.PARAMETER LibraryOnly
    Returns after defining the helper functions so offline tests can dot-source
    them without any runtime work.
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:5253',

    [string]$ApiAssemblyPath,

    [string]$FixturePassword = 'Test-1234!',

    [switch]$LibraryOnly
)

$ErrorActionPreference = 'Stop'

# Capture this script's own parameters before dot-sourcing the libraries:
# dot-sourcing L1 and F7 binds their parameter names (BaseUrl, FixturePassword,
# ApiAssemblyPath, LibraryOnly, SkipApiStart, ...) in this scope and would
# otherwise overwrite the values passed to this script.
$e05LibraryOnly     = [bool]$LibraryOnly
$e05BaseUrl         = $BaseUrl
$e05ApiAssemblyPath = $ApiAssemblyPath
$e05FixturePassword = $FixturePassword

. (Join-Path $PSScriptRoot 'Invoke-LocalFixtureVerification.ps1') -LibraryOnly
. (Join-Path $PSScriptRoot 'Invoke-F7LocalHttpVerification.ps1') -LibraryOnly

# ---------------------------------------------------------------------------
# Routes, pinned literals and fixture definition
# ---------------------------------------------------------------------------

function Get-E05Route {
    <#
        The routes this adapter drives plus every literal the assertions compare.
        The literals are held here (not read back from the application) so the
        HTTP evidence stands on its own.
    #>
    [CmdletBinding()]
    param()

    return [ordered]@{
        AdminList = '/api/v1/admin/family-home/sections'
        AdminReorder = '/api/v1/admin/family-home/sections/reorder'
        FamilyList = '/api/v1/family/home/sections'
        Logout = '/api/v1/auth/sessions/logout'

        Codes = [ordered]@{
            SectionInvalid = 'Cms.FamilyHome.SectionInvalid'
            InvalidKind = 'Cms.FamilyHome.InvalidKind'
            Incomplete = 'Cms.FamilyHome.Incomplete'
            ReorderInvalid = 'Cms.FamilyHome.ReorderInvalid'
            SectionNotFound = 'Cms.FamilyHome.SectionNotFound'
            StaleVersion = 'Cms.FamilyHome.StaleVersion'
            PublishedHistoryConflict = 'Cms.FamilyHome.PublishedHistoryConflict'
            ImmutableHistory = 'Cms.FamilyHome.ImmutableHistory'
            FamilyNotFound = 'Families.Family.NotFound'
            ValidationFailed = 'Api.Validation.Failed'
        }

        Kinds = [ordered]@{
            Banners = 'banners'
            QuickLinks = 'quick-links'
            FeaturedServices = 'featured-services'
        }

        Statuses = [ordered]@{
            Draft = 'Draft'
            Published = 'Published'
            Archived = 'Archived'
        }

        ServicesTarget = 'services'
        ServiceTarget = 'service'

        SearchToken = 'e05searchtoken'
        QuickLinkArabicTitle = 'e05searchtoken pinned link'
        QuickLinkEnglishTitle = 'e05searchtoken pinned link'
        CreatedLinkArabicTitle = 'E05 verification created link'
        CreatedLinkEnglishTitle = 'E05 verification created link'
        UpdatedLinkArabicTitle = 'E05 verification updated link'
        UpdatedLinkEnglishTitle = 'E05 verification updated link'
        FinalLinkArabicTitle = 'E05 verification final link'
        FinalLinkEnglishTitle = 'E05 verification final link'
        BannerArabicTitle = 'E05 verification banner'
        BannerEnglishTitle = 'E05 verification banner'
        InactiveTargetArabicTitle = 'E05 verification inactive target link'
        InactiveTargetEnglishTitle = 'E05 verification inactive target link'

        PublicMediaKey = 'landing/e05-adapter-verification.png'
        MissingMediaKeySuffix = 'landing/e05-adapter-absent.png'
        PrivateMediaKey = 'private/e05-adapter-verification.png'
        TraversalMediaKey = '../e05-adapter-verification.png'
        PrivateStoredMediaKey = 'private/e05-adapter-stored.png'
    }
}

function Get-E05FixtureDefinition {
    <#
        Five dedicated synthetic identities plus the fixed references they need.

        Account types (Identity AccountType): Family = 1, ContentAdmin = 6.
        UserStatus Active = 2. FamilyRole: Owner = 1, Viewer = 3.
        FamilyRelationshipType Other = 99. CaregiverType: Medical = 1,
        Companion = 2. FamilyHomeSectionKind: Banners = 1, QuickLinks = 2,
        FeaturedServices = 3. FamilyHomeSectionStatus: Draft = 1,
        Published = 2, Archived = 3. LandingHistoryAction Deleted = 3.

        The password hash is copied from the shared L1 Minimal fixture identity,
        so no credential is ever stored in this repository; the guard fails
        closed when that identity (and therefore the L1 Minimal seed) is absent.

        The phone numbers are the free E05 range +201000000091..095.
    #>
    [CmdletBinding()]
    param()

    $routes = Get-E05Route

    return [ordered]@{
        PasswordSource = 'content.admin@test.sanad.local'
        Family = [ordered]@{
            Id = '019a2e05-0003-7000-8000-000000000001'
            Name = 'E05 synthetic family'
        }
        Services = [ordered]@{
            Active = [ordered]@{
                Id = '019a2e05-0007-7000-8000-000000000001'
                ArabicName = 'E05 verification active service'
                EnglishName = 'E05 verification active service'
                IconPath = 'icons/e05-adapter-active.png'
                CaregiverType = 1
                IsActive = $true
            }
            Inactive = [ordered]@{
                Id = '019a2e05-0007-7000-8000-000000000002'
                ArabicName = 'E05 verification inactive service'
                EnglishName = 'E05 verification inactive service'
                IconPath = 'icons/e05-adapter-inactive.png'
                CaregiverType = 2
                IsActive = $false
            }
        }
        PinnedSections = [ordered]@{
            # Published featured section over the active service: the live-catalog
            # proof and the "section present" anchor of the Family reads.
            ActiveFeatured = [ordered]@{
                Id = '019a2e05-0008-7000-8000-000000000001'
                Kind = 3
                Status = 2
                Version = 2
                DisplayOrder = 941
                Sources = 'active'
            }
            # Published featured section over the inactive service: publish-time
            # activeness was true when this row was written, so the Family read
            # must omit the item and therefore the section.
            InactiveFeatured = [ordered]@{
                Id = '019a2e05-0008-7000-8000-000000000002'
                Kind = 3
                Status = 2
                Version = 2
                DisplayOrder = 942
                Sources = 'inactive'
            }
            # Published quick link whose 'service' target is that inactive service:
            # the action-bearing item is omitted, so the section is omitted too.
            InactiveTargetLink = [ordered]@{
                Id = '019a2e05-0008-7000-8000-000000000003'
                Kind = 2
                Status = 2
                Version = 2
                DisplayOrder = 943
                Sources = 'inactive'
            }
            # Draft banner whose stored media key is a private key: publication
            # must refuse the stored payload, not just the request.
            PrivateStoredKeyBanner = [ordered]@{
                Id = '019a2e05-0008-7000-8000-000000000004'
                Kind = 1
                Status = 1
                Version = 1
                DisplayOrder = 944
                Sources = 'private'
            }
        }
        Actors = [ordered]@{
            'content-admin' = [ordered]@{
                Email = 'content.admin.e05@test.sanad.local'
                UserId = '019a2e05-0001-7000-8000-000000000001'
                AccountId = '019a2e05-0002-7000-8000-000000000001'
                PhoneNumber = '+201000000091'
                FullName = 'E05 Content Admin'
                AccountType = 6
                DeviceName = 'e05-content-admin'
            }
            'family-owner' = [ordered]@{
                Email = 'family.owner.e05@test.sanad.local'
                UserId = '019a2e05-0001-7000-8000-000000000002'
                AccountId = '019a2e05-0002-7000-8000-000000000002'
                PhoneNumber = '+201000000092'
                FullName = 'E05 Family Owner'
                AccountType = 1
                DeviceName = 'e05-family-owner'
            }
            'family-viewer' = [ordered]@{
                Email = 'family.member.e05@test.sanad.local'
                UserId = '019a2e05-0001-7000-8000-000000000003'
                AccountId = '019a2e05-0002-7000-8000-000000000003'
                PhoneNumber = '+201000000093'
                FullName = 'E05 Family Member'
                AccountType = 1
                DeviceName = 'e05-family-member'
            }
            'family-none' = [ordered]@{
                Email = 'family.none.e05@test.sanad.local'
                UserId = '019a2e05-0001-7000-8000-000000000004'
                AccountId = '019a2e05-0002-7000-8000-000000000004'
                PhoneNumber = '+201000000094'
                FullName = 'E05 Family Without Family'
                AccountType = 1
                DeviceName = 'e05-family-without-family'
            }
            'family-denied' = [ordered]@{
                Email = 'denied.e05@test.sanad.local'
                UserId = '019a2e05-0001-7000-8000-000000000005'
                AccountId = '019a2e05-0002-7000-8000-000000000005'
                PhoneNumber = '+201000000095'
                FullName = 'E05 Family Role Denied'
                AccountType = 1
                DeviceName = 'e05-family-role-denied'
            }
        }
        Literals = $routes
    }
}

function Get-E05FixtureSql {
    <#
        Idempotent, INSERT-only fixture provisioning. Every statement is guarded:
        a rerun inserts nothing, and nothing is ever updated, deleted or reset.
        The shared fixture password hash is copied, never invented, and the
        provisioning fails closed when it is missing.

        The three published fixture sections and the draft private-key banner are
        inserted directly because a published row referencing a service that is
        already inactive, or a stored media key that is already private, cannot be
        produced through the public routes (publish refuses both).
    #>
    [CmdletBinding()]
    param()

    $definition = Get-E05FixtureDefinition
    $source = $definition.PasswordSource
    $family = $definition.Family
    $active = $definition.Services.Active
    $inactive = $definition.Services.Inactive
    $sections = $definition.PinnedSections
    $link = $definition.Literals.QuickLinkArabicTitle
    $linkEnglish = $definition.Literals.QuickLinkEnglishTitle

    $identityValues = @()
    $accountValues = @()
    foreach ($actor in $definition.Actors.Keys) {
        $item = $definition.Actors[$actor]
        $identityValues += ("('{0}', '{1}', '{2}', '{3}')" -f $item.UserId, $item.FullName, $item.PhoneNumber, $item.Email)
        $accountValues += ("('{0}', '{1}', {2})" -f $item.AccountId, $item.Email, $item.AccountType)
    }

    # The published quick link over the inactive service is also the pinned
    # search anchor: its stored copy carries the ASCII search token, so the Admin
    # search predicate can only return it by reading the stored content. Its
    # action is omitted by the Family read because its target service is
    # inactive, which is the omission proof at the same time.
    $publishedItems = @{
        $sections.ActiveFeatured.Id = ('[{{"serviceId":"{0}"}}]' -f $active.Id)
        $sections.InactiveFeatured.Id = ('[{{"serviceId":"{0}"}}]' -f $inactive.Id)
        $sections.InactiveTargetLink.Id = ('[{{"arabicTitle":"{0}","englishTitle":"{1}","target":{{"type":"service","serviceId":"{2}"}}}}]' -f `
            $link, $linkEnglish, $inactive.Id)
    }
    $privateBannerItems = ('[{{"arabicTitle":"{0}","englishTitle":"{1}","imageKey":"{2}"}}]' -f `
        $definition.Literals.BannerArabicTitle, $definition.Literals.BannerEnglishTitle, $definition.Literals.PrivateStoredMediaKey)

    $sectionValues = @()
    foreach ($key in @('ActiveFeatured', 'InactiveFeatured', 'InactiveTargetLink')) {
        $section = $sections[$key]
        $sectionValues += ("('{0}', {1}, '{2}', {3}, true, {4}, {5})" -f `
            $section.Id, $section.Kind, $publishedItems[$section.Id].Replace("'", "''"), $section.DisplayOrder, $section.Status, $section.Version)
    }
    $private = $sections.PrivateStoredKeyBanner
    $sectionValues += ("('{0}', {1}, '{2}', {3}, true, {4}, {5})" -f `
        $private.Id, $private.Kind, $privateBannerItems.Replace("'", "''"), $private.DisplayOrder, $private.Status, $private.Version)

    return @"
\set ON_ERROR_STOP on
BEGIN;

-- Fail closed when the shared L1 fixture password hash is missing: a fixture
-- identity without a usable hash could never authenticate, and every later
-- check would fail for the wrong reason.
DO `$e05guard`$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM identity.users
        WHERE email = '$source' AND password_hash IS NOT NULL)
    THEN
        RAISE EXCEPTION 'E05 fixture guard: the shared fixture password hash is missing';
    END IF;
END
`$e05guard`$;

INSERT INTO identity.users
    (id, arabic_full_name, english_full_name, phone_number, email,
     email_verified, phone_verified, status, ui_language, created_on_utc, updated_on_utc, password_hash)
SELECT f.id::uuid, f.full_name, f.full_name, f.phone, f.email, true, true, 2, 2, now(), now(), source.password_hash
FROM (VALUES
    $($identityValues -join ",`n    ")
) AS f(id, full_name, phone, email)
CROSS JOIN identity.users AS source
WHERE source.email = '$source'
  AND NOT EXISTS (SELECT 1 FROM identity.users AS existing WHERE existing.id = f.id::uuid)
  AND NOT EXISTS (SELECT 1 FROM identity.users AS existing WHERE existing.email = f.email);

INSERT INTO identity.user_accounts (id, user_id, account_type, created_on_utc)
SELECT f.id::uuid, u.id, f.account_type, now()
FROM (VALUES
    $($accountValues -join ",`n    ")
) AS f(id, email, account_type)
JOIN identity.users AS u ON u.email = f.email
WHERE NOT EXISTS (SELECT 1 FROM identity.user_accounts AS existing WHERE existing.id = f.id::uuid)
  AND NOT EXISTS (
      SELECT 1 FROM identity.user_accounts AS existing
      WHERE existing.user_id = u.id AND existing.account_type = f.account_type);

INSERT INTO families.families (id, owner_user_id, name, created_on_utc, updated_on_utc)
SELECT '$($family.Id)', owner.id, '$($family.Name)', now(), now()
FROM identity.users AS owner
WHERE owner.email = 'family.owner.e05@test.sanad.local'
  AND NOT EXISTS (SELECT 1 FROM families.families AS existing WHERE existing.id = '$($family.Id)');

INSERT INTO families.family_members (family_id, user_id, added_by_user_id, joined_on_utc, relationship_type, role)
SELECT '$($family.Id)', member.id, owner.id, now(), 99, 3
FROM identity.users AS member
JOIN identity.users AS owner ON owner.email = 'family.owner.e05@test.sanad.local'
WHERE member.email = 'family.member.e05@test.sanad.local'
  AND NOT EXISTS (
      SELECT 1 FROM families.family_members AS existing
      WHERE existing.family_id = '$($family.Id)' AND existing.user_id = member.id);

INSERT INTO caregivers.services
    (id, arabic_name, english_name, caregiver_type, icon_path, is_active, created_on_utc, updated_on_utc)
SELECT f.id::uuid, f.arabic_name, f.english_name, f.caregiver_type, f.icon_path, f.is_active, now(), now()
FROM (VALUES
    ('$($active.Id)', '$($active.ArabicName)', '$($active.EnglishName)', $($active.CaregiverType), '$($active.IconPath)', true),
    ('$($inactive.Id)', '$($inactive.ArabicName)', '$($inactive.EnglishName)', $($inactive.CaregiverType), '$($inactive.IconPath)', false)
) AS f(id, arabic_name, english_name, caregiver_type, icon_path, is_active)
WHERE NOT EXISTS (SELECT 1 FROM caregivers.services AS existing WHERE existing.id = f.id::uuid);

INSERT INTO cms.family_home_sections
    (id, kind, items_json, display_order, is_visible, source_id, status, version,
     created_on_utc, updated_on_utc, published_on_utc)
SELECT f.id::uuid, f.kind, f.items_json, f.display_order, f.is_visible, NULL, f.status, f.version,
    now(), now(), CASE WHEN f.status = 2 THEN now() ELSE NULL END
FROM (VALUES
    $($sectionValues -join ",`n    ")
) AS f(id, kind, items_json, display_order, is_visible, status, version)
WHERE NOT EXISTS (SELECT 1 FROM cms.family_home_sections AS existing WHERE existing.id = f.id::uuid);

COMMIT;
"@
}

function Get-E05StateQuery {
    <#
        Read-only readback of the fixtures, so the HTTP proof starts from verified
        ids, account types, activeness flags and stored section rows instead of
        assumptions. Every value is a count, a flag or a pinned public string.
    #>
    [CmdletBinding()]
    param()

    $definition = Get-E05FixtureDefinition
    $source = $definition.PasswordSource
    $family = $definition.Family
    $active = $definition.Services.Active
    $inactive = $definition.Services.Inactive
    $sections = $definition.PinnedSections

    $userIds = @()
    $accountIds = @()
    foreach ($actor in $definition.Actors.Keys) {
        $userIds += "'" + $definition.Actors[$actor].UserId + "'"
        $accountIds += "'" + $definition.Actors[$actor].AccountId + "'"
    }

    $sectionIds = @(
        "'" + $sections.ActiveFeatured.Id + "'"
        "'" + $sections.InactiveFeatured.Id + "'"
        "'" + $sections.InactiveTargetLink.Id + "'"
        "'" + $sections.PrivateStoredKeyBanner.Id + "'"
    )

    return @"
SELECT json_build_object(
    'passwordSourcePresent', (SELECT count(*) FROM identity.users WHERE email = '$source' AND password_hash IS NOT NULL),
    'identities', (SELECT count(*) FROM identity.users WHERE id IN ($($userIds -join ', '))),
    'accounts', (SELECT count(*) FROM identity.user_accounts WHERE id IN ($($accountIds -join ', '))),
    'contentAdminAccountType', (SELECT account.account_type FROM identity.user_accounts AS account JOIN identity.users AS u ON u.id = account.user_id WHERE u.email = 'content.admin.e05@test.sanad.local'),
    'familyRows', (SELECT count(*) FROM families.families WHERE id = '$($family.Id)'),
    'familyMemberRows', (SELECT count(*) FROM families.family_members WHERE family_id = '$($family.Id)' AND role = 3),
    'serviceRows', (SELECT count(*) FROM caregivers.services WHERE id IN ('$($active.Id)', '$($inactive.Id)')),
    'activeServiceIsActive', (SELECT is_active FROM caregivers.services WHERE id = '$($active.Id)'),
    'activeServiceNameEnglish', (SELECT english_name FROM caregivers.services WHERE id = '$($active.Id)'),
    'activeServiceNameArabic', (SELECT arabic_name FROM caregivers.services WHERE id = '$($active.Id)'),
    'activeServiceIconPath', (SELECT icon_path FROM caregivers.services WHERE id = '$($active.Id)'),
    'activeServiceCaregiverType', (SELECT caregiver_type FROM caregivers.services WHERE id = '$($active.Id)'),
    'inactiveServiceIsActive', (SELECT is_active FROM caregivers.services WHERE id = '$($inactive.Id)'),
    'pinnedSectionRows', (SELECT count(*) FROM cms.family_home_sections WHERE id IN ($($sectionIds -join ', '))),
    'familyHomeTables', (SELECT count(*) FROM information_schema.tables WHERE table_schema = 'cms' AND table_name IN ('family_home_sections', 'family_home_section_history'))
);
"@
}

# ---------------------------------------------------------------------------
# Assert helpers (return closures the L1 evaluator can invoke)
# ---------------------------------------------------------------------------

# Note on assertion scope: a check assertion is invoked by the L1
# Get-CheckOutcome in the script scope, so an assertion that is NOT wrapped in
# .GetNewClosure() can call the script-scope helpers. An assertion that IS
# wrapped needs .GetNewClosure() to bind the values captured while the check plan
# was built, and a closure resolves functions from the global scope only - it
# cannot see this script's functions. Every assertion that binds values
# therefore inlines its own shape checks (and uses only .NET types) instead of
# calling a helper.

function New-E05StatusAssert {
    <# Status only: authentication and authorization failures carry nothing else. #>
    [CmdletBinding()]
    param()

    return {
        param($status, $body)
        return @{ Actual = "status=$status"; Problems = @() }
    }
}

function New-E05CodeAssert {
    <# One expected HTTP status plus the pinned business code and its trace id. #>
    [CmdletBinding()]
    param(
        [int]$ExpectedStatus,
        [string]$ExpectedCode
    )

    return ({
        param($status, $body)
        $code = '<no-json-body>'
        $traceId = ''
        if ($null -ne $body -and @($body.PSObject.Properties.Name) -contains 'code') { $code = [string]$body.code }
        if ($null -ne $body -and @($body.PSObject.Properties.Name) -contains 'traceId') { $traceId = [string]$body.traceId }

        $actual = "status=$status code=$code traceIdPresent=$(-not [string]::IsNullOrWhiteSpace($traceId))"
        $problems = @()
        if ($null -eq $body) { $problems += 'response body is not valid JSON' }
        if ($status -ne $ExpectedStatus) { $problems += "expected HTTP $ExpectedStatus, got $status" }
        if ($code -ne $ExpectedCode) { $problems += "expected business code $ExpectedCode, got '$code'" }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()
}

function New-E05ValidationAssert {
    <#
        A FluentValidation pipeline rejection: HTTP 400, the shared
        Api.Validation.Failed code, and a field-level error dictionary naming the
        offending member.
    #>
    [CmdletBinding()]
    param([string]$ExpectedField)

    return ({
        param($status, $body)
        $code = '<no-json-body>'
        $fields = @()
        if ($null -ne $body -and @($body.PSObject.Properties.Name) -contains 'code') { $code = [string]$body.code }
        if ($null -ne $body -and @($body.PSObject.Properties.Name) -contains 'errors' -and $null -ne $body.errors) {
            $fields = @($body.errors.PSObject.Properties.Name)
        }

        $actual = "status=$status code=$code fields=$($fields -join ',')"
        $problems = @()
        if ($null -eq $body) { $problems += 'response body is not valid JSON' }
        if ($status -ne 400) { $problems += "expected HTTP 400, got $status" }
        if ($code -ne 'Api.Validation.Failed') { $problems += "expected the shared validation code Api.Validation.Failed, got '$code'" }
        if ($fields -notcontains $ExpectedField) { $problems += "expected a field-level error for $ExpectedField" }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()
}

function New-E05PagedListAssert {
    <#
        The Admin page shape plus the clamped paging values and, when a filter is
        asserted, one property of every returned item.
    #>
    [CmdletBinding()]
    param(
        [int]$ExpectedPage,
        [int]$ExpectedPageSize,
        [string]$ItemProperty = '',
        [string]$ExpectedItemValue = ''
    )

    return ({
        param($status, $body)
        $problems = @()
        if ($null -eq $body) {
            return @{ Actual = "status=$status"; Problems = @('response body is not valid JSON') }
        }

        $names = @($body.PSObject.Properties.Name)
        $items = @()
        if ($names -contains 'items' -and $null -ne $body.items) { $items = @($body.items) }

        $observedPage = -1
        $observedPageSize = -1
        $observedTotal = -1
        if ($names -contains 'page') { $observedPage = [int]$body.page }
        if ($names -contains 'pageSize') { $observedPageSize = [int]$body.pageSize }
        if ($names -contains 'totalCount') { $observedTotal = [int]$body.totalCount }

        $mismatched = @()
        if (-not [string]::IsNullOrWhiteSpace($ItemProperty)) {
            foreach ($item in $items) {
                $value = [string]$item.$ItemProperty
                if ($value -ne $ExpectedItemValue) { $mismatched += $value }
            }
        }

        $actual = "status=$status page=$observedPage pageSize=$observedPageSize totalCount=$observedTotal items=$($items.Count)"
        if (-not [string]::IsNullOrWhiteSpace($ItemProperty)) {
            $actual += " mismatched$ItemProperty=$($mismatched.Count)"
        }

        foreach ($member in @('items', 'page', 'pageSize', 'totalCount')) {
            if ($names -notcontains $member) { $problems += "the page is missing $member" }
        }
        if ($observedPage -ne $ExpectedPage) { $problems += "expected page $ExpectedPage, got $observedPage" }
        if ($observedPageSize -ne $ExpectedPageSize) { $problems += "expected pageSize $ExpectedPageSize, got $observedPageSize" }
        if ($observedTotal -lt 0) { $problems += 'totalCount is not a non-negative number' }
        if ($items.Count -gt $ExpectedPageSize) { $problems += "page holds $($items.Count) items, more than pageSize $ExpectedPageSize" }
        if ($items.Count -lt $ExpectedPageSize -and $items.Count -ne $observedTotal) {
            $problems += "a short page must still report totalCount=$($items.Count), got $observedTotal"
        }
        if ($mismatched.Count -gt 0) { $problems += "the $ItemProperty filter returned $($mismatched.Count) item(s) whose $ItemProperty is not '$ExpectedItemValue'" }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()
}

function New-E05SearchAssert {
    <#
        The Admin search filter over the stored typed payload: the pinned
        token-bearing section must come back and every returned section's typed
        items must carry the token, so a row whose stored copy does not contain it
        cannot have matched.
    #>
    [CmdletBinding()]
    param(
        [string]$PinnedId,
        [string]$Token
    )

    return ({
        param($status, $body)
        $problems = @()
        if ($null -eq $body) {
            return @{ Actual = "status=$status"; Problems = @('response body is not valid JSON') }
        }

        $names = @($body.PSObject.Properties.Name)
        $items = @()
        if ($names -contains 'items' -and $null -ne $body.items) { $items = @($body.items) }

        $ids = @($items | ForEach-Object { [string]$_.id })
        $withoutToken = 0
        foreach ($item in $items) {
            $serialized = ($item.items | ConvertTo-Json -Depth 5 -Compress)
            if ($null -eq $serialized -or -not $serialized.Contains($Token)) { $withoutToken++ }
        }

        $totalCount = -1
        if ($names -contains 'totalCount') { $totalCount = [int]$body.totalCount }

        $actual = "status=$status totalCount=$totalCount items=$($items.Count) pinnedPresent=$($ids -contains $PinnedId) itemsWithoutToken=$withoutToken"
        $problems = @()
        if ($ids -notcontains $PinnedId) { $problems += "the search did not return the pinned token-bearing section $PinnedId" }
        if ($items.Count -eq 0) { $problems += 'the search returned no section at all' }
        if ($withoutToken -gt 0) { $problems += "$withoutToken returned section(s) whose typed items do not carry the search token" }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()
}

function New-E05SectionAssert {
    <#
        The Admin section shape: a uuid id, the pinned kind/status, the version
        and twin precondition, the typed item array (never a JSON string and never
        a raw itemsJson member) and the publication state.
    #>
    [CmdletBinding()]
    param(
        [string]$ExpectedKind = '',
        [string]$ExpectedStatus = '',
        [int]$ExpectedVersion = -1,
        [string]$ExpectedSourceId = '-',
        [int]$ExpectedItemCount = -1,
        [string]$ExpectedItemServiceId = '',
        [string]$ExpectedItemTargetType = '',
        [string]$ExpectedPublishedOnUtc = 'any'
    )

    return ({
        param($status, $body)
        $problems = @()
        if ($null -eq $body) {
            return @{ Actual = "status=$status"; Problems = @('response body is not valid JSON') }
        }

        $names = @($body.PSObject.Properties.Name)
        $id = ''
        $idIsUuid = $false
        if ($names -contains 'id') {
            $id = [string]$body.id
            $parsedId = [Guid]::Empty
            $idIsUuid = [Guid]::TryParse($id, [ref]$parsedId)
        }

        $kind = [string]$body.kind
        $sectionStatus = [string]$body.status
        $version = -1
        if ($names -contains 'version') { $version = [int]$body.version }
        $displayOrder = -1
        if ($names -contains 'displayOrder') { $displayOrder = [int]$body.displayOrder }

        $sourceId = ''
        if ($names -contains 'sourceId' -and $null -ne $body.sourceId) { $sourceId = [string]$body.sourceId }

        $typed = @()
        $itemsAreTyped = $false
        if ($names -contains 'items') {
            $rawItems = $body.items
            if ($null -eq $rawItems) {
                $itemsAreTyped = $true
            }
            elseif ($rawItems -is [string]) {
                $itemsAreTyped = $false
            }
            else {
                $typed = @($rawItems)
                $itemsAreTyped = $true
                foreach ($candidate in $typed) {
                    if ($candidate -is [string]) { $itemsAreTyped = $false }
                }
            }
        }

        $itemServiceId = ''
        $itemTargetType = ''
        if ($typed.Count -gt 0) {
            if ($typed[0].PSObject.Properties.Name -contains 'serviceId' -and $null -ne $typed[0].serviceId) { $itemServiceId = [string]$typed[0].serviceId }
            if ($typed[0].PSObject.Properties.Name -contains 'target' -and $null -ne $typed[0].target) { $itemTargetType = [string]$typed[0].target.type }
        }

        $publishedOn = ''
        if ($names -contains 'publishedOnUtc' -and $null -ne $body.publishedOnUtc) { $publishedOn = [string]$body.publishedOnUtc }

        $actual = "status=$status kind=$kind sectionStatus=$sectionStatus version=$version displayOrder=$displayOrder items=$($typed.Count) itemsAreTyped=$itemsAreTyped itemServiceId=$itemServiceId itemTargetType=$itemTargetType publishedOnUtcPresent=$(-not [string]::IsNullOrWhiteSpace($publishedOn))"
        if ($ExpectedSourceId -ne '-') { $actual += " sourceIdPresent=$(-not [string]::IsNullOrWhiteSpace($sourceId))" }

        foreach ($member in @('id', 'kind', 'version', 'status', 'displayOrder', 'isVisible', 'sourceId', 'items', 'createdOnUtc', 'updatedOnUtc', 'publishedOnUtc')) {
            if ($names -notcontains $member) { $problems += "the section response is missing $member" }
        }
        if (-not $idIsUuid) { $problems += 'the section id is not a uuid' }
        if (-not $itemsAreTyped) { $problems += 'items is a JSON string (or holds one) instead of a typed array of objects' }
        if ($names -contains 'itemsJson') { $problems += 'the response exposes a raw itemsJson member' }
        if (-not [string]::IsNullOrWhiteSpace($ExpectedKind) -and $kind -ne $ExpectedKind) { $problems += "expected kind '$ExpectedKind', got '$kind'" }
        if (-not [string]::IsNullOrWhiteSpace($ExpectedStatus) -and $sectionStatus -ne $ExpectedStatus) { $problems += "expected status '$ExpectedStatus', got '$sectionStatus'" }
        if ($ExpectedVersion -ge 0 -and $version -ne $ExpectedVersion) { $problems += "expected version $ExpectedVersion, got $version" }
        if ($ExpectedItemCount -ge 0 -and $typed.Count -ne $ExpectedItemCount) { $problems += "expected $ExpectedItemCount item(s), got $($typed.Count)" }
        if (-not [string]::IsNullOrWhiteSpace($ExpectedItemServiceId) -and $itemServiceId -ne $ExpectedItemServiceId) { $problems += "expected item serviceId $ExpectedItemServiceId, got '$itemServiceId'" }
        if (-not [string]::IsNullOrWhiteSpace($ExpectedItemTargetType) -and $itemTargetType -ne $ExpectedItemTargetType) { $problems += "expected item target type '$ExpectedItemTargetType', got '$itemTargetType'" }

        if ($ExpectedSourceId -eq 'none' -and -not [string]::IsNullOrWhiteSpace($sourceId)) { $problems += "expected no twin sourceId, got '$sourceId'" }
        if ($ExpectedSourceId -ne '-' -and $ExpectedSourceId -ne 'none' -and $sourceId -ne $ExpectedSourceId) { $problems += "expected twin sourceId $ExpectedSourceId, got '$sourceId'" }

        if ($ExpectedPublishedOnUtc -eq 'present' -and [string]::IsNullOrWhiteSpace($publishedOn)) { $problems += 'publishedOnUtc is missing on a published section' }
        if ($ExpectedPublishedOnUtc -eq 'absent' -and -not [string]::IsNullOrWhiteSpace($publishedOn)) { $problems += 'publishedOnUtc is set on a section that was never published' }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()
}

function New-E05FamilyReadAssert {
    <#
        The Family read is a bare ordered section list. Presence and absence are
        asserted by pinned id so retained rows from an earlier run cannot make the
        check pass or fail for the wrong reason.
    #>
    [CmdletBinding()]
    param(
        [string[]]$RequiredIds = @(),
        [string[]]$ForbiddenIds = @()
    )

    return ({
        param($status, $body)
        $problems = @()
        $sections = @($body)
        $ids = @($sections | ForEach-Object { [string]$_.id })

        foreach ($required in @($RequiredIds)) {
            if ($ids -notcontains $required) { $problems += "the Family read does not return the published visible section $required" }
        }
        foreach ($forbidden in @($ForbiddenIds)) {
            if ($ids -contains $forbidden) { $problems += "the Family read returns $forbidden, which must be omitted" }
        }

        foreach ($section in $sections) {
            $names = @($section.PSObject.Properties.Name)
            foreach ($member in @('id', 'kind', 'displayOrder', 'items')) {
                if ($names -notcontains $member) { $problems += "a Family section is missing $member" }
            }
            $items = @($section.items)
            if ($items.Count -eq 0) { $problems += "the Family read returns section $($section.id) with no items" }
            foreach ($candidate in $items) {
                if ($candidate -is [string]) { $problems += 'a Family item is a JSON string instead of a typed object' }
            }
            if ($names -contains 'status' -or $names -contains 'version' -or $names -contains 'sourceId') {
                $problems += "the Family read leaks Admin-only members for section $($section.id)"
            }
        }

        $actual = "status=$status sections=$($sections.Count) "
        $actual += "requiredPresent=$( @($RequiredIds | Where-Object { $ids -contains $_ }).Count ) "
        $actual += "forbiddenPresent=$( @($ForbiddenIds | Where-Object { $ids -contains $_ }).Count )"

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()
}

function New-E05FamilyFeaturedAssert {
    <#
        One published featured section as a Family reader sees it: exactly one item
        carrying the live catalog name, icon and caregiver type, the 'service'
        target naming that same id, and no editorial override anywhere.
    #>
    [CmdletBinding()]
    param(
        [string]$SectionId,
        [string]$ServiceId,
        [string]$ArabicName,
        [string]$EnglishName,
        [string]$IconKey,
        [int]$CaregiverType
    )

    return ({
        param($status, $body)
        $problems = @()
        $sections = @($body)
        $matches = @($sections | Where-Object { [string]$_.id -eq $SectionId })
        $actual = "status=$status matchedSections=$($matches.Count)"

        if ($matches.Count -ne 1) {
            $problems += "expected exactly one Family section $SectionId, found $($matches.Count)"
            return @{ Actual = $actual; Problems = $problems }
        }

        $section = $matches[0]
        $items = @($section.items)
        $actual += " kind=$($section.kind) displayOrder=$($section.displayOrder) items=$($items.Count)"

        if ([string]$section.kind -ne 'featured-services') { $problems += "expected kind 'featured-services', got '$($section.kind)'" }

        if ($items.Count -ne 1) {
            $problems += "expected exactly one item in $SectionId, got $($items.Count)"
            return @{ Actual = $actual; Problems = $problems }
        }

        $item = $items[0]
        $names = @($item.PSObject.Properties.Name)
        $observedServiceId = ''
        $observedTargetType = ''
        $observedTargetServiceId = ''
        $observedArabic = ''
        $observedEnglish = ''
        $observedIcon = ''
        $observedType = -1
        if ($names -contains 'serviceId' -and $null -ne $item.serviceId) { $observedServiceId = [string]$item.serviceId }
        if ($names -contains 'targetType' -and $null -ne $item.targetType) { $observedTargetType = [string]$item.targetType }
        if ($names -contains 'targetServiceId' -and $null -ne $item.targetServiceId) { $observedTargetServiceId = [string]$item.targetServiceId }
        if ($names -contains 'serviceNameArabic' -and $null -ne $item.serviceNameArabic) { $observedArabic = [string]$item.serviceNameArabic }
        if ($names -contains 'serviceNameEnglish' -and $null -ne $item.serviceNameEnglish) { $observedEnglish = [string]$item.serviceNameEnglish }
        if ($names -contains 'serviceIconKey' -and $null -ne $item.serviceIconKey) { $observedIcon = [string]$item.serviceIconKey }
        if ($names -contains 'serviceCaregiverType' -and $null -ne $item.serviceCaregiverType) { $observedType = [int]$item.serviceCaregiverType }

        $overrides = 0
        foreach ($member in @('arabicTitle', 'englishTitle', 'arabicBody', 'englishBody', 'imageKey', 'iconKey')) {
            if ($names -contains $member -and $null -ne $item.$member -and -not [string]::IsNullOrWhiteSpace([string]$item.$member)) { $overrides++ }
        }

        $actual += " serviceIdMatches=$($observedServiceId -eq $ServiceId) targetType=$observedTargetType targetServiceIdMatches=$($observedTargetServiceId -eq $ServiceId) nameArabicMatches=$($observedArabic -eq $ArabicName) nameEnglishMatches=$($observedEnglish -eq $EnglishName) iconMatches=$($observedIcon -eq $IconKey) caregiverType=$observedType editorialOverrides=$overrides"

        if ($observedServiceId -ne $ServiceId) { $problems += "expected item serviceId $ServiceId, got '$observedServiceId'" }
        if ($observedTargetType -ne 'service') { $problems += "expected the featured item target type 'service', got '$observedTargetType'" }
        if ($observedTargetServiceId -ne $ServiceId) { $problems += "expected targetServiceId $ServiceId, got '$observedTargetServiceId'" }
        if ($observedArabic -ne $ArabicName) { $problems += 'the featured item does not carry the live catalog Arabic name' }
        if ($observedEnglish -ne $EnglishName) { $problems += 'the featured item does not carry the live catalog English name' }
        if ($observedIcon -ne $IconKey) { $problems += 'the featured item does not carry the live catalog icon key' }
        if ($observedType -ne $CaregiverType) { $problems += "expected caregiver type $CaregiverType, got $observedType" }
        if ($overrides -gt 0) { $problems += 'the featured item carries an editorial override, which featured entries must not have' }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()
}

# ---------------------------------------------------------------------------
# Check plan builders
# ---------------------------------------------------------------------------

function New-E05CheckPlanStageOne {
    <#
        Checks that need no captured runtime value: the authentication and role
        boundaries, the Admin paging/filter contract, the request-level content and
        media-key rules, the Family read of the pinned published fixtures, and the
        five draft creations whose ids the later stages use.
    #>
    [CmdletBinding()]
    param(
        [hashtable]$Tokens,
        [string]$RandomGuid,
        [string]$PublicMediaKey
    )

    $routes = Get-E05Route
    $codes = $routes.Codes
    $kinds = $routes.Kinds
    $statuses = $routes.Statuses
    $admin = $routes.AdminList

    $plain = New-E05StatusAssert
    $longTitle = 'a' * 201
    $elevenItems = @()
    foreach ($index in 1..11) {
        $elevenItems += @{ arabicTitle = "E05 item $index"; englishTitle = "E05 item $index" }
    }

    $createdLinkItems = @(@{
        arabicTitle = $routes.CreatedLinkArabicTitle
        englishTitle = $routes.CreatedLinkEnglishTitle
        target = @{ type = $routes.ServicesTarget }
    })

    return @(
        [ordered]@{
            Name = 'auth.admin-list-anonymous'
            Method = 'GET'
            Path = $admin
            Actor = 'anonymous'
            Token = $null
            ExpectedStatus = 401
            Expected = 'HTTP 401 because the Admin family-home routes take a bearer token only'
            Assert = $plain
        }
        [ordered]@{
            Name = 'role.admin-list-family-denied'
            Method = 'GET'
            Path = $admin
            Actor = 'family-denied (Family account)'
            Token = $Tokens['family-denied']
            ExpectedStatus = 403
            Expected = 'HTTP 403 because a Family account never carries the CmsContent policy'
            Assert = $plain
        }
        [ordered]@{
            Name = 'auth.family-read-anonymous'
            Method = 'GET'
            Path = $routes.FamilyList
            Actor = 'anonymous'
            Token = $null
            ExpectedStatus = 401
            Expected = 'HTTP 401 because the Family home read requires the FamilyAccess policy'
            Assert = $plain
        }
        [ordered]@{
            Name = 'invalid.admin-list-status-token'
            Method = 'GET'
            Path = "${admin}?status=2"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            ExpectedStatus = 400
            Expected = 'HTTP 400 Cms.FamilyHome.SectionInvalid because a bare numeric status token was never part of the contract'
            Assert = (New-E05CodeAssert -ExpectedStatus 400 -ExpectedCode $codes.SectionInvalid)
        }
        [ordered]@{
            Name = 'invalid.admin-list-kind-token'
            Method = 'GET'
            Path = "${admin}?kind=9"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            ExpectedStatus = 400
            Expected = 'HTTP 400 Cms.FamilyHome.InvalidKind because a bare numeric kind token is rejected rather than parsed as an enum'
            Assert = (New-E05CodeAssert -ExpectedStatus 400 -ExpectedCode $codes.InvalidKind)
        }
        [ordered]@{
            Name = 'paging.admin-list-clamped'
            Method = 'GET'
            Path = "${admin}?page=0&pageSize=500"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            ExpectedStatus = 200
            Expected = 'HTTP 200 with page clamped to 1, pageSize clamped to 100 and a totalCount independent of the page'
            Assert = (New-E05PagedListAssert -ExpectedPage 1 -ExpectedPageSize 100)
        }
        [ordered]@{
            Name = 'paging.admin-list-kind-filter'
            Method = 'GET'
            Path = "${admin}?kind=featured-services&page=1&pageSize=100"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            ExpectedStatus = 200
            Expected = "HTTP 200 where every returned item kind is '$($kinds.FeaturedServices)' for a kebab-case kind filter"
            Assert = (New-E05PagedListAssert -ExpectedPage 1 -ExpectedPageSize 100 -ItemProperty 'kind' -ExpectedItemValue $kinds.FeaturedServices)
        }
        [ordered]@{
            Name = 'paging.admin-list-status-filter'
            Method = 'GET'
            Path = "${admin}?status=Draft&page=1&pageSize=100"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            ExpectedStatus = 200
            Expected = "HTTP 200 where every returned item status is '$($statuses.Draft)'"
            Assert = (New-E05PagedListAssert -ExpectedPage 1 -ExpectedPageSize 100 -ItemProperty 'status' -ExpectedItemValue $statuses.Draft)
        }
        [ordered]@{
            Name = 'unknown.admin-detail'
            Method = 'GET'
            Path = "$admin/$RandomGuid"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            ExpectedStatus = 404
            Expected = 'HTTP 404 Cms.FamilyHome.SectionNotFound for an unknown section id'
            Assert = (New-E05CodeAssert -ExpectedStatus 404 -ExpectedCode $codes.SectionNotFound)
        }
        [ordered]@{
            Name = 'invalid.create-unknown-kind'
            Method = 'POST'
            Path = $admin
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ kind = 'bogus-kind'; items = @(); displayOrder = 0; isVisible = $true } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 400
            Expected = 'HTTP 400 Cms.FamilyHome.InvalidKind for an unknown kind token on create'
            Assert = (New-E05CodeAssert -ExpectedStatus 400 -ExpectedCode $codes.InvalidKind)
        }
        [ordered]@{
            Name = 'invalid.create-negative-display-order'
            Method = 'POST'
            Path = $admin
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ kind = 'quick-links'; items = @(); displayOrder = -1; isVisible = $true } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 400
            Expected = 'HTTP 400 Api.Validation.Failed with a field-level error for DisplayOrder, the shape rule the draft validator owns'
            Assert = (New-E05ValidationAssert -ExpectedField 'DisplayOrder')
        }
        [ordered]@{
            Name = 'invalid.create-over-cap-title'
            Method = 'POST'
            Path = $admin
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ kind = 'banners'; items = @(@{ arabicTitle = $longTitle; englishTitle = $longTitle }); displayOrder = 0; isVisible = $true } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 400
            Expected = 'HTTP 400 Cms.FamilyHome.SectionInvalid because a 201-character title pair exceeds the 200-character cap'
            Assert = (New-E05CodeAssert -ExpectedStatus 400 -ExpectedCode $codes.SectionInvalid)
        }
        [ordered]@{
            Name = 'invalid.create-missing-media-key'
            Method = 'POST'
            Path = $admin
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ kind = 'banners'; items = @(@{ arabicTitle = $routes.BannerArabicTitle; englishTitle = $routes.BannerEnglishTitle; imageKey = $routes.MissingMediaKeySuffix }); displayOrder = 0; isVisible = $true } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 400
            Expected = 'HTTP 400 Cms.FamilyHome.SectionInvalid because a public media key must name a file that exists under the public storage root'
            Assert = (New-E05CodeAssert -ExpectedStatus 400 -ExpectedCode $codes.SectionInvalid)
        }
        [ordered]@{
            Name = 'invalid.create-private-media-key'
            Method = 'POST'
            Path = $admin
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ kind = 'banners'; items = @(@{ arabicTitle = $routes.BannerArabicTitle; englishTitle = $routes.BannerEnglishTitle; imageKey = $routes.PrivateMediaKey }); displayOrder = 0; isVisible = $true } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 400
            Expected = 'HTTP 400 Cms.FamilyHome.SectionInvalid because a private/ key selects the sibling private directory and is never a public family-home key'
            Assert = (New-E05CodeAssert -ExpectedStatus 400 -ExpectedCode $codes.SectionInvalid)
        }
        [ordered]@{
            Name = 'invalid.create-traversal-media-key'
            Method = 'POST'
            Path = $admin
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ kind = 'banners'; items = @(@{ arabicTitle = $routes.BannerArabicTitle; englishTitle = $routes.BannerEnglishTitle; imageKey = $routes.TraversalMediaKey }); displayOrder = 0; isVisible = $true } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 400
            Expected = 'HTTP 400 Cms.FamilyHome.SectionInvalid because a key that escapes the public root after normalization is refused'
            Assert = (New-E05CodeAssert -ExpectedStatus 400 -ExpectedCode $codes.SectionInvalid)
        }
        [ordered]@{
            Name = 'invalid.create-duplicate-featured'
            Method = 'POST'
            Path = $admin
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ kind = 'featured-services'; items = @(@{ serviceId = $RandomGuid }, @{ serviceId = $RandomGuid }); displayOrder = 0; isVisible = $true } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 400
            Expected = 'HTTP 400 Cms.FamilyHome.SectionInvalid because a featured service id may not repeat inside one section'
            Assert = (New-E05CodeAssert -ExpectedStatus 400 -ExpectedCode $codes.SectionInvalid)
        }
        [ordered]@{
            Name = 'invalid.create-eleven-items'
            Method = 'POST'
            Path = $admin
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ kind = 'quick-links'; items = $elevenItems; displayOrder = 0; isVisible = $true } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 400
            Expected = 'HTTP 400 Cms.FamilyHome.SectionInvalid because a section holds at most 10 items'
            Assert = (New-E05CodeAssert -ExpectedStatus 400 -ExpectedCode $codes.SectionInvalid)
        }
        [ordered]@{
            Name = 'write.create-quick-links'
            Method = 'POST'
            Path = $admin
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ kind = 'quick-links'; items = $createdLinkItems; displayOrder = 1201; isVisible = $true } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 201
            Expected = 'HTTP 201 for a typed quick-link draft: version 1, status Draft, no twin, no raw itemsJson and the typed services target returned as an object'
            Assert = (New-E05SectionAssert -ExpectedKind $kinds.QuickLinks -ExpectedStatus $statuses.Draft -ExpectedVersion 1 -ExpectedSourceId 'none' -ExpectedItemCount 1 -ExpectedItemTargetType $routes.ServicesTarget -ExpectedPublishedOnUtc 'absent')
        }
        [ordered]@{
            Name = 'write.create-empty-banner-draft'
            Method = 'POST'
            Path = $admin
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ kind = 'banners'; items = @(); displayOrder = 1202; isVisible = $true } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 201
            Expected = 'HTTP 201 because a draft section may be empty; completeness is only enforced at publication'
            Assert = (New-E05SectionAssert -ExpectedKind $kinds.Banners -ExpectedStatus $statuses.Draft -ExpectedVersion 1 -ExpectedItemCount 0 -ExpectedPublishedOnUtc 'absent')
        }
        [ordered]@{
            Name = 'write.create-banner-without-image'
            Method = 'POST'
            Path = $admin
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ kind = 'banners'; items = @(@{ arabicTitle = $routes.BannerArabicTitle; englishTitle = $routes.BannerEnglishTitle }); displayOrder = 1203; isVisible = $true } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 201
            Expected = 'HTTP 201 for a banner draft whose image key is still missing: publication, not authoring, enforces the banner image key'
            Assert = (New-E05SectionAssert -ExpectedKind $kinds.Banners -ExpectedStatus $statuses.Draft -ExpectedVersion 1 -ExpectedItemCount 1 -ExpectedPublishedOnUtc 'absent')
        }
        [ordered]@{
            Name = 'write.create-banner-with-public-key'
            Method = 'POST'
            Path = $admin
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ kind = 'banners'; items = @(@{ arabicTitle = $routes.BannerArabicTitle; englishTitle = $routes.BannerEnglishTitle; imageKey = $PublicMediaKey }); displayOrder = 1204; isVisible = $true } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 201
            Expected = 'HTTP 201 because the synthetic public key really exists under the verified public storage root'
            Assert = (New-E05SectionAssert -ExpectedKind $kinds.Banners -ExpectedStatus $statuses.Draft -ExpectedVersion 1 -ExpectedItemCount 1 -ExpectedPublishedOnUtc 'absent')
        }
        [ordered]@{
            Name = 'write.create-inactive-featured'
            Method = 'POST'
            Path = $admin
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ kind = 'featured-services'; items = @(@{ serviceId = (Get-E05FixtureDefinition).Services.Inactive.Id }); displayOrder = 1205; isVisible = $true } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 201
            Expected = 'HTTP 201 because referencing a currently inactive catalog service is legal while authoring; only publication requires activeness'
            Assert = (New-E05SectionAssert -ExpectedKind $kinds.FeaturedServices -ExpectedStatus $statuses.Draft -ExpectedVersion 1 -ExpectedItemCount 1 -ExpectedPublishedOnUtc 'absent')
        }
        [ordered]@{
            Name = 'write.create-inactive-target-link'
            Method = 'POST'
            Path = $admin
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ kind = 'quick-links'; items = @(@{ arabicTitle = $routes.InactiveTargetArabicTitle; englishTitle = $routes.InactiveTargetEnglishTitle; target = @{ type = $routes.ServiceTarget; serviceId = (Get-E05FixtureDefinition).Services.Inactive.Id } }); displayOrder = 1206; isVisible = $true } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 201
            Expected = 'HTTP 201 for a typed service-target quick-link draft over a currently inactive service'
            Assert = (New-E05SectionAssert -ExpectedKind $kinds.QuickLinks -ExpectedStatus $statuses.Draft -ExpectedVersion 1 -ExpectedItemCount 1 -ExpectedPublishedOnUtc 'absent')
        }
        [ordered]@{
            Name = 'read.family-owner-published-and-omitted'
            Method = 'GET'
            Path = $routes.FamilyList
            Actor = 'family-owner'
            Token = $Tokens['family-owner']
            ExpectedStatus = 200
            Expected = 'HTTP 200 returning the pinned published visible section and omitting the two pinned sections whose referenced service is inactive'
            Assert = (New-E05FamilyReadAssert -RequiredIds @((Get-E05FixtureDefinition).PinnedSections.ActiveFeatured.Id) -ForbiddenIds @((Get-E05FixtureDefinition).PinnedSections.InactiveFeatured.Id, (Get-E05FixtureDefinition).PinnedSections.InactiveTargetLink.Id))
        }
        [ordered]@{
            Name = 'read.family-featured-live-catalog-fields'
            Method = 'GET'
            Path = $routes.FamilyList
            Actor = 'family-viewer'
            Token = $Tokens['family-viewer']
            ExpectedStatus = 200
            Expected = 'HTTP 200 whose featured item carries the live catalog name/icon/caregiver type and no editorial override'
            Assert = (New-E05FamilyFeaturedAssert -SectionId (Get-E05FixtureDefinition).PinnedSections.ActiveFeatured.Id -ServiceId (Get-E05FixtureDefinition).Services.Active.Id -ArabicName (Get-E05FixtureDefinition).Services.Active.ArabicName -EnglishName (Get-E05FixtureDefinition).Services.Active.EnglishName -IconKey (Get-E05FixtureDefinition).Services.Active.IconPath -CaregiverType (Get-E05FixtureDefinition).Services.Active.CaregiverType)
        }
        [ordered]@{
            Name = 'read.family-member-of-the-same-family'
            Method = 'GET'
            Path = $routes.FamilyList
            Actor = 'family-viewer (member)'
            Token = $Tokens['family-viewer']
            ExpectedStatus = 200
            Expected = 'HTTP 200 because a family member who is not the owner still reads the shared editorial home'
            Assert = (New-E05FamilyReadAssert -RequiredIds @((Get-E05FixtureDefinition).PinnedSections.ActiveFeatured.Id))
        }
        [ordered]@{
            Name = 'read.family-account-without-family'
            Method = 'GET'
            Path = $routes.FamilyList
            Actor = 'family-none (Family account, no family)'
            Token = $Tokens['family-none']
            ExpectedStatus = 404
            Expected = 'HTTP 404 Families.Family.NotFound because the caller resolves to no non-deleted family'
            Assert = (New-E05CodeAssert -ExpectedStatus 404 -ExpectedCode $codes.FamilyNotFound)
        }
    )
}

function New-E05CheckPlanStageTwo {
    <#
        Checks that need the ids captured from stage one: the search predicate, the
        version preconditions, the draft update and every publication refusal,
        including the two that only a stored payload can produce.
    #>
    [CmdletBinding()]
    param(
        [hashtable]$Tokens,
        [hashtable]$State
    )

    $routes = Get-E05Route
    $codes = $routes.Codes
    $kinds = $routes.Kinds
    $statuses = $routes.Statuses
    $admin = $routes.AdminList
    $definition = Get-E05FixtureDefinition

    $linkId = [string]$State.LinkId
    $linkVersion = [int]$State.LinkVersion

    $staleExpectation = 'HTTP 409 Cms.FamilyHome.StaleVersion because the supplied precondition no longer matches the current version'

    return @(
        [ordered]@{
            Name = 'paging.admin-list-search'
            Method = 'GET'
            Path = "${admin}?search=$($routes.SearchToken)&page=1&pageSize=100"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            ExpectedStatus = 200
            Expected = 'HTTP 200 whose returned sections all carry the search token inside their stored typed payload, proving the predicate reads content and not a section title column'
            Assert = (New-E05SearchAssert -PinnedId $definition.PinnedSections.InactiveTargetLink.Id -Token $routes.SearchToken)
        }
        [ordered]@{
            Name = 'invalid.update-stale-version'
            Method = 'PUT'
            Path = "$admin/$linkId"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ items = @(@{ arabicTitle = $routes.UpdatedLinkArabicTitle; englishTitle = $routes.UpdatedLinkEnglishTitle; target = @{ type = $routes.ServicesTarget } }); displayOrder = 1201; isVisible = $true; expectedVersion = 99 } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 409
            Expected = $staleExpectation
            Assert = (New-E05CodeAssert -ExpectedStatus 409 -ExpectedCode $codes.StaleVersion)
        }
        [ordered]@{
            Name = 'invalid.update-negative-version'
            Method = 'PUT'
            Path = "$admin/$linkId"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ items = @(); displayOrder = 1201; isVisible = $true; expectedVersion = -1 } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 400
            Expected = 'HTTP 400 Cms.FamilyHome.SectionInvalid because a negative precondition is malformed, not a conflict'
            Assert = (New-E05CodeAssert -ExpectedStatus 400 -ExpectedCode $codes.SectionInvalid)
        }
        [ordered]@{
            Name = 'invalid.update-negative-display-order'
            Method = 'PUT'
            Path = "$admin/$linkId"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ items = @(); displayOrder = -1; isVisible = $true; expectedVersion = 0 } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 400
            Expected = 'HTTP 400 Api.Validation.Failed with a field-level error for DisplayOrder (the zero precondition means "use the current version")'
            Assert = (New-E05ValidationAssert -ExpectedField 'DisplayOrder')
        }
        [ordered]@{
            Name = 'write.update-draft'
            Method = 'PUT'
            Path = "$admin/$linkId"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ items = @(@{ arabicTitle = $routes.UpdatedLinkArabicTitle; englishTitle = $routes.UpdatedLinkEnglishTitle; target = @{ type = $routes.ServicesTarget } }); displayOrder = 1201; isVisible = $true; expectedVersion = $linkVersion } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 200
            Expected = 'HTTP 200 returning the same draft row with its version advanced and the typed items replaced'
            Assert = (New-E05SectionAssert -ExpectedKind $kinds.QuickLinks -ExpectedStatus $statuses.Draft -ExpectedVersion ($linkVersion + 1) -ExpectedSourceId 'none' -ExpectedItemCount 1 -ExpectedItemTargetType $routes.ServicesTarget)
        }
        [ordered]@{
            Name = 'invalid.publish-empty-draft'
            Method = 'POST'
            Path = "$admin/$($State.EmptyBannerId)/publish"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ expectedVersion = 0 } | ConvertTo-Json -Compress)
            ExpectedStatus = 400
            Expected = 'HTTP 400 Cms.FamilyHome.Incomplete because a published section requires at least one item'
            Assert = (New-E05CodeAssert -ExpectedStatus 400 -ExpectedCode $codes.Incomplete)
        }
        [ordered]@{
            Name = 'invalid.publish-banner-without-image'
            Method = 'POST'
            Path = "$admin/$($State.BannerWithoutImageId)/publish"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ expectedVersion = 0 } | ConvertTo-Json -Compress)
            ExpectedStatus = 400
            Expected = 'HTTP 400 Cms.FamilyHome.Incomplete because a published banner requires an image key'
            Assert = (New-E05CodeAssert -ExpectedStatus 400 -ExpectedCode $codes.Incomplete)
        }
        [ordered]@{
            Name = 'invalid.publish-inactive-featured'
            Method = 'POST'
            Path = "$admin/$($State.InactiveFeaturedId)/publish"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ expectedVersion = 0 } | ConvertTo-Json -Compress)
            ExpectedStatus = 400
            Expected = 'HTTP 400 Cms.FamilyHome.Incomplete because a referenced catalog service must exist and be active at publication'
            Assert = (New-E05CodeAssert -ExpectedStatus 400 -ExpectedCode $codes.Incomplete)
        }
        [ordered]@{
            Name = 'invalid.publish-inactive-target'
            Method = 'POST'
            Path = "$admin/$($State.InactiveTargetId)/publish"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ expectedVersion = 0 } | ConvertTo-Json -Compress)
            ExpectedStatus = 400
            Expected = 'HTTP 400 Cms.FamilyHome.Incomplete because a service-target quick link must reference an active catalog service at publication'
            Assert = (New-E05CodeAssert -ExpectedStatus 400 -ExpectedCode $codes.Incomplete)
        }
        [ordered]@{
            Name = 'invalid.publish-stored-private-key'
            Method = 'POST'
            Path = "$admin/$($definition.PinnedSections.PrivateStoredKeyBanner.Id)/publish"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ expectedVersion = 0 } | ConvertTo-Json -Compress)
            ExpectedStatus = 400
            Expected = 'HTTP 400 Cms.FamilyHome.SectionInvalid because publication validates the stored media key, and a private/ key is never a public key'
            Assert = (New-E05CodeAssert -ExpectedStatus 400 -ExpectedCode $codes.SectionInvalid)
        }
        [ordered]@{
            Name = 'invalid.publish-stale-version'
            Method = 'POST'
            Path = "$admin/$linkId/publish"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ expectedVersion = 99 } | ConvertTo-Json -Compress)
            ExpectedStatus = 409
            Expected = $staleExpectation
            Assert = (New-E05CodeAssert -ExpectedStatus 409 -ExpectedCode $codes.StaleVersion)
        }
        [ordered]@{
            Name = 'invalid.publish-negative-version'
            Method = 'POST'
            Path = "$admin/$linkId/publish"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ expectedVersion = -1 } | ConvertTo-Json -Compress)
            ExpectedStatus = 400
            Expected = 'HTTP 400 Cms.FamilyHome.SectionInvalid because a negative precondition is malformed'
            Assert = (New-E05CodeAssert -ExpectedStatus 400 -ExpectedCode $codes.SectionInvalid)
        }
        [ordered]@{
            Name = 'write.publish-quick-links'
            Method = 'POST'
            Path = "$admin/$linkId/publish"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ expectedVersion = ($linkVersion + 1) } | ConvertTo-Json -Compress)
            ExpectedStatus = 200
            Expected = 'HTTP 200 returning the same row as Published with a publication instant and its version advanced'
            Assert = (New-E05SectionAssert -ExpectedKind $kinds.QuickLinks -ExpectedStatus $statuses.Published -ExpectedVersion ($linkVersion + 2) -ExpectedSourceId 'none' -ExpectedItemCount 1 -ExpectedPublishedOnUtc 'present')
        }
        [ordered]@{
            Name = 'write.publish-banner-with-public-key'
            Method = 'POST'
            Path = "$admin/$($State.BannerWithPublicKeyId)/publish"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ expectedVersion = 0 } | ConvertTo-Json -Compress)
            ExpectedStatus = 200
            Expected = 'HTTP 200 because the stored public media key exists, so publication passes the same stored-key rule that refused the private key'
            Assert = (New-E05SectionAssert -ExpectedKind $kinds.Banners -ExpectedStatus $statuses.Published -ExpectedVersion 2 -ExpectedItemCount 1 -ExpectedPublishedOnUtc 'present')
        }
        [ordered]@{
            Name = 'conflict.publish-already-published'
            Method = 'POST'
            Path = "$admin/$linkId/publish"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ expectedVersion = 0 } | ConvertTo-Json -Compress)
            ExpectedStatus = 400
            Expected = 'HTTP 400 Cms.FamilyHome.SectionInvalid because only a draft is published, matching the existing landing convention'
            Assert = (New-E05CodeAssert -ExpectedStatus 400 -ExpectedCode $codes.SectionInvalid)
        }
        [ordered]@{
            Name = 'read.family-published-visible-new-sections'
            Method = 'GET'
            Path = $routes.FamilyList
            Actor = 'family-owner'
            Token = $Tokens['family-owner']
            ExpectedStatus = 200
            Expected = 'HTTP 200 where the two sections this run published are now visible to a Family reader'
            Assert = (New-E05FamilyReadAssert -RequiredIds @($linkId, [string]$State.BannerWithPublicKeyId, $definition.PinnedSections.ActiveFeatured.Id))
        }
    )
}

function New-E05CheckPlanStageThree {
    <#
        The lifecycle, deletion and immutability checks that need the versions the
        publish step produced.
    #>
    [CmdletBinding()]
    param(
        [hashtable]$Tokens,
        [hashtable]$State
    )

    $routes = Get-E05Route
    $codes = $routes.Codes
    $kinds = $routes.Kinds
    $statuses = $routes.Statuses
    $admin = $routes.AdminList
    $definition = Get-E05FixtureDefinition

    $linkId = [string]$State.LinkId
    $publishedVersion = [int]$State.LinkPublishedVersion

    $finalItems = @(@{
        arabicTitle = $routes.FinalLinkArabicTitle
        englishTitle = $routes.FinalLinkEnglishTitle
        target = @{ type = $routes.ServicesTarget }
    })

    return @(
        [ordered]@{
            Name = 'write.update-published-creates-twin'
            Method = 'PUT'
            Path = "$admin/$linkId"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ items = $finalItems; displayOrder = 1201; isVisible = $true; expectedVersion = $publishedVersion } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 200
            Expected = 'HTTP 200 returning a fresh Draft twin (version 1) whose sourceId names the published row it was forked from'
            Assert = (New-E05SectionAssert -ExpectedKind $kinds.QuickLinks -ExpectedStatus $statuses.Draft -ExpectedVersion 1 -ExpectedSourceId $linkId -ExpectedItemCount 1 -ExpectedPublishedOnUtc 'absent')
        }
        [ordered]@{
            Name = 'read.published-row-unchanged-by-twin'
            Method = 'GET'
            Path = "$admin/$linkId"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            ExpectedStatus = 200
            Expected = 'HTTP 200 where the published row still holds its own version and content: a twin edit never rewrites live content'
            Assert = (New-E05SectionAssert -ExpectedKind $kinds.QuickLinks -ExpectedStatus $statuses.Published -ExpectedVersion $publishedVersion -ExpectedSourceId 'none' -ExpectedPublishedOnUtc 'present')
        }
        [ordered]@{
            Name = 'conflict.delete-published'
            Method = 'DELETE'
            Path = "$admin/$linkId"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            ExpectedStatus = 409
            Expected = 'HTTP 409 Cms.FamilyHome.PublishedHistoryConflict because a published section is retained history and is never deleted'
            Assert = (New-E05CodeAssert -ExpectedStatus 409 -ExpectedCode $codes.PublishedHistoryConflict)
        }
        [ordered]@{
            Name = 'write.delete-draft-twin'
            Method = 'DELETE'
            Path = "$admin/{write.update-published-creates-twin.id}"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            ExpectedStatus = 204
            Expected = 'HTTP 204 because a draft is the only deletable state'
            Assert = (New-E05StatusAssert)
        }
        [ordered]@{
            Name = 'read.deleted-twin-not-found'
            Method = 'GET'
            Path = "$admin/{write.update-published-creates-twin.id}"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            ExpectedStatus = 404
            Expected = 'HTTP 404 Cms.FamilyHome.SectionNotFound because deleting the draft twin removed it'
            Assert = (New-E05CodeAssert -ExpectedStatus 404 -ExpectedCode $codes.SectionNotFound)
        }
        [ordered]@{
            Name = 'read.published-row-retained-after-twin-delete'
            Method = 'GET'
            Path = "$admin/$linkId"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            ExpectedStatus = 200
            Expected = 'HTTP 200 where the published row survives the deletion of its draft twin untouched'
            Assert = (New-E05SectionAssert -ExpectedKind $kinds.QuickLinks -ExpectedStatus $statuses.Published -ExpectedVersion $publishedVersion -ExpectedSourceId 'none')
        }
        [ordered]@{
            Name = 'write.update-published-creates-second-twin'
            Method = 'PUT'
            Path = "$admin/$linkId"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ items = $finalItems; displayOrder = 1201; isVisible = $true; expectedVersion = $publishedVersion } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 200
            Expected = 'HTTP 200 returning a new draft twin for the same published row, so a second twin can be published'
            Assert = (New-E05SectionAssert -ExpectedKind $kinds.QuickLinks -ExpectedStatus $statuses.Draft -ExpectedVersion 1 -ExpectedSourceId $linkId)
        }
        [ordered]@{
            Name = 'write.publish-twin-archives-predecessor'
            Method = 'POST'
            Path = "$admin/{write.update-published-creates-second-twin.id}/publish"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ expectedVersion = 1 } | ConvertTo-Json -Compress)
            ExpectedStatus = 200
            Expected = 'HTTP 200 publishing the twin and superseding the previously published section of the same kind'
            Assert = (New-E05SectionAssert -ExpectedKind $kinds.QuickLinks -ExpectedStatus $statuses.Published -ExpectedVersion 2 -ExpectedSourceId $linkId -ExpectedPublishedOnUtc 'present')
        }
        [ordered]@{
            Name = 'read.superseded-published-archived'
            Method = 'GET'
            Path = "$admin/$linkId"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            ExpectedStatus = 200
            Expected = 'HTTP 200 where the superseded published section is now Archived and still readable'
            Assert = (New-E05SectionAssert -ExpectedKind $kinds.QuickLinks -ExpectedStatus $statuses.Archived -ExpectedSourceId 'none')
        }
        [ordered]@{
            Name = 'conflict.update-archived'
            Method = 'PUT'
            Path = "$admin/$linkId"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ items = $finalItems; displayOrder = 1201; isVisible = $true; expectedVersion = 0 } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 409
            Expected = 'HTTP 409 Cms.FamilyHome.ImmutableHistory because archived history is immutable'
            Assert = (New-E05CodeAssert -ExpectedStatus 409 -ExpectedCode $codes.ImmutableHistory)
        }
        [ordered]@{
            Name = 'conflict.publish-archived'
            Method = 'POST'
            Path = "$admin/$linkId/publish"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ expectedVersion = 0 } | ConvertTo-Json -Compress)
            ExpectedStatus = 409
            Expected = 'HTTP 409 Cms.FamilyHome.ImmutableHistory because an archived section is never republished'
            Assert = (New-E05CodeAssert -ExpectedStatus 409 -ExpectedCode $codes.ImmutableHistory)
        }
        [ordered]@{
            Name = 'conflict.visibility-archived'
            Method = 'POST'
            Path = "$admin/$linkId/visibility"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ value = $true; expectedVersion = 0 } | ConvertTo-Json -Compress)
            ExpectedStatus = 409
            Expected = 'HTTP 409 Cms.FamilyHome.ImmutableHistory because archived history cannot be re-hidden or re-shown'
            Assert = (New-E05CodeAssert -ExpectedStatus 409 -ExpectedCode $codes.ImmutableHistory)
        }
        [ordered]@{
            Name = 'conflict.delete-archived'
            Method = 'DELETE'
            Path = "$admin/$linkId"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            ExpectedStatus = 409
            Expected = 'HTTP 409 Cms.FamilyHome.PublishedHistoryConflict because archived history is retained and never deleted'
            Assert = (New-E05CodeAssert -ExpectedStatus 409 -ExpectedCode $codes.PublishedHistoryConflict)
        }
        [ordered]@{
            Name = 'conflict.delete-published-twin'
            Method = 'DELETE'
            Path = "$admin/{write.update-published-creates-second-twin.id}"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            ExpectedStatus = 409
            Expected = 'HTTP 409 Cms.FamilyHome.PublishedHistoryConflict because the twin is published now'
            Assert = (New-E05CodeAssert -ExpectedStatus 409 -ExpectedCode $codes.PublishedHistoryConflict)
        }
    )
}

function New-E05CheckPlanStageFour {
    <#
        The visibility and reorder checks. Every one of them addresses the
        second published twin, whose id stage three only produces while it
        runs, so this plan is built after the twin lifecycle has settled.
    #>
    [CmdletBinding()]
    param(
        [hashtable]$Tokens,
        [hashtable]$State
    )

    $routes = Get-E05Route
    $codes = $routes.Codes
    $admin = $routes.AdminList
    $definition = Get-E05FixtureDefinition

    return @(
        [ordered]@{
            Name = 'write.visibility-hide-published'
            Method = 'POST'
            Path = "$admin/$($State.TwinTwoId)/visibility"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ value = $false; expectedVersion = 2 } | ConvertTo-Json -Compress)
            ExpectedStatus = 204
            Expected = 'HTTP 204 because a published section may be hidden'
            Assert = (New-E05StatusAssert)
        }
        [ordered]@{
            Name = 'read.family-hidden-published-omitted'
            Method = 'GET'
            Path = $routes.FamilyList
            Actor = 'family-owner'
            Token = $Tokens['family-owner']
            ExpectedStatus = 200
            Expected = 'HTTP 200 that omits a published but hidden section while still returning the visible pinned section'
            Assert = (New-E05FamilyReadAssert -RequiredIds @($definition.PinnedSections.ActiveFeatured.Id) -ForbiddenIds @([string]$State.TwinTwoId))
        }
        [ordered]@{
            Name = 'write.visibility-restore-published'
            Method = 'POST'
            Path = "$admin/$($State.TwinTwoId)/visibility"
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ value = $true; expectedVersion = 3 } | ConvertTo-Json -Compress)
            ExpectedStatus = 204
            Expected = 'HTTP 204 restoring visibility on the same published section'
            Assert = (New-E05StatusAssert)
        }
        [ordered]@{
            Name = 'read.family-visibility-restored'
            Method = 'GET'
            Path = $routes.FamilyList
            Actor = 'family-viewer'
            Token = $Tokens['family-viewer']
            ExpectedStatus = 200
            Expected = 'HTTP 200 returning the section again once visibility is restored'
            Assert = (New-E05FamilyReadAssert -RequiredIds @([string]$State.TwinTwoId, $definition.PinnedSections.ActiveFeatured.Id))
        }
        [ordered]@{
            Name = 'write.reorder-known-sections'
            Method = 'POST'
            Path = $routes.AdminReorder
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ items = @(@{ id = [string]$State.TwinTwoId; displayOrder = 500 }, @{ id = [string]$State.BannerWithPublicKeyId; displayOrder = 501 }) } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 204
            Expected = 'HTTP 204 reordering two known sections'
            Assert = (New-E05StatusAssert)
        }
        [ordered]@{
            Name = 'invalid.reorder-unknown-id'
            Method = 'POST'
            Path = $routes.AdminReorder
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ items = @(@{ id = $State.RandomGuid; displayOrder = 0 }) } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 400
            Expected = 'HTTP 400 Cms.FamilyHome.ReorderInvalid because a reorder entry must reference a known section'
            Assert = (New-E05CodeAssert -ExpectedStatus 400 -ExpectedCode $codes.ReorderInvalid)
        }
        [ordered]@{
            Name = 'invalid.reorder-duplicate-id'
            Method = 'POST'
            Path = $routes.AdminReorder
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ items = @(@{ id = [string]$State.TwinTwoId; displayOrder = 0 }, @{ id = [string]$State.TwinTwoId; displayOrder = 1 }) } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 400
            Expected = 'HTTP 400 Cms.FamilyHome.ReorderInvalid because a section may appear exactly once in a reorder request'
            Assert = (New-E05CodeAssert -ExpectedStatus 400 -ExpectedCode $codes.ReorderInvalid)
        }
        [ordered]@{
            Name = 'invalid.reorder-empty'
            Method = 'POST'
            Path = $routes.AdminReorder
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ items = @() } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 400
            Expected = 'HTTP 400 Cms.FamilyHome.ReorderInvalid because an empty reorder request is malformed'
            Assert = (New-E05CodeAssert -ExpectedStatus 400 -ExpectedCode $codes.ReorderInvalid)
        }
        [ordered]@{
            Name = 'invalid.reorder-negative-order'
            Method = 'POST'
            Path = $routes.AdminReorder
            Actor = 'content-admin'
            Token = $Tokens['content-admin']
            Body = (@{ items = @(@{ id = [string]$State.TwinTwoId; displayOrder = -1 }) } | ConvertTo-Json -Depth 10 -Compress)
            ExpectedStatus = 400
            Expected = 'HTTP 400 Cms.FamilyHome.ReorderInvalid because display order is a non-negative integer'
            Assert = (New-E05CodeAssert -ExpectedStatus 400 -ExpectedCode $codes.ReorderInvalid)
        }
    )
}

function New-E05LogoutChecks {
    <# Every session this run created is revoked again. #>
    [CmdletBinding()]
    param([hashtable]$Tokens)

    $routes = Get-E05Route
    $checks = @()
    foreach ($actor in $Tokens.Keys) {
        $checks += [ordered]@{
            Name = "session.logout.$actor"
            Method = 'POST'
            Path = $routes.Logout
            Actor = $actor
            Token = $Tokens[$actor]
            ExpectedStatus = 204
            Expected = 'HTTP 204, revoking the session this run created'
            Assert = (New-E05StatusAssert)
        }
    }

    return $checks
}

function New-E05SqlCheckPlan {
    <#
        Read-only readbacks that prove the storage-level effects: the deleted twin
        is gone while exactly one Deleted history row for it remains, the
        superseded published row is archived in storage, and the pinned rows and
        the synthetic family are still present.
    #>
    [CmdletBinding()]
    param(
        [string]$DeletedTwinId,
        [string]$PublishedId,
        [string]$PublishedTwinId,
        [string]$FamilyId
    )

    $definition = Get-E05FixtureDefinition
    $pinnedIds = @(
        "'" + $definition.PinnedSections.ActiveFeatured.Id + "'"
        "'" + $definition.PinnedSections.InactiveFeatured.Id + "'"
        "'" + $definition.PinnedSections.InactiveTargetLink.Id + "'"
        "'" + $definition.PinnedSections.PrivateStoredKeyBanner.Id + "'"
    )

    return @(
        [ordered]@{
            Name = 'sql.delete-appends-history-row'
            Method = 'SQL'
            Path = 'psql:cms.family_home_section_history'
            Actor = 'local-readonly-db'
            ExpectedStatus = 200
            Expected = 'the deleted draft twin row is gone from cms.family_home_sections while exactly one Deleted (action 3) history row for it remains'
            Sql = "SELECT json_build_object('deletedRow', (SELECT count(*) FROM cms.family_home_sections WHERE id = '$DeletedTwinId'), 'deleteHistory', (SELECT count(*) FROM cms.family_home_section_history WHERE section_id = '$DeletedTwinId' AND action = 3));"
            Assert = ({
                param($status, $body)
                $problems = @()
                if ($null -eq $body) { return @{ Actual = 'no-json-body'; Problems = @('the readback returned no JSON') } }
                $actual = "deletedRow=$($body.deletedRow) deleteHistory=$($body.deleteHistory)"
                if ([int]$body.deletedRow -ne 0) { $problems += 'the deleted draft twin row is still present in cms.family_home_sections' }
                if ([int]$body.deleteHistory -ne 1) { $problems += 'exactly one Deleted history row must be appended for the deleted twin' }
                return @{ Actual = $actual; Problems = $problems }
            }).GetNewClosure()
        }
        [ordered]@{
            Name = 'sql.published-history-retained'
            Method = 'SQL'
            Path = 'psql:cms.family_home_sections'
            Actor = 'local-readonly-db'
            ExpectedStatus = 200
            Expected = 'the superseded published row is Archived in storage, its publication history is retained, the published twin is live, and the pinned rows and the synthetic family survive'
            Sql = "SELECT json_build_object('publishedRow', (SELECT count(*) FROM cms.family_home_sections WHERE id = '$PublishedId'), 'publishedStatus', (SELECT status FROM cms.family_home_sections WHERE id = '$PublishedId'), 'publishedHistory', (SELECT count(*) FROM cms.family_home_section_history WHERE section_id = '$PublishedId' AND action = 1), 'twinStatus', (SELECT status FROM cms.family_home_sections WHERE id = '$PublishedTwinId'), 'pinnedRows', (SELECT count(*) FROM cms.family_home_sections WHERE id IN ($($pinnedIds -join ', '))), 'familyRows', (SELECT count(*) FROM families.families WHERE id = '$FamilyId'));"
            Assert = ({
                param($status, $body)
                $problems = @()
                if ($null -eq $body) { return @{ Actual = 'no-json-body'; Problems = @('the readback returned no JSON') } }
                $actual = "publishedRow=$($body.publishedRow) publishedStatus=$($body.publishedStatus) publishedHistory=$($body.publishedHistory) twinStatus=$($body.twinStatus) pinnedRows=$($body.pinnedRows) familyRows=$($body.familyRows)"
                if ([int]$body.publishedRow -ne 1) { $problems += 'the superseded published row was removed from cms.family_home_sections' }
                if ([int]$body.publishedStatus -ne 3) { $problems += "the superseded published row is not Archived in storage (status $($body.publishedStatus))" }
                if ([int]$body.publishedHistory -lt 1) { $problems += 'no Published history row was retained for the superseded section' }
                if ([int]$body.twinStatus -ne 2) { $problems += "the published twin is not Published in storage (status $($body.twinStatus))" }
                if ([int]$body.pinnedRows -ne 4) { $problems += 'the four pinned fixture sections are not all still present' }
                if ([int]$body.familyRows -ne 1) { $problems += 'the synthetic family row is missing' }
                return @{ Actual = $actual; Problems = $problems }
            }).GetNewClosure()
        }
    )
}

function Get-E05UnitOnlyCoverage {
    <#
        What this HTTP adapter does not execute, and why. Every entry is covered by
        a focused unit test instead; nothing here is claimed as HTTP evidence.
    #>
    [CmdletBinding()]
    param()

    return @(
        [ordered]@{
            area = 'Stored payload that cannot be read'
            reason = 'A persistent malformed-content row would break the Admin list read for every later run, because that read projects every row in its page and fails closed. The adapter is INSERT-only, so it cannot clean the row up. Covered by FamilyHomeTests.AdminReads_FailClosedOnUnreadableStoredContent for the detail, list and public reads.'
        }
        [ordered]@{
            area = 'Arabic copy stored literally so the Admin search predicate can match it'
            reason = 'Proving Arabic matching over HTTP would put an Arabic literal in this adapter. Covered by the focused unit test that asserts the stored items_json keeps the Arabic characters and contains no \uXXXX escape, which is exactly what the pinned search predicate depends on. The HTTP search check runs over an ASCII token.'
        }
        [ordered]@{
            area = 'Concurrent writers on one section'
            reason = 'A true race cannot be driven deterministically from one sequential HTTP client. The version precondition is proved over HTTP (409 StaleVersion, 400 on a negative value); the DbUpdateConcurrencyException mapping is proved by unit tests.'
        }
        [ordered]@{
            area = 'items_json length cap (40,000 characters)'
            reason = 'Covered by the focused domain/unit tests; a request that large adds no HTTP behavior that the item-cap check does not already prove.'
        }
        [ordered]@{
            area = 'Editor and Viewer role differences inside a family'
            reason = 'The Family home read requires membership only, so the three roles read identically. The adapter covers owner, member and a Family account with no family; the role matrix itself is the pre-existing Families access contract with its own tests.'
        }
        [ordered]@{
            area = 'Public Landing surface unchanged'
            reason = 'Proved by the existing Landing read regression tests, which this adapter does not replace.'
        }
        [ordered]@{
            area = 'iconKey media validation'
            reason = 'The icon key runs through the same controller helper as the image key, which is proved for an existing public key, a missing key, a private/ key and a traversal key.'
        }
    )
}

# ---------------------------------------------------------------------------
# Runtime helpers
# ---------------------------------------------------------------------------

function New-E05PublicFile {
    <#
        Writes the guarded synthetic public PNG under the verified public storage
        root. It never overwrites and never deletes: an unrelated file already
        occupying the key fails the run closed instead.
    #>
    [CmdletBinding()]
    param(
        [string]$StorageRootPath,
        [string]$RelativeKey
    )

    $result = [ordered]@{
        Key = $RelativeKey
        FullPath = $null
        Created = $false
        Exists = $false
        SignatureOk = $false
        Problems = @()
    }

    if ([string]::IsNullOrWhiteSpace($StorageRootPath)) {
        $result.Problems += 'the verified storage root path is empty'
        return $result
    }

    $root = Resolve-FullPath $StorageRootPath
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        $result.Problems += 'the verified storage root does not exist'
        return $result
    }

    if ($RelativeKey.StartsWith('private/', [System.StringComparison]::OrdinalIgnoreCase)) {
        $result.Problems += 'the synthetic public file must not use the private sibling directory'
        return $result
    }

    $normalized = $RelativeKey.Replace('\', '/').TrimStart('/')
    $full = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($root, $normalized))
    if (-not $full.StartsWith($root + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        $result.Problems += 'the synthetic public file would fall outside the verified storage root'
        return $result
    }
    $result.FullPath = $full

    $directory = [System.IO.Path]::GetDirectoryName($full)
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    }

    if (Test-Path -LiteralPath $full -PathType Leaf) {
        $existing = [System.IO.File]::ReadAllBytes($full)
        $result.Exists = $true
        if ($existing.Length -ge 8 -and $existing[0] -eq 0x89 -and $existing[1] -eq 0x50 -and $existing[2] -eq 0x4E -and $existing[3] -eq 0x47) {
            $result.SignatureOk = $true
        }
        else {
            $result.Problems += 'an unrelated file already occupies the synthetic public key'
        }
        return $result
    }

    $bytes = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==')
    [System.IO.File]::WriteAllBytes($full, $bytes)
    $result.Created = $true
    $result.Exists = $true
    $result.SignatureOk = ($bytes.Length -ge 8 -and $bytes[0] -eq 0x89 -and $bytes[1] -eq 0x50 -and $bytes[2] -eq 0x4E -and $bytes[3] -eq 0x47)

    return $result
}

function Resolve-E05PlanReference {
    <#
        Resolves in-plan references such as '{write.create-x.id}' against the
        responses already collected by the current plan, so a later check can
        address a row an earlier check in the same plan created. An unknown or
        empty reference resolves to the empty string, which makes the affected
        check fail loudly instead of silently aiming at a placeholder id.
    #>
    [CmdletBinding()]
    param(
        [AllowEmptyString()][string]$Template,
        [hashtable]$Responses
    )

    if ([string]::IsNullOrEmpty($Template)) { return $Template }
    if (-not $Template.Contains('{')) { return $Template }

    return [regex]::Replace($Template, '\{([A-Za-z0-9_.\-]+)\}', {
            param([System.Text.RegularExpressions.Match]$match)

            # The check name itself may contain dots, so the reference is split
            # at its LAST dot: everything before it is the check name and the
            # remainder is the property path inside that check's parsed body.
            $reference = $match.Groups[1].Value
            $dot = $reference.LastIndexOf('.')
            if ($dot -lt 1) { return '' }

            $name = $reference.Substring(0, $dot)
            if (-not $Responses.ContainsKey($name)) { return '' }

            $value = $Responses[$name]
            foreach ($segment in $reference.Substring($dot + 1).Split('.')) {
                if ($null -eq $value) { return '' }
                if ($value -is [System.Collections.IDictionary]) { $value = $value[$segment] }
                else { $value = $value.$segment }
            }
            if ($null -eq $value) { return '' }
            return [string]$value
        })
}

function Invoke-E05PlannedChecks {
    <#
        Sends one planned check (GET/POST/PUT/DELETE with an optional JSON body)
        and evaluates it with the L1 outcome evaluator. Unlike the L1 planner this
        one sends the request body, treats a transport failure as that check's own
        failure, and never aborts the batch: the whole point of the run is to see
        every result. Parsed responses are returned separately so the caller can
        capture the ids and versions the later stages need without printing a
        response body.
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
        # A check may name a value produced by an earlier check in the same plan
        # ('{write.create-x.id}'), so the plan order is the dependency order.
        if ([string]$check.Path -match '\{') {
            $check.Path = Resolve-E05PlanReference -Template ([string]$check.Path) -Responses $responses
        }
        if ($check.ContainsKey('Body') -and $null -ne $check.Body -and ([string]$check.Body -match '\{')) {
            $check.Body = Resolve-E05PlanReference -Template ([string]$check.Body) -Responses $responses
        }

        $requestBody = $null
        if ($check.ContainsKey('Body') -and $null -ne $check.Body) { $requestBody = [string]$check.Body }

        $status = 0
        $bodyText = ''
        $transportError = $null

        try {
            $response = Invoke-SanadHttpRequest -Client $Client -Method $check.Method -Url "$Url$($check.Path)" -Body $requestBody -Token $check.Token
            $status = [int]$response.Status
            $bodyText = [string]$response.Body
        }
        catch {
            $transportError = Get-TrimmedRedactedLine $_.Exception.Message
        }

        if ($transportError) {
            $responses[$check.Name] = $null
            $results += [ordered]@{
                name             = $check.Name
                method           = $check.Method
                path             = $check.Path
                actor            = $check.Actor
                expected         = $check.Expected
                actual           = "transport-failure: $transportError"
                passed           = $false
                problems         = @("the request did not complete: $transportError")
                traceId          = $null
                traceCorrelation = 'no-trace-id'
                localLocation    = $null
            }
            continue
        }

        $parsed = ConvertFrom-SafeJson $bodyText
        $responses[$check.Name] = $parsed

        $traceId = $null
        if ($null -ne $parsed -and $parsed.PSObject.Properties.Name -contains 'traceId') { $traceId = [string]$parsed.traceId }
        $location = $null
        if ($traceId) { $location = Get-TraceLocation -TraceId $traceId -LogLines (Get-CapturedLogLines -Path $LogPath) }

        # A broken assertion must fail its own check, never the batch.
        try {
            $results += Get-CheckOutcome -Check $check -Status $status -BodyText $bodyText -TraceId $traceId -Location $location
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

function Invoke-E05SqlChecks {
    <#
        Runs one read-only psql readback per planned SQL check and evaluates it with
        the same outcome evaluator the HTTP checks use, so an SQL failure is
        reported like any other check instead of aborting the run.
    #>
    [CmdletBinding()]
    param(
        [string]$PsqlPath,
        [hashtable]$Connection,
        [hashtable[]]$Plan,
        [string]$Tag
    )

    $results = @()

    foreach ($check in $Plan) {
        $run = Invoke-F7Psql -PsqlPath $PsqlPath -Connection $Connection -Sql $check.Sql -Tag $Tag -ExpectJson

        if (-not $run.Ok) {
            $summary = Get-TrimmedRedactedLine $run.ErrorSummary
            $results += [ordered]@{
                name             = $check.Name
                method           = 'SQL'
                path             = $check.Path
                actor            = $check.Actor
                expected         = $check.Expected
                actual           = "psql-failure: $summary"
                passed           = $false
                problems         = @("the read-only readback did not run: $summary")
                traceId          = $null
                traceCorrelation = 'no-trace-id'
                localLocation    = $null
            }
            continue
        }

        try {
            $results += Get-CheckOutcome -Check $check -Status 200 -BodyText $run.Raw -TraceId $null -Location $null
        }
        catch {
            $assertionError = 'the readback assertion threw: ' + (Get-TrimmedRedactedLine $_.Exception.Message)
            $results += [ordered]@{
                name             = $check.Name
                method           = 'SQL'
                path             = $check.Path
                actor            = $check.Actor
                expected         = $check.Expected
                actual           = $assertionError
                passed           = $false
                problems         = @($assertionError)
                traceId          = $null
                traceCorrelation = 'no-trace-id'
                localLocation    = $null
            }
        }
    }

    return $results
}

# ---------------------------------------------------------------------------

if ($e05LibraryOnly) { return }

# ---------------------------------------------------------------------------
# Main flow
# ---------------------------------------------------------------------------

$repoRoot = Get-RepositoryRoot
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'AGENTS.md') -PathType Leaf)) {
    throw 'Run this script from the Sanad API repository checkout.'
}

$head = Get-PinnedHead $repoRoot
$uri = Assert-LoopbackBaseUrl $e05BaseUrl
$baseUrl = "$($uri.Scheme)://$($uri.Authority)"

$apiDirectory = Join-Path $repoRoot 'src/API/Sanad.API'
$assemblyCandidate = $e05ApiAssemblyPath
if ([string]::IsNullOrWhiteSpace($assemblyCandidate)) {
    $assemblyCandidate = Join-Path $apiDirectory 'bin/Release/net10.0/Sanad.API.dll'
}
$assemblyPath = Resolve-FullPath $assemblyCandidate
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw 'The built API assembly was not found. Build Release first (dotnet build Sanad.slnx -c Release).'
}

$definition = Get-E05FixtureDefinition
$routes = Get-E05Route
$verificationEnvironment = Get-LocalFixtureEnvironment -Password $e05FixturePassword -EnableSeed
$serveEnvironment = Get-LocalFixtureEnvironment -Password $e05FixturePassword

$unitOnly = Get-E05UnitOnlyCoverage
$httpChecks = @()
$sqlChecks = @()
$failureNotes = @()

# 1. Read-only target verification and the fail-closed fixture guard. Nothing is
#    written, no API is started and no HTTP check runs before this passes.
$preflight = Invoke-TargetVerification -RepoRoot $repoRoot -AssemblyPath $assemblyPath -Environment $verificationEnvironment -ApiDirectory $apiDirectory
$connectionSource = Resolve-F7ConnectionStringSource -RepoRoot $repoRoot
$connection = ConvertFrom-F7PostgresConnectionString $connectionSource.Value
$psqlPath = Get-F7PsqlPath
$guard = Test-F7FixtureTarget -VerificationPassed $preflight.passed -Report $preflight.report -Connection $connection -PsqlPath $psqlPath

$refusals = @()
if (-not $preflight.passed) { $refusals += 'the read-only target verification did not pass' }
if (-not $guard.Ok) { $refusals += @($guard.Problems) }
if ([string]$connection.Database -eq 'SanadBrunoTestDb') { $refusals += 'the resolved database is SanadBrunoTestDb, which this adapter never touches' }
if ([string]$connection.Database -ne 'SanadDb') { $refusals += "the resolved database is not the persistent local SanadDb" }
if (@($connection.Problems).Count -gt 0) { $refusals += @($connection.Problems) }

if ($refusals.Count -gt 0) {
    $pending = @()
    foreach ($module in @($preflight.report.modules)) {
        if ([int]$module.pendingMigrations -ne 0) {
            $pending += "$($module.module)=$($module.pendingMigrations) pending"
        }
    }

    $notes = @(
        'The read-only target verification or the fixture guard refused the run: no fixture SQL, no public file, no API start and no HTTP or readback check ran, and nothing was written.'
    )
    if ($pending.Count -gt 0) {
        $notes += ('Modules with pending migrations: ' + ($pending -join '; ') + '. The phase migration must be applied by the release owner before this adapter can execute.')
    }

    $output = [ordered]@{
        workflow           = 'e05-family-home-verification'
        head               = $head
        baseUrl            = $baseUrl
        databaseSourceName = $connectionSource.Source
        resolvedDatabase   = [string]$connection.Database
        phase              = 'target-verification'
        apiStarted         = $false
        passed             = $false
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
        fixtures           = $null
        publicFile         = $null
        httpChecks         = @()
        sqlChecks          = @()
        unitOnlyCoverage   = $unitOnly
        results            = [ordered]@{
            httpExecuted = 0
            sqlExecuted  = 0
            unitOnlyAreas = $unitOnly.Count
            total        = 0
            passed       = 0
            failed       = 0
        }
        notes              = $notes
        limitation         = @(
            'Startup migrations stay disabled for every process this script starts; migration state is only read, so the E05 family-home migration must already be applied locally for this adapter to run.',
            'The fixture identities, the synthetic catalog services and the pinned section rows are retained in the persistent local database; nothing is deleted.',
            'Each successful run appends the sections it creates and publishes through the real routes, so retained rows accumulate by design.'
        )
    }

    $output | ConvertTo-Json -Depth 10
    exit 1
}

$publicFile = New-E05PublicFile -StorageRootPath ([string]$preflight.report.storageRootPath) -RelativeKey $routes.PublicMediaKey
if (@($publicFile.Problems).Count -gt 0 -or -not $publicFile.Exists) {
    $failureNotes += 'the synthetic public media file could not be prepared'
    $failureNotes += @($publicFile.Problems)
}

# 2. INSERT-only, guarded fixtures, then a read-only readback.
$fixtures = [ordered]@{
    provisioned = $false
    state       = $null
    problems    = @()
}

if ($failureNotes.Count -eq 0) {
    $provision = Invoke-F7Psql -PsqlPath $psqlPath -Connection $connection -Sql (Get-E05FixtureSql) -Tag 'e05-fixtures'
    if (-not $provision.Ok) {
        $fixtures.problems = @((Get-TrimmedRedactedLine $provision.ErrorSummary))
        $failureNotes += 'the fixture provisioning failed; no API was started and no HTTP check ran'
    }
    else {
        $stateRun = Invoke-F7Psql -PsqlPath $psqlPath -Connection $connection -Sql (Get-E05StateQuery) -Tag 'e05-state' -ExpectJson
        if (-not $stateRun.Ok) {
            $fixtures.problems = @((Get-TrimmedRedactedLine $stateRun.ErrorSummary))
            $failureNotes += 'the fixture readback failed; no API was started and no HTTP check ran'
        }
        else {
            $fixtures.provisioned = $true
            $fixtures.state = $stateRun.Value

            $stateProblems = @()
            if ([int]$stateRun.Value.identities -ne 5) { $stateProblems += 'the five fixture identities are not all present' }
            if ([int]$stateRun.Value.accounts -ne 5) { $stateProblems += 'the five fixture accounts are not all present' }
            if ([int]$stateRun.Value.contentAdminAccountType -ne 6) { $stateProblems += 'the fixture admin does not carry the ContentAdmin account type' }
            if ([int]$stateRun.Value.familyRows -ne 1) { $stateProblems += 'the synthetic family row is missing' }
            if ([int]$stateRun.Value.familyMemberRows -ne 1) { $stateProblems += 'the synthetic family member row is missing' }
            if ([int]$stateRun.Value.serviceRows -ne 2) { $stateProblems += 'the synthetic catalog services are missing' }
            if (-not [bool]$stateRun.Value.activeServiceIsActive) { $stateProblems += 'the active synthetic service is not active' }
            if ([bool]$stateRun.Value.inactiveServiceIsActive) { $stateProblems += 'the inactive synthetic service is active' }
            if ([int]$stateRun.Value.pinnedSectionRows -ne 4) { $stateProblems += 'the four pinned fixture sections are not all present' }
            if ([int]$stateRun.Value.familyHomeTables -ne 2) { $stateProblems += 'the family-home tables are not both present' }

            $fixtures.problems = $stateProblems
            if ($stateProblems.Count -gt 0) { $failureNotes += 'the fixture readback did not match the expected state' }
        }
    }
}

$client = $null
$process = $null
$tokens = @{}
$responses = @{}
$state = @{}
$apiStarted = $false
$logDirectory = $null
$apiLogPaths = @()

try {
    if ($failureNotes.Count -eq 0) {
        if (Test-BaseUrlReachable -Url $baseUrl) {
            throw "the E05 port is already occupied by another listener; stop the process this adapter owns before running again."
        }

        $logDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sanad-e05-verify-' + [Guid]::NewGuid().ToString('N'))
        [System.IO.Directory]::CreateDirectory($logDirectory) | Out-Null
        $apiLogPaths = @((Join-Path $logDirectory 'api.stdout.log'), (Join-Path $logDirectory 'api.stderr.log'))

        $process = Start-FixtureApiProcess -AssemblyPath $assemblyPath -WorkingDirectory $apiDirectory -Url $baseUrl -Environment $serveEnvironment -OutputLogPath $apiLogPaths[0] -ErrorLogPath $apiLogPaths[1]
        $apiStarted = $true

        if (-not (Wait-ForEndpoint -Url $baseUrl -TimeoutSeconds 90 -Process $process)) {
            $logLines = @(Get-CapturedLogLines -Path $apiLogPaths)
            $lastLogLine = $logLines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1
            if ($lastLogLine) { $failureNotes += ('Last captured API log line: ' + (Get-TrimmedRedactedLine $lastLogLine)) }
            throw 'the Development API did not become reachable within 90 seconds'
        }

        $client = New-SanadHttpClient

        # 3. Session preflight for the five dedicated fixture identities. A failure
        #    here is a failure note, not an abort: the affected checks then report
        #    their own unauthenticated status.
        foreach ($actor in @('content-admin', 'family-owner', 'family-viewer', 'family-none', 'family-denied')) {
            $fixtureActor = $definition.Actors[$actor]
            try {
                $tokens[$actor] = Get-FixtureToken -Client $client -Url $baseUrl `
                    -Email $fixtureActor.Email -Password $e05FixturePassword -DeviceName $fixtureActor.DeviceName
            }
            catch {
                $failureNotes += "the session preflight failed for $actor"
            }
        }

        $state.LinkId = ''
        $state.RandomGuid = [Guid]::NewGuid().ToString()

        # 4. Stage one: boundaries, paging, request validation, the Family read of
        #    the pinned fixtures and the draft creations.
        $stageOne = Invoke-E05PlannedChecks -Client $client -Url $baseUrl `
            -Plan (New-E05CheckPlanStageOne -Tokens $tokens -RandomGuid $state.RandomGuid -PublicMediaKey $routes.PublicMediaKey) `
            -LogPath $apiLogPaths
        $httpChecks += $stageOne.Results
        foreach ($key in $stageOne.Responses.Keys) { $responses[$key] = $stageOne.Responses[$key] }

        $created = $responses['write.create-quick-links']
        if ($null -ne $created -and $null -ne $created.id) {
            $state.LinkId = [string]$created.id
            $state.LinkVersion = [int]$created.version
        }
        else {
            $state.LinkId = [Guid]::Empty.ToString()
            $state.LinkVersion = 0
            $failureNotes += 'the quick-link draft creation returned no usable section id, so the lifecycle stages ran against a placeholder id'
        }

        foreach ($pair in @(
                @('EmptyBannerId', 'write.create-empty-banner-draft'),
                @('BannerWithoutImageId', 'write.create-banner-without-image'),
                @('BannerWithPublicKeyId', 'write.create-banner-with-public-key'),
                @('InactiveFeaturedId', 'write.create-inactive-featured'),
                @('InactiveTargetId', 'write.create-inactive-target-link'))) {
            $captured = $responses[$pair[1]]
            if ($null -ne $captured -and $null -ne $captured.id) { $state[$pair[0]] = [string]$captured.id }
            else {
                $state[$pair[0]] = [Guid]::Empty.ToString()
                $failureNotes += "the creation check $($pair[1]) returned no usable section id"
            }
        }

        # 5. Stage two: search, version preconditions, updates and publication.
        $stageTwo = Invoke-E05PlannedChecks -Client $client -Url $baseUrl `
            -Plan (New-E05CheckPlanStageTwo -Tokens $tokens -State $state) `
            -LogPath $apiLogPaths
        $httpChecks += $stageTwo.Results
        foreach ($key in $stageTwo.Responses.Keys) { $responses[$key] = $stageTwo.Responses[$key] }

        $published = $responses['write.publish-quick-links']
        if ($null -ne $published -and $null -ne $published.version) {
            $state.LinkPublishedVersion = [int]$published.version
        }
        else {
            $state.LinkPublishedVersion = [int]$state.LinkVersion + 1
            $failureNotes += 'the quick-link publication returned no usable version, so the twin stages used the expected version'
        }

        # 6. Stage three: the twin lifecycle, deletion semantics, archived
        #    immutability, visibility and reorder.
        $stageThree = Invoke-E05PlannedChecks -Client $client -Url $baseUrl `
            -Plan (New-E05CheckPlanStageThree -Tokens $tokens -State $state) `
            -LogPath $apiLogPaths
        $httpChecks += $stageThree.Results
        foreach ($key in $stageThree.Responses.Keys) { $responses[$key] = $stageThree.Responses[$key] }

        $twinOne = $responses['write.update-published-creates-twin']
        if ($null -ne $twinOne -and $null -ne $twinOne.id) { $state.TwinOneId = [string]$twinOne.id }
        else {
            $state.TwinOneId = [Guid]::Empty.ToString()
            $failureNotes += 'the first twin creation returned no usable section id'
        }

        $twinTwo = $responses['write.update-published-creates-second-twin']
        if ($null -ne $twinTwo -and $null -ne $twinTwo.id) { $state.TwinTwoId = [string]$twinTwo.id }
        else {
            $state.TwinTwoId = [Guid]::Empty.ToString()
            $failureNotes += 'the second twin creation returned no usable section id'
        }

        # 7. Stage four: visibility and reorder, which address the second
        #    published twin and therefore run once its id is known.
        $stageFour = Invoke-E05PlannedChecks -Client $client -Url $baseUrl `
            -Plan (New-E05CheckPlanStageFour -Tokens $tokens -State $state) `
            -LogPath $apiLogPaths
        $httpChecks += $stageFour.Results
        foreach ($key in $stageFour.Responses.Keys) { $responses[$key] = $stageFour.Responses[$key] }
    }
}
catch {
    $failureNotes += (Get-TrimmedRedactedLine $_.Exception.Message)
}
finally {
    # Every created session is revoked, and exactly the one API instance this
    # adapter started is stopped, even when a check failed.
    if ($client) {
        try {
            $logoutPlan = @(New-E05LogoutChecks -Tokens $tokens)
            if ($logoutPlan.Count -gt 0) {
                $logout = Invoke-E05PlannedChecks -Client $client -Url $baseUrl -Plan $logoutPlan -LogPath $apiLogPaths
                $httpChecks += $logout.Results
            }
        }
        catch {
            $failureNotes += ('the logout checks could not run: ' + (Get-TrimmedRedactedLine $_.Exception.Message))
        }
        $client.Dispose()
    }

    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        Wait-Process -Id $process.Id -Timeout 15 -ErrorAction SilentlyContinue
    }
}

# 7. Read-only readbacks over the same persistent target.
if ($fixtures.provisioned -and -not [string]::IsNullOrWhiteSpace([string]$state.TwinOneId)) {
    try {
        $sqlPlan = New-E05SqlCheckPlan -DeletedTwinId ([string]$state.TwinOneId) -PublishedId ([string]$state.LinkId) `
            -PublishedTwinId ([string]$state.TwinTwoId) -FamilyId $definition.Family.Id
        $sqlChecks += Invoke-E05SqlChecks -PsqlPath $psqlPath -Connection $connection -Plan $sqlPlan -Tag 'e05-readback'
    }
    catch {
        $failureNotes += ('the read-only readbacks could not run: ' + (Get-TrimmedRedactedLine $_.Exception.Message))
    }
}

$allChecks = @($httpChecks) + @($sqlChecks)
$passedChecks = @($allChecks | Where-Object { $_.passed }).Count
$failedChecks = @($allChecks | Where-Object { -not $_.passed }).Count
$overallPassed = ($failureNotes.Count -eq 0) -and ($failedChecks -eq 0) -and ($allChecks.Count -gt 0)

$notes = @($failureNotes)
if ($allChecks.Count -eq 0) { $notes += 'No check executed, so nothing is claimed as verified.' }

$output = [ordered]@{
    workflow           = 'e05-family-home-verification'
    head               = $head
    baseUrl            = $baseUrl
    databaseSourceName = $connectionSource.Source
    resolvedDatabase   = [string]$connection.Database
    phase              = 'http-checks'
    apiStarted         = $apiStarted
    passed             = $overallPassed
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
    fixtures           = $fixtures
    publicFile         = $publicFile
    httpChecks         = @($httpChecks)
    sqlChecks          = @($sqlChecks)
    unitOnlyCoverage   = $unitOnly
    results            = [ordered]@{
        httpExecuted  = @($httpChecks).Count
        sqlExecuted   = @($sqlChecks).Count
        unitOnlyAreas = $unitOnly.Count
        total         = $allChecks.Count
        passed        = $passedChecks
        failed        = $failedChecks
    }
    notes              = $notes
    limitation         = @(
        'Startup migrations stay disabled for every process this script starts; migration state is only read, so the E05 family-home migration must already be applied locally for this adapter to run.',
        'The write path of the Admin list search is proved over an ASCII token; matching Arabic copy is proved by the focused unit test instead.',
        'Trace correlation is reported as unavailable when no captured local log line carries the trace id of a failed check.',
        'The fixture identities, the synthetic catalog services and the pinned section rows are retained in the persistent local database; nothing is deleted, and each successful run appends the sections it creates and publishes through the real routes.'
    )
}

$output | ConvertTo-Json -Depth 12

if (-not $overallPassed) { exit 1 }
