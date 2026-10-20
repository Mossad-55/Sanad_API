#requires -Version 5.1
<#
.SYNOPSIS
    F7 ratings visibility and moderation (A8/A15) - local HTTP verification
    adapter over the L1 fixture library.

.DESCRIPTION
    A bounded adapter, not a second harness. It reuses the L1 library's helpers
    (target verification, process environment overrides, API start/stop, HTTP
    request, outcome evaluation, login preflight and trace correlation) and adds
    only what F7 needs:

      1. The L1 read-only preflight report is re-checked fail-closed: all nine
         module contexts must target the loopback PersistentLocal Development
         database with no pending migration, and no live Paymux/SMS/SMTP/Push
         input may be configured. Nothing is written before that gate passes.
      2. INSERT-only, guarded, idempotent SQL through the local psql client adds
         seven dedicated synthetic identities (SuperAdmin-less operational admin,
         ContentAdmin, FinanceAdmin, two Care Home owners, one caregiver and one
         family), one caregiver profile with three ratings, one family with a
         completed caregiver booking, two facilities with three ratings each
         backed by three Care Home bookings.
      3. Starts (or reuses) the Development API once against the verified target
         on the F7 loopback port.
      4. Normalises the fixture rating state, then proves the pinned F7 behavior:
         unauthenticated 401, role isolation on both admin routes and the owner
         route, the owner flag lifecycle with a repeated flag writing nothing,
         admin hide resolving the open flag with attribution, the stored
         caregiver aggregate and every aggregate consumer agreeing after a hide
         and after a restore, a family edit that cannot unhide the rating, the
         owner page aggregate, unknown-id 404s, blank/over-long reason 400s and
         the pinned 409 conflicts.
      5. Logs out every session this run created.

    Safety properties:
      - The base URL must be a loopback HTTP(S) listener; there is no remote
        fallback and no way to pass one.
      - Live provider configuration is cleared for every process this script
        starts; migrations stay disabled (`Database:ApplyMigrationsOnStartup=false`).
      - Fixture SQL is INSERT-only, guarded and idempotent, and ASCII only.
      - Tokens, credentials, connection strings and payloads are never printed;
        a failed check reports only status, safe business code, field names,
        trace id and a redacted local log line.
      - Offline tests dot-source this file with -LibraryOnly and exercise only
        the pure helpers; no API, database or HTTP call occurs there.

.PARAMETER BaseUrl
    Loopback HTTP(S) base URL of the API. Defaults to the F7 port 5247, which
    does not collide with the other local adapters (5235/5237/5241/5243). Also
    used as ASPNETCORE_URLS when the script starts the API.

.PARAMETER ApiAssemblyPath
    Optional. Built API assembly. Defaults to the Release output.

.PARAMETER FixturePassword
    Synthetic fixture password whose hash is copied from the shared L1 Minimal
    fixture identity. Never a real credential and never emitted in output.

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
    [string]$BaseUrl = 'http://localhost:5247',

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
$f7LibraryOnly      = [bool]$LibraryOnly
$f7BaseUrl          = $BaseUrl
$f7ApiAssemblyPath  = $ApiAssemblyPath
$f7FixturePassword  = $FixturePassword
$f7SkipApiStart     = [bool]$SkipApiStart
$f7SkipFixtureWrite = [bool]$SkipFixtureWrite

. (Join-Path $PSScriptRoot 'Invoke-LocalFixtureVerification.ps1') -LibraryOnly

# ---------------------------------------------------------------------------
# F7 fixture definition
# ---------------------------------------------------------------------------

function Get-F7FixtureDefinition {
    <#
        Seven dedicated synthetic identities plus the fixed references they need.

        Account types (Identity AccountType): Family = 1, MedicalCaregiver = 2,
        ContentAdmin = 6, SupportAdmin = 7, CareHomeOwner = 8, FinanceAdmin = 9.
        UserStatus Active = 2. CaregiverStatus Active = 4. CaregiverType
        Medical = 1. CaregiverAvailability Available = 1. FamilyRole Owner = 1.
        FamilyRelationshipType Other = 99. CareHomeStatus Approved = 5.
        CareHomeBookingStatus Accepted = 3. CareHomeBookingPaymentStatus Paid = 2.
        BookingStatus Completed = 5. BookingShiftType HomeVisit = 1.

        The password hash is copied from the shared L1 Minimal fixture identity,
        so no credential is ever stored in this repository; the guard fails
        closed when that identity (and therefore the L1 Minimal seed) is absent.

        Rating days are fixed past UTC days so ordering, paging and the owner
        page are deterministic and a family re-rating really moves the row's
        updated instant past its created instant:
          caregiver ratings  2026-02-01/02/03  stars 5/4/3  (average 4.00)
          Care Home ratings  2026-03-01/02/03  stars 5/4/3  (average 4.00)
        Hiding the 5-star caregiver rating leaves 3.50; hiding the 3-star one
        leaves 4.50; hiding the 5-star Care Home rating leaves 3.50. No chosen
        set contains a rounding tie, so the stored caregiver aggregate
        (MidpointRounding.ToEven) and the SQL-computed aggregates
        (ROUND(...,2), half away from zero) must agree exactly.
    #>
    [CmdletBinding()]
    param()

    $caregiverRatings = @(
        [ordered]@{ Key = 'caregiverRating1'; Short = 'cgr1'; Id = '019a2f7b-0008-7000-8000-000000000001'; BookingKey = 'caregiverBooking1'; Stars = 5; Day = '2026-02-01'; Review = 'F7 verification caregiver rating one retained content' }
        [ordered]@{ Key = 'caregiverRating2'; Short = 'cgr2'; Id = '019a2f7b-0008-7000-8000-000000000002'; BookingKey = 'caregiverBooking2'; Stars = 4; Day = '2026-02-02'; Review = 'F7 verification caregiver rating two retained content' }
        [ordered]@{ Key = 'caregiverRating3'; Short = 'cgr3'; Id = '019a2f7b-0008-7000-8000-000000000003'; BookingKey = 'caregiverBooking3'; Stars = 3; Day = '2026-02-03'; Review = 'F7 verification caregiver rating three retained content' }
    )
    $careHomeRatings = @(
        [ordered]@{ Key = 'careHomeRating1'; Short = 'chr1'; Id = '019a2f7b-0009-7000-8000-000000000001'; BookingKey = 'careHomeBooking1'; Stars = 5; Day = '2026-03-01'; Review = 'F7 verification care home rating one retained content' }
        [ordered]@{ Key = 'careHomeRating2'; Short = 'chr2'; Id = '019a2f7b-0009-7000-8000-000000000002'; BookingKey = 'careHomeBooking2'; Stars = 4; Day = '2026-03-02'; Review = 'F7 verification care home rating two retained content' }
        [ordered]@{ Key = 'careHomeRating3'; Short = 'chr3'; Id = '019a2f7b-0009-7000-8000-000000000003'; BookingKey = 'careHomeBooking3'; Stars = 3; Day = '2026-03-03'; Review = 'F7 verification care home rating three retained content' }
    )

    return [ordered]@{
        SupportAdmin  = [ordered]@{
            Email         = 'support.admin.f7@test.sanad.local'
            UserId        = '019a2f7b-0001-7000-8000-000000000001'
            PhoneNumber   = '+201000000071'
            EnglishName   = 'F7 Support Operator'
            ArabicName    = 'F7 Support Operator (arabic name slot)'
            AccountType   = 7
            AccountTypeId = '019a2f7b-0002-7000-8000-000000000001'
        }
        ContentAdmin  = [ordered]@{
            Email         = 'content.admin.f7@test.sanad.local'
            UserId        = '019a2f7b-0001-7000-8000-000000000002'
            PhoneNumber   = '+201000000072'
            EnglishName   = 'F7 Content Operator'
            ArabicName    = 'F7 Content Operator (arabic name slot)'
            AccountType   = 6
            AccountTypeId = '019a2f7b-0002-7000-8000-000000000002'
        }
        FinanceAdmin  = [ordered]@{
            Email         = 'finance.admin.f7@test.sanad.local'
            UserId        = '019a2f7b-0001-7000-8000-000000000003'
            PhoneNumber   = '+201000000073'
            EnglishName   = 'F7 Finance Operator'
            ArabicName    = 'F7 Finance Operator (arabic name slot)'
            AccountType   = 9
            AccountTypeId = '019a2f7b-0002-7000-8000-000000000003'
        }
        Owner         = [ordered]@{
            Email         = 'owner.f7@test.sanad.local'
            UserId        = '019a2f7b-0001-7000-8000-000000000004'
            PhoneNumber   = '+201000000074'
            EnglishName   = 'F7 Facility Owner'
            ArabicName    = 'F7 Facility Owner (arabic name slot)'
            AccountType   = 8
            AccountTypeId = '019a2f7b-0002-7000-8000-000000000004'
        }
        Owner2        = [ordered]@{
            Email         = 'owner2.f7@test.sanad.local'
            UserId        = '019a2f7b-0001-7000-8000-000000000005'
            PhoneNumber   = '+201000000075'
            EnglishName   = 'F7 Second Facility Owner'
            ArabicName    = 'F7 Second Facility Owner (arabic name slot)'
            AccountType   = 8
            AccountTypeId = '019a2f7b-0002-7000-8000-000000000005'
        }
        Caregiver     = [ordered]@{
            Email         = 'caregiver.f7@test.sanad.local'
            UserId        = '019a2f7b-0001-7000-8000-000000000006'
            PhoneNumber   = '+201000000076'
            EnglishName   = 'F7 Verification Caregiver Unique'
            ArabicName    = 'F7 Verification Caregiver Unique (arabic name slot)'
            AccountType   = 2
            AccountTypeId = '019a2f7b-0002-7000-8000-000000000006'
        }
        Family        = [ordered]@{
            Email         = 'family.f7@test.sanad.local'
            UserId        = '019a2f7b-0001-7000-8000-000000000007'
            PhoneNumber   = '+201000000077'
            EnglishName   = 'F7 Family Reviewer'
            ArabicName    = 'F7 Family Reviewer (arabic name slot)'
            AccountType   = 1
            AccountTypeId = '019a2f7b-0002-7000-8000-000000000007'
        }
        CaregiverProfile = [ordered]@{
            Id              = '019a2f7b-0003-7000-8000-000000000001'
            Type            = 1
            Status          = 4
            Availability    = 1
            BaselineStars   = @(5, 4, 3)
            BaselineAverage = '4.00'
            BaselineCount   = 3
        }
        Facility1     = [ordered]@{
            Id      = '019a2f7b-0004-7000-8000-000000000001'
            OwnerId = '019a2f7b-0001-7000-8000-000000000004'
        }
        Facility2     = [ordered]@{
            Id      = '019a2f7b-0004-7000-8000-000000000002'
            OwnerId = '019a2f7b-0001-7000-8000-000000000005'
        }
        FamilyActive  = [ordered]@{
            Id   = '019a2f7b-0005-7000-8000-000000000001'
            Name = 'F7 verification family'
        }
        CaregiverBookings = [ordered]@{
            caregiverBooking1 = '019a2f7b-0006-7000-8000-000000000001'
            caregiverBooking2 = '019a2f7b-0006-7000-8000-000000000002'
            caregiverBooking3 = '019a2f7b-0006-7000-8000-000000000003'
        }
        CareHomeBookings = [ordered]@{
            careHomeBooking1 = [ordered]@{ Id = '019a2f7b-0007-7000-8000-000000000001'; MerchantReference = 'F7CHVERIFY0001' }
            careHomeBooking2 = [ordered]@{ Id = '019a2f7b-0007-7000-8000-000000000002'; MerchantReference = 'F7CHVERIFY0002' }
            careHomeBooking3 = [ordered]@{ Id = '019a2f7b-0007-7000-8000-000000000003'; MerchantReference = 'F7CHVERIFY0003' }
        }
        CaregiverRatings = $caregiverRatings
        CareHomeRatings  = $careHomeRatings
        PasswordSource   = 'content.admin@test.sanad.local'
    }
}

function Get-F7FixtureSql {
    <#
        Idempotent, INSERT-only fixture provisioning. Every statement is guarded
        by its own primary key: a rerun inserts nothing, and nothing is ever
        updated, deleted or reset. The shared fixture password hash is copied,
        never invented.
    #>
    [CmdletBinding()]
    param()

    $definition = Get-F7FixtureDefinition
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
    foreach ($actor in @($definition.SupportAdmin, $definition.ContentAdmin, $definition.FinanceAdmin,
                         $definition.Owner, $definition.Owner2, $definition.Caregiver, $definition.Family)) {
        $identitySql += ($identityInsert -f $actor.UserId, $actor.ArabicName, $actor.EnglishName,
            $actor.PhoneNumber, $actor.Email, $source, $actor.AccountTypeId, $actor.AccountType)
        $identitySql += "`n"
    }

    $profile = $definition.CaregiverProfile
    $family = $definition.FamilyActive
    $facility1 = $definition.Facility1
    $facility2 = $definition.Facility2
    $familyUserId = $definition.Family.UserId
    $caregiverUserId = $definition.Caregiver.UserId
    $booking1 = $definition.CaregiverBookings['caregiverBooking1']

    $caregiverSql = @"
-- One active, visible caregiver profile whose stored aggregate matches the
-- three visible F7 ratings (4.00 over 3) so the consumers start in agreement.
INSERT INTO caregivers.caregivers
    (id, user_id, type, status, availability, average_rating, reviews_count,
     show_profile, show_rating, share_location, show_phone, medical_home_visit_price,
     created_on_utc, updated_on_utc)
SELECT '$($profile.Id)', '$caregiverUserId', $($profile.Type), $($profile.Status), $($profile.Availability),
       $($profile.BaselineAverage), $($profile.BaselineCount), true, true, false, false, 250.00, now(), now()
WHERE NOT EXISTS (SELECT 1 FROM caregivers.caregivers WHERE id = '$($profile.Id)');

INSERT INTO families.families (id, owner_user_id, name, created_on_utc, updated_on_utc, deleted_on_utc)
SELECT '$($family.Id)', '$familyUserId', '$($family.Name)', now(), now(), NULL
WHERE NOT EXISTS (SELECT 1 FROM families.families WHERE id = '$($family.Id)');

INSERT INTO families.family_members (family_id, user_id, added_by_user_id, relationship_type, role, joined_on_utc)
SELECT '$($family.Id)', '$familyUserId', '$familyUserId', 99, 1, now()
WHERE NOT EXISTS (
    SELECT 1 FROM families.family_members
    WHERE family_id = '$($family.Id)' AND user_id = '$familyUserId');

-- One completed caregiver booking inside the fixture family, so the family
-- re-rating route (which rejects a hidden rating becoming visible) has a real
-- eligible booking to edit.
INSERT INTO families.bookings
    (id, family_id, created_by_user_id, elderly_id, caregiver_id, caregiver_type, shift_type,
     booking_date, start_time, end_time, service_address, price_base_fee,
     price_platform_fee_percentage, price_platform_fee_amount, price_total_payable_amount,
     price_currency, status, created_on_utc, updated_on_utc, completed_on_utc)
SELECT '$booking1', '$($family.Id)', '$familyUserId', '$familyUserId', '$($profile.Id)', 2, 1,
       DATE '2026-02-01', TIME '10:00', TIME '11:00', 'F7 verification service address slot', 250.00,
       0.10, 25.00, 275.00, 'EGP', 5, now(), now(), TIMESTAMPTZ '2026-02-01T11:00:00Z'
WHERE NOT EXISTS (SELECT 1 FROM families.bookings WHERE id = '$booking1');

INSERT INTO care_homes.facilities
    (id, owner_user_id, status, submitted_revision_id, approved_revision_id, version, created_on_utc, updated_on_utc)
SELECT '$($facility1.Id)', '$($facility1.OwnerId)', 5, NULL, NULL, 1, now(), now()
WHERE NOT EXISTS (SELECT 1 FROM care_homes.facilities WHERE id = '$($facility1.Id)');

INSERT INTO care_homes.facilities
    (id, owner_user_id, status, submitted_revision_id, approved_revision_id, version, created_on_utc, updated_on_utc)
SELECT '$($facility2.Id)', '$($facility2.OwnerId)', 5, NULL, NULL, 1, now(), now()
WHERE NOT EXISTS (SELECT 1 FROM care_homes.facilities WHERE id = '$($facility2.Id)');
"@

    # caregiver ratings: one per fixture booking (the rating table has a unique
    # booking index), all visible (moderation_state = 0).
    $caregiverRatingSql = ''
    foreach ($rating in $definition.CaregiverRatings) {
        $bookingId = $definition.CaregiverBookings[$rating.BookingKey]
        $caregiverRatingSql += @"
INSERT INTO caregivers.caregiver_ratings
    (id, caregiver_id, booking_id, family_id, created_by_user_id, updated_by_user_id,
     stars, review_text, created_on_utc, updated_on_utc, moderation_state)
SELECT '$($rating.Id)', '$($profile.Id)', '$bookingId', '$($family.Id)', '$familyUserId', '$familyUserId',
       $($rating.Stars), '$($rating.Review)', TIMESTAMPTZ '$($rating.Day)T10:00:00Z',
       TIMESTAMPTZ '$($rating.Day)T10:00:00Z', 0
WHERE NOT EXISTS (SELECT 1 FROM caregivers.caregiver_ratings WHERE id = '$($rating.Id)');

"@
    }

    # Care Home bookings: care_homes.ratings.booking_id is a NOT NULL foreign
    # key, so each rating needs its own booking row (MerchantReference is unique).
    $careHomeSql = ''
    foreach ($rating in $definition.CareHomeRatings) {
        $booking = $definition.CareHomeBookings[$rating.BookingKey]
        $careHomeSql += @"
INSERT INTO care_homes.bookings
    (id, "FacilityId", "FamilyUserId", "FamilyId", "ElderlyId", "RoomTypeId", "StartDate", "EndDate",
     "Status", "PaymentStatus", "BaseAmount", "PlatformFeeAmount", "TaxAmount", "TotalAmount",
     "ChargeRuleVersion", "ElderlyArabicName", "ElderlyEnglishName", "ElderlyAge",
     "ResponsibleContactName", "MerchantReference", "CheckoutHoldUntilUtc", "Version",
     "CreatedOnUtc", "UpdatedOnUtc", earliest_arrival_utc, family_check_in_confirmed_on_utc,
     family_check_in_confirmed_by)
SELECT '$($booking.Id)', '$($facility1.Id)', '$familyUserId', '$($family.Id)', '$familyUserId',
       '019a2f7b-000a-7000-8000-000000000001', DATE '$($rating.Day)', DATE '$($rating.Day)',
       3, 2, 1000.00, 100.00, 0.00, 1100.00, 1, 'F7 elderly arabic name slot',
       'F7 elderly english name slot', 79, 'F7 responsible contact name slot', '$($booking.MerchantReference)',
       TIMESTAMPTZ '$($rating.Day)T08:00:00Z', 1, now(), now(),
       TIMESTAMPTZ '$($rating.Day)T08:00:00Z', TIMESTAMPTZ '$($rating.Day)T09:00:00Z', '$familyUserId'
WHERE NOT EXISTS (SELECT 1 FROM care_homes.bookings WHERE id = '$($booking.Id)');

INSERT INTO care_homes.ratings
    (id, booking_id, care_home_id, family_id, created_by_user_id, updated_by_user_id,
     stars, review_text, created_on_utc, updated_on_utc, moderation_state)
SELECT '$($rating.Id)', '$($booking.Id)', '$($facility1.Id)', '$($family.Id)', '$familyUserId', '$familyUserId',
       $($rating.Stars), '$($rating.Review)', TIMESTAMPTZ '$($rating.Day)T10:00:00Z',
       TIMESTAMPTZ '$($rating.Day)T10:00:00Z', 0
WHERE NOT EXISTS (SELECT 1 FROM care_homes.ratings WHERE id = '$($rating.Id)');

"@
    }

    return @"
\set ON_ERROR_STOP on
BEGIN;

-- Fail closed when the shared L1 Minimal fixture identity is missing: a fixture
-- identity without a usable password hash could never authenticate.
DO `$f7guard`$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM identity.users
        WHERE email = '$source' AND password_hash IS NOT NULL)
    THEN
        RAISE EXCEPTION 'F7 fixture guard: the shared fixture password hash is missing';
    END IF;
END
`$f7guard`$;

$identitySql
$caregiverSql

$caregiverRatingSql
$careHomeSql
COMMIT;
"@
}

function Get-F7StateQuery {
    <#
        Read-only readback of the whole F7 fixture and of every fact the checks
        assert. Every value is cast to text in SQL so a comparison in the report
        is exact (no culture-dependent number formatting) and a missing row reads
        as an empty string rather than as a JSON null.
    #>
    [CmdletBinding()]
    param()

    $definition = Get-F7FixtureDefinition
    $source = $definition.PasswordSource
    $profile = $definition.CaregiverProfile
    $familyId = $definition.FamilyActive.Id
    $facility1 = $definition.Facility1
    $facility2 = $definition.Facility2
    $familyUserId = $definition.Family.UserId
    $caregiverUserId = $definition.Caregiver.UserId
    $supportAdminId = $definition.SupportAdmin.UserId
    $booking1 = $definition.CaregiverBookings['caregiverBooking1']

    $actorPairs = ''
    foreach ($actorName in @('SupportAdmin', 'ContentAdmin', 'FinanceAdmin', 'Owner', 'Owner2', 'Caregiver', 'Family')) {
        $actor = $definition.$actorName
        $prefix = $actorName.Substring(0, 1).ToLowerInvariant() + $actorName.Substring(1)
        $actorPairs += @"
        '${prefix}UserId', (SELECT coalesce(id::text, '') FROM identity.users WHERE email = '$($actor.Email)'),
        '${prefix}AccountTypes', (SELECT coalesce(string_agg(account.account_type::text, ','), '') FROM identity.user_accounts AS account JOIN identity.users AS u ON u.id = account.user_id WHERE u.email = '$($actor.Email)'),
        '${prefix}IdentityStatus', (SELECT coalesce(status::text, '') FROM identity.users WHERE email = '$($actor.Email)'),

"@
    }

    $caregiverRatingPairs = ''
    foreach ($rating in $definition.CaregiverRatings) {
        $caregiverRatingPairs += @"
        '$($rating.Short)Exists', (SELECT coalesce((count(*) = 1)::text, '') FROM caregivers.caregiver_ratings WHERE id = '$($rating.Id)'),
        '$($rating.Short)Stars', (SELECT coalesce(stars::text, '') FROM caregivers.caregiver_ratings WHERE id = '$($rating.Id)'),
        '$($rating.Short)State', (SELECT coalesce(moderation_state::text, '') FROM caregivers.caregiver_ratings WHERE id = '$($rating.Id)'),
        '$($rating.Short)HiddenByUserId', (SELECT coalesce(hidden_by_user_id::text, '') FROM caregivers.caregiver_ratings WHERE id = '$($rating.Id)'),
        '$($rating.Short)HiddenOnPresent', (SELECT coalesce((hidden_on_utc IS NOT NULL)::text, '') FROM caregivers.caregiver_ratings WHERE id = '$($rating.Id)'),
        '$($rating.Short)HideReason', (SELECT coalesce(hide_reason, '') FROM caregivers.caregiver_ratings WHERE id = '$($rating.Id)'),
        '$($rating.Short)ReviewText', (SELECT coalesce(review_text, '') FROM caregivers.caregiver_ratings WHERE id = '$($rating.Id)'),
        '$($rating.Short)HideReasonLength', (SELECT coalesce(length(hide_reason), 0)::text FROM caregivers.caregiver_ratings WHERE id = '$($rating.Id)'),
        '$($rating.Short)ReviewTextLength', (SELECT coalesce(length(review_text), 0)::text FROM caregivers.caregiver_ratings WHERE id = '$($rating.Id)'),
        '$($rating.Short)UpdatedAfterCreated', (SELECT coalesce((updated_on_utc > created_on_utc)::text, '') FROM caregivers.caregiver_ratings WHERE id = '$($rating.Id)'),

"@
    }

    $careHomeRatingPairs = ''
    foreach ($rating in $definition.CareHomeRatings) {
        $careHomeRatingPairs += @"
        '$($rating.Short)Exists', (SELECT coalesce((count(*) = 1)::text, '') FROM care_homes.ratings WHERE id = '$($rating.Id)'),
        '$($rating.Short)Stars', (SELECT coalesce(stars::text, '') FROM care_homes.ratings WHERE id = '$($rating.Id)'),
        '$($rating.Short)State', (SELECT coalesce(moderation_state::text, '') FROM care_homes.ratings WHERE id = '$($rating.Id)'),
        '$($rating.Short)HiddenByUserId', (SELECT coalesce(hidden_by_user_id::text, '') FROM care_homes.ratings WHERE id = '$($rating.Id)'),
        '$($rating.Short)HiddenOnPresent', (SELECT coalesce((hidden_on_utc IS NOT NULL)::text, '') FROM care_homes.ratings WHERE id = '$($rating.Id)'),
        '$($rating.Short)HideReason', (SELECT coalesce(hide_reason, '') FROM care_homes.ratings WHERE id = '$($rating.Id)'),
        '$($rating.Short)ReviewText', (SELECT coalesce(review_text, '') FROM care_homes.ratings WHERE id = '$($rating.Id)'),
        '$($rating.Short)HideReasonLength', (SELECT coalesce(length(hide_reason), 0)::text FROM care_homes.ratings WHERE id = '$($rating.Id)'),
        '$($rating.Short)ReviewTextLength', (SELECT coalesce(length(review_text), 0)::text FROM care_homes.ratings WHERE id = '$($rating.Id)'),
        '$($rating.Short)OpenFlagCount', (SELECT count(*)::text FROM care_homes.rating_flags WHERE rating_id = '$($rating.Id)' AND status = 0),
        '$($rating.Short)FlagRowCount', (SELECT count(*)::text FROM care_homes.rating_flags WHERE rating_id = '$($rating.Id)'),
        '$($rating.Short)LastFlagStatus', (SELECT coalesce(status::text, '') FROM care_homes.rating_flags WHERE rating_id = '$($rating.Id)' ORDER BY created_on_utc DESC, id DESC LIMIT 1),
        '$($rating.Short)LastFlagResolution', (SELECT coalesce(resolution::text, '') FROM care_homes.rating_flags WHERE rating_id = '$($rating.Id)' ORDER BY created_on_utc DESC, id DESC LIMIT 1),
        '$($rating.Short)LastFlagResolvedBy', (SELECT coalesce(resolved_by_user_id::text, '') FROM care_homes.rating_flags WHERE rating_id = '$($rating.Id)' ORDER BY created_on_utc DESC, id DESC LIMIT 1),
        '$($rating.Short)LastFlagResolvedOnPresent', (SELECT coalesce((resolved_on_utc IS NOT NULL)::text, '') FROM care_homes.rating_flags WHERE rating_id = '$($rating.Id)' ORDER BY created_on_utc DESC, id DESC LIMIT 1),
        '$($rating.Short)LastFlagReason', (SELECT coalesce(reason, '') FROM care_homes.rating_flags WHERE rating_id = '$($rating.Id)' ORDER BY created_on_utc DESC, id DESC LIMIT 1),
        '$($rating.Short)LastFlagReasonLength', (SELECT coalesce(length(reason), 0)::text FROM care_homes.rating_flags WHERE rating_id = '$($rating.Id)' ORDER BY created_on_utc DESC, id DESC LIMIT 1),
        '$($rating.Short)LastFlagOwnerUserId', (SELECT coalesce(owner_user_id::text, '') FROM care_homes.rating_flags WHERE rating_id = '$($rating.Id)' ORDER BY created_on_utc DESC, id DESC LIMIT 1),

"@
    }

    $identityPairs = @"
        'passwordSourceUserId', (SELECT coalesce(id::text, '') FROM identity.users WHERE email = '$source'),
        'minimalFixturePresentCount', (SELECT count(*)::text FROM identity.users WHERE email = '$source'),

        'caregiverId', (SELECT coalesce(id::text, '') FROM caregivers.caregivers WHERE id = '$($profile.Id)'),
        'caregiverUserId', (SELECT coalesce(user_id::text, '') FROM caregivers.caregivers WHERE id = '$($profile.Id)'),
        'caregiverType', (SELECT coalesce(type::text, '') FROM caregivers.caregivers WHERE id = '$($profile.Id)'),
        'caregiverStatus', (SELECT coalesce(status::text, '') FROM caregivers.caregivers WHERE id = '$($profile.Id)'),
        'caregiverAvailability', (SELECT coalesce(availability::text, '') FROM caregivers.caregivers WHERE id = '$($profile.Id)'),
        'caregiverShowProfile', (SELECT coalesce(show_profile::text, '') FROM caregivers.caregivers WHERE id = '$($profile.Id)'),
        'caregiverShowRating', (SELECT coalesce(show_rating::text, '') FROM caregivers.caregivers WHERE id = '$($profile.Id)'),
        'caregiverStoredAverage', (SELECT coalesce(to_char(average_rating, 'FM990.00'), '') FROM caregivers.caregivers WHERE id = '$($profile.Id)'),
        'caregiverStoredCount', (SELECT coalesce(reviews_count::text, '') FROM caregivers.caregivers WHERE id = '$($profile.Id)'),
        'caregiverVisibleAverage', (SELECT coalesce(to_char(round(avg(stars)::numeric, 2), 'FM990.00'), '0.00') FROM caregivers.caregiver_ratings WHERE caregiver_id = '$($profile.Id)' AND moderation_state = 0),
        'caregiverVisibleCount', (SELECT count(*)::text FROM caregivers.caregiver_ratings WHERE caregiver_id = '$($profile.Id)' AND moderation_state = 0),
        'caregiverHiddenRatingCount', (SELECT count(*)::text FROM caregivers.caregiver_ratings WHERE caregiver_id = '$($profile.Id)' AND moderation_state = 1),
        'caregiverRatingRowCount', (SELECT count(*)::text FROM caregivers.caregiver_ratings WHERE caregiver_id = '$($profile.Id)'),
        'caregiverActionRowCount', (SELECT count(*)::text FROM caregivers.caregiver_rating_moderation_actions),
        'caregiverLastActionType', (SELECT coalesce(action::text, '') FROM caregivers.caregiver_rating_moderation_actions ORDER BY occurred_on_utc DESC, id DESC LIMIT 1),
        'caregiverLastActionActor', (SELECT coalesce(actor_user_id::text, '') FROM caregivers.caregiver_rating_moderation_actions ORDER BY occurred_on_utc DESC, id DESC LIMIT 1),
        'caregiverLastActionRatingId', (SELECT coalesce(rating_id::text, '') FROM caregivers.caregiver_rating_moderation_actions ORDER BY occurred_on_utc DESC, id DESC LIMIT 1),
        'caregiverLastActionReason', (SELECT coalesce(reason, '') FROM caregivers.caregiver_rating_moderation_actions ORDER BY occurred_on_utc DESC, id DESC LIMIT 1),
        'caregiverLastActionReasonLength', (SELECT coalesce(length(reason), 0)::text FROM caregivers.caregiver_rating_moderation_actions ORDER BY occurred_on_utc DESC, id DESC LIMIT 1),
"@

    $facilityPairs = @"
        'familyId', (SELECT coalesce(id::text, '') FROM families.families WHERE id = '$familyId'),
        'familyOwnerUserId', (SELECT coalesce(owner_user_id::text, '') FROM families.families WHERE id = '$familyId'),
        'familyDeletedOnPresent', (SELECT coalesce((deleted_on_utc IS NOT NULL)::text, '') FROM families.families WHERE id = '$familyId'),
        'familyMemberCount', (SELECT count(*)::text FROM families.family_members WHERE family_id = '$familyId'),
        'familyBookingId', (SELECT coalesce(id::text, '') FROM families.bookings WHERE id = '$booking1'),
        'familyBookingStatus', (SELECT coalesce(status::text, '') FROM families.bookings WHERE id = '$booking1'),
        'familyBookingFamilyId', (SELECT coalesce(family_id::text, '') FROM families.bookings WHERE id = '$booking1'),
        'familyBookingCaregiverId', (SELECT coalesce(caregiver_id::text, '') FROM families.bookings WHERE id = '$booking1'),

        'facility1Id', (SELECT coalesce(id::text, '') FROM care_homes.facilities WHERE id = '$($facility1.Id)'),
        'facility1OwnerUserId', (SELECT coalesce(owner_user_id::text, '') FROM care_homes.facilities WHERE id = '$($facility1.Id)'),
        'facility2Id', (SELECT coalesce(id::text, '') FROM care_homes.facilities WHERE id = '$($facility2.Id)'),
        'facility2OwnerUserId', (SELECT coalesce(owner_user_id::text, '') FROM care_homes.facilities WHERE id = '$($facility2.Id)'),
        'facility1VisibleRatingCount', (SELECT count(*)::text FROM care_homes.ratings WHERE care_home_id = '$($facility1.Id)' AND moderation_state = 0),
        'facility1HiddenRatingCount', (SELECT count(*)::text FROM care_homes.ratings WHERE care_home_id = '$($facility1.Id)' AND moderation_state = 1),
        'facility1RatingRowCount', (SELECT count(*)::text FROM care_homes.ratings WHERE care_home_id = '$($facility1.Id)'),
        'facility1OpenFlagCount', (SELECT count(*)::text FROM care_homes.rating_flags WHERE status = 0 AND rating_id IN (SELECT id FROM care_homes.ratings WHERE care_home_id = '$($facility1.Id)')),
        'facility1FlagRowCount', (SELECT count(*)::text FROM care_homes.rating_flags WHERE rating_id IN (SELECT id FROM care_homes.ratings WHERE care_home_id = '$($facility1.Id)')),
        'facility2RatingRowCount', (SELECT count(*)::text FROM care_homes.ratings WHERE care_home_id = '$($facility2.Id)'),
        'careHomeActionRowCount', (SELECT count(*)::text FROM care_homes.rating_moderation_actions),
        'careHomeActionLastType', (SELECT coalesce(action::text, '') FROM care_homes.rating_moderation_actions ORDER BY occurred_on_utc DESC, id DESC LIMIT 1),
        'careHomeActionLastActor', (SELECT coalesce(actor_user_id::text, '') FROM care_homes.rating_moderation_actions ORDER BY occurred_on_utc DESC, id DESC LIMIT 1),
        'careHomeActionLastRatingId', (SELECT coalesce(rating_id::text, '') FROM care_homes.rating_moderation_actions ORDER BY occurred_on_utc DESC, id DESC LIMIT 1),
        'careHomeActionLastReason', (SELECT coalesce(reason, '') FROM care_homes.rating_moderation_actions ORDER BY occurred_on_utc DESC, id DESC LIMIT 1),
        'careHomeActionLastReasonLength', (SELECT coalesce(length(reason), 0)::text FROM care_homes.rating_moderation_actions ORDER BY occurred_on_utc DESC, id DESC LIMIT 1),
        'careHomeFlagRowCount', (SELECT count(*)::text FROM care_homes.rating_flags),
        'careHomeOpenFlagRowCount', (SELECT count(*)::text FROM care_homes.rating_flags WHERE status = 0),
"@

    $tailPairs = @"
        'supportAdminFixtureActor', '$supportAdminId',
        'familyFixtureActor', '$familyUserId',
        'caregiverFixtureActor', '$caregiverUserId'
"@

    # json_build_object accepts at most 100 arguments, so the readback is built
    # from several bounded objects concatenated with the jsonb operator. Every
    # argument pair is one list entry, which keeps a single missing comma from
    # ever reaching the local client.
    $pairs = New-Object System.Collections.Generic.List[string]
    foreach ($block in @($actorPairs, $identityPairs, $caregiverRatingPairs,
                         $facilityPairs, $careHomeRatingPairs, $tailPairs)) {
        foreach ($line in ($block -split "`r?`n")) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $pairs.Add($line.Trim().TrimEnd(','))
        }
    }

    $blocks = @()
    for ($index = 0; $index -lt $pairs.Count; $index += 40) {
        $last = [Math]::Min($index + 39, $pairs.Count - 1)
        $slice = @($pairs[$index..$last])
        $blocks += ("json_build_object(`n" + ($slice -join ",`n") + "`n)::jsonb")
    }

    return "SELECT (`n    " + ($blocks -join "`n    || ") + "`n);"
}

# ---------------------------------------------------------------------------
# Fail-closed guards (pure)
# ---------------------------------------------------------------------------

function ConvertFrom-F7PostgresConnectionString {
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

function Resolve-F7ConnectionStringSource {
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

function Test-F7FixtureTarget {
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

# ---------------------------------------------------------------------------
# Assert helpers (return closures the L1 evaluator can invoke)
# ---------------------------------------------------------------------------

function New-F7AssertSet {
    <#
        Status-only assertions. These are deliberately plain scriptblocks (not
        .GetNewClosure()): a closure resolves commands and functions in a scope
        that reaches only the global scope, so the script-scope library helper
        Get-SafeErrorSummary would throw inside one.
    #>
    [CmdletBinding()]
    param()

    return [ordered]@{
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

function New-F7CodeAssert {
    <# Asserts HTTP status plus the published business code of a problem details body. #>
    [CmdletBinding()]
    param([string]$Code)

    return ({
        param($status, $body)
        # No Get-SafeErrorSummary call here on purpose: this assertion is bound
        # with .GetNewClosure() to capture $Code, and a closure cannot reach a
        # script-scope helper. The L1 Get-CheckOutcome wrapper still records the
        # safe error summary whenever the HTTP status itself does not match.
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

function New-F7PageAssert {
    <#
        Asserts a page shape: the exact published property sets of the page and
        of every item, that no item exposes a forbidden field, the optional
        numeric aggregates, the optional newest-first star order, an optional
        excluded rating id, and any per-item or per-page check the caller
        supplies.
    #>
    [CmdletBinding()]
    param(
        [int]$Page = -1,
        [int]$PageSize = -1,
        [int]$TotalCount = -1,
        [double]$AverageStars = -1,
        [int]$RatingCount = -1,
        [string[]]$PageProperty = @(),
        [switch]$ExactPageProperties,
        [string[]]$ItemProperty = @(),
        [switch]$ExactItemProperties,
        [string[]]$ItemNullProperty = @(),
        [string[]]$ForbiddenToken = @(),
        [string]$ExpectedItemStarOrder,
        [string]$ForbiddenItemId,
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
        if ($AverageStars -ge 0 -and [math]::Abs([double]$body.averageStars - $AverageStars) -gt 0.000001) {
            $problems += "averageStars is $($body.averageStars), expected $AverageStars"
        }
        if ($RatingCount -ge 0 -and [int]$body.ratingCount -ne $RatingCount) {
            $problems += "ratingCount is $($body.ratingCount), expected $RatingCount"
        }
        if ($ExpectedItemStarOrder) {
            $order = @($items | ForEach-Object { [string]$_.stars })
            if (($order -join ',') -ne $ExpectedItemStarOrder) {
                $problems += "the item star order is '$($order -join ',')', expected '$ExpectedItemStarOrder'"
            }
        }

        $pageNames = @($body.PSObject.Properties.Name)
        if ($ExactPageProperties -and ($pageNames.Count -ne $PageProperty.Count)) {
            $problems += "the page publishes $($pageNames.Count) properties ($($pageNames -join ','))"
        }
        foreach ($name in $PageProperty) {
            if ($pageNames -notcontains $name) { $problems += "the page misses the published property $name" }
        }
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
            foreach ($name in $ItemNullProperty) {
                if ($names -notcontains $name) { $problems += "an item misses the published property $name" }
                elseif ($null -ne $item.$name) { $problems += "an item publishes a non-null $name" }
            }
            foreach ($token in $ForbiddenToken) {
                foreach ($name in $names) {
                    if ($name.ToLowerInvariant().Contains($token)) { $problems += "an item publishes a forbidden field: $name" }
                }
            }
            if ($ForbiddenItemId) {
                foreach ($name in @('ratingId', 'id')) {
                    if ($names -contains $name -and [string]$item.$name -eq $ForbiddenItemId) {
                        $problems += "the page still publishes the rating $ForbiddenItemId"
                    }
                }
            }
            if ($ItemCheck) {
                foreach ($problem in @(& $ItemCheck $item)) { if ($problem) { $problems += $problem } }
            }
        }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()
}

function New-F7JsonAssert {
    <#
        Generic body assertion: passes the parsed body to the caller's check,
        which returns a list of problems.
    #>
    [CmdletBinding()]
    param([scriptblock]$Check, [string]$ActualText)

    return ({
        param($status, $body)
        if ($null -eq $body) {
            return @{ Actual = "status=$status"; Problems = @('response body is not valid JSON') }
        }
        $problems = @()
        foreach ($problem in @(& $Check $body)) { if ($problem) { $problems += $problem } }
        $actual = "status=$status"
        if ($ActualText) { $actual = "status=$status $ActualText" }
        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()
}

function New-F7StateExpectation {
    <#
        Compares published readback keys with their expected text values. A key
        the readback does not carry reads as an empty string, so a missing key
        is reported rather than silently skipped. -Agree states a relational
        expectation ("these two readback values must be equal"), which is how
        the stored caregiver aggregate is held against the SQL-computed one.

        Accepts the (status, body) pair the step runner passes to every
        assertion; the readback's status is always 0.
    #>
    [CmdletBinding()]
    param(
        [hashtable]$Expect = @{},
        [hashtable]$Agree = @{},
        [hashtable]$CounterDelta = @{},
        [hashtable]$CounterEquals = @{},
        [hashtable]$Capture = @{}
    )

    return ({
        param($status, $body)
        $problems = @()
        foreach ($key in @($Expect.Keys)) {
            $actual = [string]$body.$key
            if ($actual -ne [string]$Expect[$key]) {
                $problems += "$key is '$actual', expected '$($Expect[$key])'"
            }
        }
        foreach ($key in @($Agree.Keys)) {
            $left = [string]$body.$key
            $right = [string]$body.($Agree[$key])
            if ($left -ne $right) {
                $problems += "$key is '$left' but $($Agree[$key]) is '$right'"
            }
        }
        foreach ($key in @($CounterDelta.Keys)) {
            $captureKey = [string]@($CounterDelta[$key])[0]
            $delta = [long]@($CounterDelta[$key])[1]
            $base = [long]$Capture[$captureKey]
            $value = [long]$body.$key
            if ($value -ne ($base + $delta)) {
                $problems += "$key is $value, expected $($base + $delta) (captured $captureKey=$base plus $delta)"
            }
        }
        foreach ($key in @($CounterEquals.Keys)) {
            $captureKey = [string]$CounterEquals[$key]
            $expected = [string]$Capture[$captureKey]
            $actual = [string]$body.$key
            if ($actual -ne $expected) {
                $problems += "$key is '$actual', expected the captured $captureKey='$expected'"
            }
        }
        return $problems
    }).GetNewClosure()
}

# ---------------------------------------------------------------------------
# Step builders
# ---------------------------------------------------------------------------

function Get-F7Route {
    [CmdletBinding()]
    param()

    return @{
        AdminCaregiverRatings = '/api/v1/admin/caregivers/ratings'
        AdminCareHomeRatings  = '/api/v1/admin/care-homes/ratings'
        OwnerRatings          = '/api/v1/care-homes/facilities/mine/ratings'
        Caregivers            = '/api/v1/caregivers'
        FamilyTopRated        = '/api/v1/family/caregiver-ratings/top-10'
        FamilyCaregiverRating = '/api/v1/family/caregiver-ratings/bookings'
        Logout                = '/api/v1/auth/sessions/logout'
    }
}

function New-F7HttpStep {
    <#
        One planned HTTP step. The runner sends Method/Body/Token to Path under
        the loopback base URL and evaluates the response with the L1
        Get-CheckOutcome, so an F7 step and an F6 check share one report shape.
    #>
    [CmdletBinding()]
    param(
        [string]$Name,
        [string]$Method,
        [string]$Path,
        [string]$Actor,
        [AllowNull()][string]$Token,
        [AllowNull()][string]$Body = $null,
        [int]$ExpectedStatus,
        [string]$Expected,
        [scriptblock]$Assert
    )

    return @{
        Kind           = 'http'
        Name           = $Name
        Method         = $Method
        Path           = $Path
        Actor          = $Actor
        Token          = $Token
        Body           = $Body
        ExpectedStatus = $ExpectedStatus
        Expected       = $Expected
        Assert         = $Assert
        Sql            = $null
        Capture        = @{}
    }
}

function New-F7SqlStep {
    <#
        One planned readback step. The runner runs the local psql client, parses
        the JSON the query prints and evaluates it through the same
        Get-CheckOutcome path with status 0, so a readback check and an HTTP
        check land in one uniform report. -Capture promotes selected readback
        values into the shared capture table: that is how a later assertion
        states a delta on an append-only table (moderation actions and flags are
        never deleted, and a crashed earlier run may already have grown them)
        without hard-coding a count.
    #>
    [CmdletBinding()]
    param(
        [string]$Name,
        [string]$Tag,
        [string]$Expected,
        [scriptblock]$Assert,
        [AllowNull()][string]$Sql = $null,
        [hashtable]$Capture = @{}
    )

    # Every readback step reads the same published state, so the caller only
    # names the step: the query is filled in here when it is not supplied.
    if ([string]::IsNullOrWhiteSpace($Sql)) { $Sql = Get-F7StateQuery }

    return @{
        Kind           = 'sql'
        Name           = $Name
        Method         = 'SQL'
        Path           = "psql:$Tag"
        Actor          = 'psql'
        Token          = $null
        Body           = $null
        ExpectedStatus = 0
        Expected       = $Expected
        Assert         = $Assert
        Sql            = $Sql
        Capture        = $Capture
    }
}

# ---------------------------------------------------------------------------
# The F7 step plan
# ---------------------------------------------------------------------------

function New-F7ConsumerAssert {
    <#
        One assertion over a consumer of the stored caregiver aggregate. Every
        consumer is checked against the same pair of numbers, so "after a hide or
        a restore the stored aggregate and every SQL-computed aggregate agree"
        is proven per consumer instead of asserted once.

        The expected values are bound when the closure is created, because the
        whole plan is evaluated after it is built: a mutable expectation would
        read its final value for every step.
    #>
    [CmdletBinding()]
    param(
        [ValidateSet('profile', 'search', 'earnings', 'topRated')]
        [string]$Kind,
        [double]$Average,
        [int]$Count,
        [string]$CaregiverId,
        [string]$CaregiverName
    )

    return ({
        param($status, $body)
        $problems = @()
        $actual = "status=$status"

        switch ($Kind) {
            'profile' {
                $actual = "status=$status averageRating=$($body.averageRating) reviewsCount=$($body.reviewsCount)"
                if ([string]$body.id -ne $CaregiverId) { $problems += "id is '$($body.id)', expected the fixture caregiver" }
                if ([math]::Abs([double]$body.averageRating - $Average) -gt 0.000001) { $problems += "averageRating is $($body.averageRating), expected $Average" }
                if ([int]$body.reviewsCount -ne $Count) { $problems += "reviewsCount is $($body.reviewsCount), expected $Count" }
                foreach ($forbidden in @('ratings', 'reviews', 'reviewText', 'familyId', 'createdByUserId', 'hiddenOnUtc')) {
                    if ($body.PSObject.Properties.Name -contains $forbidden) { $problems += "the public profile publishes $forbidden" }
                }
            }
            'search' {
                $items = @($body.items)
                $actual = "status=$status items=$($items.Count) totalCount=$($body.totalCount)"
                if ($items.Count -ne 1) { $problems += "the search returned $($items.Count) caregivers, expected the one fixture caregiver" }
                if ([int]$body.totalCount -ne 1) { $problems += "totalCount is $($body.totalCount), expected 1" }
                if ([int]$body.totalPages -ne 1) { $problems += "totalPages is $($body.totalPages), expected 1" }
                if ($items.Count -ge 1) {
                    if ([string]$items[0].id -ne $CaregiverId) { $problems += "the search card is not the fixture caregiver" }
                    if ([math]::Abs([double]$items[0].averageRating - $Average) -gt 0.000001) { $problems += "averageRating is $($items[0].averageRating), expected $Average" }
                    if ([int]$items[0].reviewsCount -ne $Count) { $problems += "reviewsCount is $($items[0].reviewsCount), expected $Count" }
                }
            }
            'earnings' {
                $actual = "status=$status averageRating=$($body.averageRating)"
                if ([string]$body.caregiverId -ne $CaregiverId) { $problems += "caregiverId is '$($body.caregiverId)', expected the fixture caregiver" }
                if ([math]::Abs([double]$body.averageRating - $Average) -gt 0.000001) { $problems += "averageRating is $($body.averageRating), expected $Average" }
                if ($body.PSObject.Properties.Name -contains 'reviewsCount') { $problems += 'the earnings summary publishes a reviewsCount it never had' }
            }
            'topRated' {
                $items = @($body)
                $actual = "status=$status items=$($items.Count)"
                $match = @($items | Where-Object { [string]$_.caregiverId -eq $CaregiverId })
                if ($match.Count -ne 1) {
                    $problems += "the live top-10 read published the fixture caregiver $($match.Count) times, expected once"
                }
                else {
                    if ([math]::Abs([double]$match[0].averageRating - $Average) -gt 0.000001) { $problems += "averageRating is $($match[0].averageRating), expected $Average" }
                    if ([int]$match[0].reviewsCount -ne $Count) { $problems += "reviewsCount is $($match[0].reviewsCount), expected $Count" }
                    if ([string]$match[0].englishFullName -ne $CaregiverName) { $problems += 'the top-10 card is not the fixture caregiver' }
                    if ($null -ne $match[0].avatarUrl) { $problems += 'the fixture caregiver unexpectedly reports an avatar' }
                }
                for ($index = 1; $index -lt $items.Count; $index++) {
                    if ([double]$items[$index].averageRating -gt [double]$items[$index - 1].averageRating) {
                        $problems += 'the top-10 read is not ordered by descending average rating'
                        break
                    }
                }
            }
        }

        return @{ Actual = $actual; Problems = $problems }
    }).GetNewClosure()
}

function New-F7OwnerItemAssert {
    <#
        One assertion over an item of the owner's facility-rating page. The
        expectation is a map from fixture rating short key to the flag status
        (and, for an Open or Resolved flag, the owner's own reason) that this
        particular read must show, so a read can state exactly which ratings it
        expects and nothing else. Every expectation is bound when the closure is
        created because the whole plan is built before any of it runs.
    #>
    [CmdletBinding()]
    param(
        [hashtable]$Expectation,
        [hashtable]$ReviewByRating,
        [hashtable]$ShortByRating
    )

    return ({
        param($item)
        $problems = @()
        $ratingId = [string]$item.ratingId
        if (-not $ReviewByRating.ContainsKey($ratingId)) {
            $problems += "the owner page publishes $ratingId, which is not a fixture rating"
            return $problems
        }

        $short = [string]$ShortByRating[$ratingId]
        if ([string]$item.reviewText -ne [string]$ReviewByRating[$ratingId]) {
            $problems += "reviewText of $short is not the retained fixture content"
        }

        $expected = $Expectation[$short]
        if ($null -eq $expected) {
            $problems += "the owner page published $short, which this read did not expect"
            return $problems
        }

        if ([string]$item.flagStatus -ne [string]$expected.Status) {
            $problems += "flagStatus of $short is '$($item.flagStatus)', expected '$($expected.Status)'"
        }
        if ([string]$expected.Status -eq 'None') {
            if ($null -ne $item.myFlagReason) { $problems += "a rating the owner never flagged still reports a flag reason ($short)" }
            if ($null -ne $item.myFlaggedOnUtc) { $problems += "a rating the owner never flagged still reports a flag instant ($short)" }
        }
        else {
            if ([string]$item.myFlagReason -ne [string]$expected.Reason) {
                $problems += "myFlagReason of $short is not the reason the owner gave"
            }
            if ($null -eq $item.myFlaggedOnUtc) { $problems += "a flagged rating reports no flag instant ($short)" }
        }
        return $problems
    }).GetNewClosure()
}

function New-F7OwnerHistoricalExpectation {
    <#
        The owner's own flag facts as the pre-run readback describes them. The
        flag table is append-only and the fixture rows are retained, so a rating
        this run has not touched yet can already carry a case the owner opened on
        an earlier run of this adapter. The read then asserts what is stored
        (Resolved with the reason the owner actually gave) instead of a "never
        flagged" state only a pristine fixture can still show.
    #>
    [CmdletBinding()]
    param(
        [AllowNull()][object]$Baseline,
        [string]$Short,
        [string]$OwnerUserId
    )

    $none = @{ Status = 'None' }
    if ($null -eq $Baseline) { return $none }

    $lastOwner = [string]$Baseline."${Short}LastFlagOwnerUserId"
    $lastStatus = [string]$Baseline."${Short}LastFlagStatus"
    if ($lastOwner -ne $OwnerUserId -or [string]::IsNullOrWhiteSpace($lastStatus)) { return $none }

    $reason = [string]$Baseline."${Short}LastFlagReason"
    if ($lastStatus -eq '0') { return @{ Status = 'Open'; Reason = $reason } }

    return @{ Status = 'Resolved'; Reason = $reason }
}

function New-F7StepPlan {
    <#
        The whole F7 step plan. It is ordered on purpose: section 0 normalises
        whatever a crashed earlier run left behind, every later section consumes
        what an earlier one proved, and the last section logs every session this
        run created out again. The plan is built from the readback taken before
        it runs, so the normalisation steps are exactly the ones the local
        database actually needs.
    #>
    [CmdletBinding()]
    param(
        [hashtable]$Tokens,
        [AllowNull()][object]$Baseline,
        [hashtable]$Capture,
        [string]$UnknownGuid
    )

    $definition = Get-F7FixtureDefinition
    $routes = Get-F7Route
    $helpers = New-F7AssertSet

    $caregiverAdmin = $routes.AdminCaregiverRatings
    $careHomeAdmin = $routes.AdminCareHomeRatings
    $ownerRoute = $routes.OwnerRatings
    $caregiversRoute = $routes.Caregivers
    $topRatedRoute = $routes.FamilyTopRated
    $ratingRoute = $routes.FamilyCaregiverRating
    $logoutRoute = $routes.Logout

    $supportAdminToken = $Tokens['support-admin']
    $ownerToken = $Tokens['owner']
    $owner2Token = $Tokens['owner2']
    $caregiverToken = $Tokens['caregiver']
    $familyToken = $Tokens['family']

    $caregiverId = $definition.CaregiverProfile.Id
    $caregiverName = $definition.Caregiver.EnglishName
    # The search route takes the name through the query string: it must be
    # escaped once here, because a raw space would leave the request line
    # invalid and the check would fail for the wrong reason.
    $searchQuery = [System.Uri]::EscapeDataString($caregiverName)
    $familyId = $definition.FamilyActive.Id
    $facility1Id = $definition.Facility1.Id
    $facility2Id = $definition.Facility2.Id
    $supportAdminUserId = $definition.SupportAdmin.UserId
    $ownerUserId = $definition.Owner.UserId
    $booking1 = $definition.CaregiverBookings['caregiverBooking1']

    $caregiverRatingId = [ordered]@{}
    $careHomeRatingId = [ordered]@{}
    $caregiverReview = [ordered]@{}
    $careHomeReview = [ordered]@{}
    $caregiverReviewByRating = @{}
    $caregiverBookingByRating = @{}
    $careHomeReviewByRating = @{}
    foreach ($rating in $definition.CaregiverRatings) {
        $caregiverRatingId[$rating.Short] = $rating.Id
        $caregiverReview[$rating.Short] = $rating.Review
        $caregiverReviewByRating[$rating.Id] = $rating.Review
        $caregiverBookingByRating[$rating.Id] = $definition.CaregiverBookings[$rating.BookingKey]
    }
    foreach ($rating in $definition.CareHomeRatings) {
        $careHomeRatingId[$rating.Short] = $rating.Id
        $careHomeReview[$rating.Short] = $rating.Review
        $careHomeReviewByRating[$rating.Id] = $rating.Review
    }

    # The published shapes. Nothing outside these sets may ever appear: no
    # reviewer name, phone, email or avatar in an admin or owner read.
    $pageProperties = @('items', 'page', 'pageSize', 'totalCount')
    $ownerPageProperties = @('items', 'page', 'pageSize', 'totalCount', 'averageStars', 'ratingCount')
    $caregiverItemProperties = @(
        'ratingId', 'caregiverId', 'bookingId', 'familyId', 'stars', 'reviewText', 'createdOnUtc',
        'updatedOnUtc', 'moderationState', 'hiddenByUserId', 'hiddenOnUtc', 'hideReason'
    )
    $careHomeItemProperties = @(
        'ratingId', 'facilityId', 'bookingId', 'familyId', 'stars', 'reviewText', 'createdOnUtc',
        'updatedOnUtc', 'moderationState', 'hiddenByUserId', 'hiddenOnUtc', 'hideReason',
        'unresolvedFlagId', 'unresolvedFlagReason', 'unresolvedFlaggedOnUtc'
    )
    $ownerItemProperties = @('ratingId', 'stars', 'reviewText', 'createdOnUtc', 'flagStatus', 'myFlagReason', 'myFlaggedOnUtc')
    $caregiverForbidden = @('name', 'phone', 'email', 'avatar', 'arabic', 'english', 'reviewer', 'familyuser')
    $ownerForbidden = @('family', 'caregiver', 'user', 'name', 'phone', 'email', 'avatar', 'arabic', 'english', 'booking', 'facility', 'hidden')

    $reason500 = 'a' * 500
    $reason501 = 'a' * 501
    $restoreReason = 'F7 admin restores a rating a previous run left hidden'
    $hideReason = 'F7 admin hides the top caregiver rating'
    $careHomeHideReason = 'F7 admin hide resolves the owner flag'
    $careHomeRestoreReason = 'F7 admin restore after resolving the owner flag'
    $careHomeKeepReason = 'F7 admin keeps the rating visible'
    $flagReason = 'F7 owner flags the second rating'
    $familyEditReview = 'F7 family edit of a hidden rating must not unhide it'
    $emptyBody = '{}'

    $restoreBody = (@{ reason = $restoreReason } | ConvertTo-Json -Compress)
    $hideBody = (@{ reason = $hideReason } | ConvertTo-Json -Compress)
    $careHomeHideBody = (@{ reason = $careHomeHideReason } | ConvertTo-Json -Compress)
    $careHomeRestoreBody = (@{ reason = $careHomeRestoreReason } | ConvertTo-Json -Compress)
    $careHomeKeepBody = (@{ reason = $careHomeKeepReason } | ConvertTo-Json -Compress)
    $flagBody = (@{ reason = $flagReason } | ConvertTo-Json -Compress)
    $flag500Body = (@{ reason = $reason500 } | ConvertTo-Json -Compress)
    $flag501Body = (@{ reason = $reason501 } | ConvertTo-Json -Compress)
    $blankReasonBody = (@{ reason = '' } | ConvertTo-Json -Compress)
    $spaceReasonBody = (@{ reason = '   ' } | ConvertTo-Json -Compress)
    $familyEditBody = (@{ stars = 1; reviewText = $familyEditReview } | ConvertTo-Json -Compress)
    $familyCanonicalBody = (@{ stars = 5; reviewText = $caregiverReview['cgr1'] } | ConvertTo-Json -Compress)

    $caregiverItemCheck = ({
        param($item)
        $problems = @()
        if ([string]$item.caregiverId -ne $caregiverId) { $problems += "caregiverId is '$($item.caregiverId)', expected the fixture caregiver" }
        if ([string]$item.familyId -ne $familyId) { $problems += "familyId is '$($item.familyId)', expected the fixture family" }
        if ([string]$item.moderationState -ne 'visible') { $problems += "moderationState is '$($item.moderationState)', expected 'visible'" }
        if ($null -ne $item.hiddenByUserId) { $problems += 'a visible rating reports a hide actor' }
        if ($null -ne $item.hiddenOnUtc) { $problems += 'a visible rating reports a hide instant' }
        if ($null -ne $item.hideReason) { $problems += 'a visible rating reports a hide reason' }
        if (-not $caregiverReviewByRating.Contains([string]$item.ratingId)) {
            $problems += "ratingId $($item.ratingId) is not a fixture rating"
        }
        else {
            if ([string]$item.reviewText -ne $caregiverReviewByRating[[string]$item.ratingId]) {
                $problems += "reviewText of $($item.ratingId) is not the retained fixture content"
            }
            if ([string]$item.bookingId -ne $caregiverBookingByRating[[string]$item.ratingId]) {
                $problems += "bookingId of $($item.ratingId) is not the fixture booking"
            }
        }
        return $problems
    }).GetNewClosure()

    $careHomeItemCheck = ({
        param($item)
        $problems = @()
        if ([string]$item.facilityId -ne $facility1Id) { $problems += "facilityId is '$($item.facilityId)', expected the fixture facility" }
        if ([string]$item.familyId -ne $familyId) { $problems += "familyId is '$($item.familyId)', expected the fixture family" }
        if ([string]$item.moderationState -ne 'visible') { $problems += "moderationState is '$($item.moderationState)', expected 'visible'" }
        if ($null -ne $item.hiddenByUserId) { $problems += 'a visible rating reports a hide actor' }
        if ($null -ne $item.hiddenOnUtc) { $problems += 'a visible rating reports a hide instant' }
        if ($null -ne $item.hideReason) { $problems += 'a visible rating reports a hide reason' }
        if ($null -ne $item.unresolvedFlagId) { $problems += 'a rating with no open flag reports an unresolved flag' }
        if ($null -ne $item.unresolvedFlagReason) { $problems += 'a rating with no open flag reports a flag reason' }
        if ($null -ne $item.unresolvedFlaggedOnUtc) { $problems += 'a rating with no open flag reports a flag instant' }
        if (-not $careHomeReviewByRating.Contains([string]$item.ratingId)) {
            $problems += "ratingId $($item.ratingId) is not a fixture rating"
        }
        elseif ([string]$item.reviewText -ne $careHomeReviewByRating[[string]$item.ratingId]) {
            $problems += "reviewText of $($item.ratingId) is not the retained fixture content"
        }
        return $problems
    }).GetNewClosure()

    $caregiverHiddenItemCheck = ({
        param($item)
        $problems = @()
        if ([string]$item.moderationState -ne 'hidden') { $problems += "moderationState is '$($item.moderationState)', expected 'hidden'" }
        if ([string]$item.hiddenByUserId -ne $supportAdminUserId) { $problems += "hiddenByUserId is '$($item.hiddenByUserId)', expected the support admin who hid it" }
        if ($null -eq $item.hiddenOnUtc) { $problems += 'a hidden rating reports no hide instant' }
        if ($null -eq $item.hideReason) { $problems += 'a hidden rating reports no hide reason' }
        return $problems
    }).GetNewClosure()

    $careHomeHiddenItemCheck = ({
        param($item)
        $problems = @()
        if ([string]$item.moderationState -ne 'hidden') { $problems += "moderationState is '$($item.moderationState)', expected 'hidden'" }
        if ([string]$item.hiddenByUserId -ne $supportAdminUserId) { $problems += "hiddenByUserId is '$($item.hiddenByUserId)', expected the support admin who hid it" }
        if ($null -eq $item.hiddenOnUtc) { $problems += 'a hidden rating reports no hide instant' }
        if ($null -eq $item.hideReason) { $problems += 'a hidden rating reports no hide reason' }
        if ([string]$item.facilityId -ne $facility1Id) { $problems += "facilityId is '$($item.facilityId)', expected the fixture facility" }
        return $problems
    }).GetNewClosure()

    $ownerVisibleItemCheck = ({
        param($item)
        $problems = @()
        if ([string]$item.flagStatus -ne 'None') { $problems += "flagStatus is '$($item.flagStatus)', expected 'None'" }
        if ($null -ne $item.myFlagReason) { $problems += 'a rating the owner never flagged reports a flag reason' }
        if ($null -ne $item.myFlaggedOnUtc) { $problems += 'a rating the owner never flagged reports a flag instant' }
        return $problems
    }).GetNewClosure()

    $careHomePageContainsFixture = ({
        param($body)
        $problems = @()
        $present = @($body.items | ForEach-Object { [string]$_.ratingId })
        foreach ($expected in @($careHomeRatingId['chr1'], $careHomeRatingId['chr2'], $careHomeRatingId['chr3'])) {
            if ($present -notcontains $expected) {
                $problems += "the unfiltered admin page does not publish the fixture rating $expected"
            }
        }
        return $problems
    }).GetNewClosure()

    $shortByRating = @{}
    foreach ($rating in $definition.CareHomeRatings) {
        $shortByRating[$rating.Id] = $rating.Short
    }

    # -----------------------------------------------------------------------
    # Value-carrying bodies and closures. The whole plan is evaluated after it
    # is built, so every expectation a closure needs is bound here rather than
    # read from a local that a later step would have moved on.
    # -----------------------------------------------------------------------

    $flagRepeatReason = 'F7 owner repeats the flag on the second rating'
    $keepFlagReason = 'F7 owner flags the first rating'
    $keepFlagRepeatReason = 'F7 owner flags the first rating again after the admin kept it'

    $overReasonBody = (@{ reason = $reason501 } | ConvertTo-Json -Compress)
    $reason500Body = (@{ reason = $reason500 } | ConvertTo-Json -Compress)
    $flagRepeatBody = (@{ reason = $flagRepeatReason } | ConvertTo-Json -Compress)
    $keepFlagBody = (@{ reason = $keepFlagReason } | ConvertTo-Json -Compress)
    $keepFlagRepeatBody = (@{ reason = $keepFlagRepeatReason } | ConvertTo-Json -Compress)

    # A hidden Care Home rating, checked against the reason that hid it: the
    # retained content and the resolving attribution must both survive, and no
    # unresolved flag may still hang off it.
    $careHomeHiddenItemCheck = ({
        param($item, $expectedReason)
        $problems = @()
        if ([string]$item.facilityId -ne $facility1Id) { $problems += "facilityId is '$($item.facilityId)', expected the fixture facility" }
        if ([string]$item.familyId -ne $familyId) { $problems += "familyId is '$($item.familyId)', expected the fixture family" }
        if ([string]$item.moderationState -ne 'hidden') { $problems += "moderationState is '$($item.moderationState)', expected 'hidden'" }
        if ([string]$item.hiddenByUserId -ne $supportAdminUserId) { $problems += "hiddenByUserId is '$($item.hiddenByUserId)', expected the support admin who hid it" }
        if ($null -eq $item.hiddenOnUtc) { $problems += 'a hidden rating reports no hide instant' }
        if ([string]$item.hideReason -ne $expectedReason) {
            $problems += "hideReason of $($item.ratingId) is not the 500-character reason that hid it"
        }
        if ($null -ne $item.unresolvedFlagId) { $problems += 'a hidden rating still reports an unresolved flag' }
        if (-not $careHomeReviewByRating.Contains([string]$item.ratingId)) {
            $problems += "ratingId $($item.ratingId) is not a fixture rating"
        }
        elseif ([string]$item.reviewText -ne $careHomeReviewByRating[[string]$item.ratingId]) {
            $problems += "reviewText of $($item.ratingId) is not the retained fixture content"
        }
        return $problems
    }).GetNewClosure()

    $careHomeHiddenLongReasonItemCheck = ({
        param($item)
        return @(& $careHomeHiddenItemCheck $item $reason500)
    }).GetNewClosure()

    # A visible Care Home rating whose owner has an open flag: the unresolved
    # flag summary is published with the owner's reason and no reviewer field.
    $careHomeFlaggedItemCheck = ({
        param($item, $expectedReason)
        $problems = @()
        if ([string]$item.facilityId -ne $facility1Id) { $problems += "facilityId is '$($item.facilityId)', expected the fixture facility" }
        if ([string]$item.familyId -ne $familyId) { $problems += "familyId is '$($item.familyId)', expected the fixture family" }
        if ([string]$item.moderationState -ne 'visible') { $problems += "moderationState is '$($item.moderationState)', expected 'visible'" }
        if ($null -ne $item.hiddenByUserId) { $problems += 'a visible rating reports a hide actor' }
        if ($null -ne $item.hiddenOnUtc) { $problems += 'a visible rating reports a hide instant' }
        if ($null -ne $item.hideReason) { $problems += 'a visible rating reports a hide reason' }
        $flagId = [string]$item.unresolvedFlagId
        if ([string]::IsNullOrWhiteSpace($flagId)) {
            $problems += 'the flagged rating reports no unresolved flag id'
        }
        else {
            $parsedFlagId = [guid]::Empty
            if (-not [guid]::TryParse($flagId, [ref]$parsedFlagId)) { $problems += 'the unresolved flag id is not a GUID' }
        }
        if ([string]$item.unresolvedFlagReason -ne $expectedReason) {
            $problems += "unresolvedFlagReason of $($item.ratingId) is not the owner's flag reason"
        }
        if ($null -eq $item.unresolvedFlaggedOnUtc) { $problems += 'an unresolved flag reports no flag instant' }
        if (-not $careHomeReviewByRating.Contains([string]$item.ratingId)) {
            $problems += "ratingId $($item.ratingId) is not a fixture rating"
        }
        elseif ([string]$item.reviewText -ne $careHomeReviewByRating[[string]$item.ratingId]) {
            $problems += "reviewText of $($item.ratingId) is not the retained fixture content"
        }
        return $problems
    }).GetNewClosure()

    $careHomeFlaggedChr2ItemCheck = ({
        param($item)
        return @(& $careHomeFlaggedItemCheck $item $flagReason)
    }).GetNewClosure()

    # The only open flag on the first rating at this point is the one the owner
    # opened just above, so its reason is the first-flag reason: the repeat
    # reason belongs to the case opened after the admin kept this one.
    $careHomeFlaggedChr1ItemCheck = ({
        param($item)
        return @(& $careHomeFlaggedItemCheck $item $keepFlagReason)
    }).GetNewClosure()

    # The unresolved-flagged-first ordering rule: the only flagged rating of the
    # unfiltered page must be the first row.
    $careHomePageFirstIsFlagged = ({
        param($body)
        $problems = @()
        $items = @($body.items)
        if ($items.Count -eq 0) {
            $problems += 'the unfiltered admin page is empty'
            return $problems
        }
        if ([string]$items[0].ratingId -ne $careHomeRatingId['chr2']) {
            $problems += "the first unfiltered row is $($items[0].ratingId), expected the flagged fixture rating"
        }
        if ($null -eq $items[0].unresolvedFlagId) {
            $problems += 'the first unfiltered row carries no unresolved flag'
        }
        for ($index = 1; $index -lt $items.Count; $index++) {
            if ($null -ne $items[$index].unresolvedFlagId) {
                $problems += "row $index also carries an unresolved flag, so the ordering rule is not working"
            }
        }
        return $problems
    }).GetNewClosure()

    # A hidden caregiver rating: content retained, moderation facts published.
    $caregiverHiddenItemCheck = ({
        param($item, $expectedStars, $expectedReview, $expectedReason)
        $problems = @()
        if ([string]$item.caregiverId -ne $caregiverId) { $problems += "caregiverId is '$($item.caregiverId)', expected the fixture caregiver" }
        if ([string]$item.familyId -ne $familyId) { $problems += "familyId is '$($item.familyId)', expected the fixture family" }
        if ([int]$item.stars -ne $expectedStars) { $problems += "stars of the hidden rating is $($item.stars), expected $expectedStars" }
        if ([string]$item.reviewText -ne $expectedReview) { $problems += 'the hidden rating did not retain its content' }
        if ([string]$item.moderationState -ne 'hidden') { $problems += "moderationState is '$($item.moderationState)', expected 'hidden'" }
        if ([string]$item.hiddenByUserId -ne $supportAdminUserId) { $problems += "hiddenByUserId is '$($item.hiddenByUserId)', expected the support admin who hid it" }
        if ($null -eq $item.hiddenOnUtc) { $problems += 'a hidden rating reports no hide instant' }
        if ([string]$item.hideReason -ne $expectedReason) { $problems += 'hideReason is not the reason that hid the rating' }
        if (-not $caregiverBookingByRating.ContainsKey([string]$item.ratingId)) {
            $problems += "ratingId $($item.ratingId) is not a fixture rating"
        }
        elseif ([string]$item.bookingId -ne $caregiverBookingByRating[[string]$item.ratingId]) {
            $problems += "bookingId of $($item.ratingId) is not the fixture booking"
        }
        return $problems
    }).GetNewClosure()

    $caregiverHiddenLongReasonItemCheck = ({
        param($item)
        return @(& $caregiverHiddenItemCheck $item 3 $caregiverReview['cgr3'] $reason500)
    }).GetNewClosure()

    $caregiverHiddenAfterEditItemCheck = ({
        param($item)
        return @(& $caregiverHiddenItemCheck $item 1 $familyEditReview $hideReason)
    }).GetNewClosure()

    # A search page filtered by minRating: the discriminator is the stored
    # aggregate, so an empty page proves the filter reads what a hide rewrote.
    $emptySearchPageCheck = ({
        param($body)
        $problems = @()
        $items = @($body.items)
        if ($items.Count -ne 0) { $problems += "the minRating filter returned $($items.Count) caregivers, expected none" }
        if ([int]$body.totalCount -ne 0) { $problems += "totalCount is $($body.totalCount), expected 0" }
        return $problems
    }).GetNewClosure()

    $plan = @()

    # -- Section 0: normalise a state a crashed earlier run may have left ----
    # Exactly the facts the run depends on are repaired, with the published
    # verbs: restore a rating that is hidden, close a leftover open flag, and
    # re-apply the canonical rating of the family edit booking (a PUT is an
    # idempotent upsert on this route). Nothing is deleted, reset or re-seeded.
    foreach ($rating in $definition.CaregiverRatings) {
        if ([string]$Baseline.($rating.Short + 'State') -eq '1') {
            $plan += New-F7HttpStep -Name "normalise.restore-caregiver-$($rating.Short)" -Method 'POST' `
                -Path "$caregiverAdmin/$($rating.Id)/restore" -Actor 'support-admin' -Token $supportAdminToken `
                -Body $restoreBody -ExpectedStatus 204 `
                -Expected 'HTTP 204, returning a rating a previous run left hidden to the visible baseline' `
                -Assert $helpers.Problem
        }
    }
    foreach ($rating in $definition.CareHomeRatings) {
        if ([string]$Baseline.($rating.Short + 'State') -eq '1') {
            $plan += New-F7HttpStep -Name "normalise.restore-carehome-$($rating.Short)" -Method 'POST' `
                -Path "$careHomeAdmin/$($rating.Id)/restore" -Actor 'support-admin' -Token $supportAdminToken `
                -Body $careHomeRestoreBody -ExpectedStatus 204 `
                -Expected 'HTTP 204, returning a Care Home rating a previous run left hidden to the visible baseline' `
                -Assert $helpers.Problem
        }
    }
    foreach ($rating in $definition.CareHomeRatings) {
        if ([string]$Baseline.($rating.Short + 'OpenFlagCount') -ne '0') {
            $plan += New-F7HttpStep -Name "normalise.keep-carehome-$($rating.Short)" -Method 'POST' `
                -Path "$careHomeAdmin/$($rating.Id)/keep" -Actor 'support-admin' -Token $supportAdminToken `
                -Body $careHomeKeepBody -ExpectedStatus 204 `
                -Expected 'HTTP 204, closing a flag a previous run left open by keeping the rating visible' `
                -Assert $helpers.Problem
        }
    }

    $plan += New-F7HttpStep -Name 'normalise.family-canonical-rating' -Method 'PUT' `
        -Path "$ratingRoute/$booking1" -Actor 'family' -Token $familyToken -Body $familyCanonicalBody `
        -ExpectedStatus 200 `
        -Expected 'HTTP 200: the fixture booking carries its canonical 5-star fixture rating again, so the baseline aggregate is deterministic' `
        -Assert (New-F7JsonAssert -ActualText "ratingId=$($caregiverRatingId['cgr1'])" -Check ({
            param($body)
            $problems = @()
            if ([string]$body.ratingId -ne $caregiverRatingId['cgr1']) { $problems += "ratingId is '$($body.ratingId)', expected the fixture rating" }
            if ([int]$body.stars -ne 5) { $problems += "stars is $($body.stars), expected 5" }
            return $problems
        }).GetNewClosure())

    $plan += New-F7SqlStep -Name 'sql.state-normalized' -Tag 'state-normalized' `
        -Expected 'the canonical readback: all six ratings visible with their retained content, no open owner flag, the stored caregiver aggregate equal to the SQL-computed one, and this run''s append-only counters captured' `
        -Capture @{
            caregiverActionBase = 'caregiverActionRowCount'
            careHomeActionBase  = 'careHomeActionRowCount'
            careHomeFlagBase    = 'careHomeFlagRowCount'
            facility1FlagBase   = 'facility1FlagRowCount'
            chr1FlagBase        = 'chr1FlagRowCount'
            chr2FlagBase        = 'chr2FlagRowCount'
            chr3FlagBase        = 'chr3FlagRowCount'
        } `
        -Assert (New-F7StateExpectation -Capture $Capture -Agree @{
            caregiverStoredAverage = 'caregiverVisibleAverage'
            caregiverStoredCount   = 'caregiverVisibleCount'
        } -Expect @{
            cgr1State = '0'; cgr2State = '0'; cgr3State = '0'
            chr1State = '0'; chr2State = '0'; chr3State = '0'
            cgr1Stars = '5'; cgr2Stars = '4'; cgr3Stars = '3'
            chr1Stars = '5'; chr2Stars = '4'; chr3Stars = '3'
            cgr1HiddenByUserId = ''; cgr2HiddenByUserId = ''; cgr3HiddenByUserId = ''
            chr1HiddenByUserId = ''; chr2HiddenByUserId = ''; chr3HiddenByUserId = ''
            cgr1HiddenOnPresent = 'false'; cgr2HiddenOnPresent = 'false'; cgr3HiddenOnPresent = 'false'
            chr1HiddenOnPresent = 'false'; chr2HiddenOnPresent = 'false'; chr3HiddenOnPresent = 'false'
            cgr1HideReason = ''; cgr2HideReason = ''; cgr3HideReason = ''
            chr1HideReason = ''; chr2HideReason = ''; chr3HideReason = ''
            cgr1ReviewText = $caregiverReview['cgr1']; cgr2ReviewText = $caregiverReview['cgr2']; cgr3ReviewText = $caregiverReview['cgr3']
            chr1ReviewText = $careHomeReview['chr1']; chr2ReviewText = $careHomeReview['chr2']; chr3ReviewText = $careHomeReview['chr3']
            cgr1UpdatedAfterCreated = 'true'
            caregiverStoredAverage = '4.00'; caregiverStoredCount = '3'
            caregiverVisibleAverage = '4.00'; caregiverVisibleCount = '3'
            caregiverHiddenRatingCount = '0'; caregiverRatingRowCount = '3'
            facility1VisibleRatingCount = '3'; facility1HiddenRatingCount = '0'; facility1RatingRowCount = '3'
            facility1OpenFlagCount = '0'; facility2RatingRowCount = '0'
            chr1OpenFlagCount = '0'; chr2OpenFlagCount = '0'; chr3OpenFlagCount = '0'
            careHomeOpenFlagRowCount = '0'
            caregiverShowProfile = 'true'; caregiverShowRating = 'true'
            caregiverStatus = '4'; caregiverAvailability = '1'; caregiverType = '1'
            familyBookingStatus = '5'; familyBookingCaregiverId = $caregiverId
        })

    # -- Section 1: authentication and role isolation -----------------------
    $anonymousCalls = @(
        [ordered]@{ Name = 'auth.anonymous-caregiver-ratings'; Method = 'GET'; Path = $caregiverAdmin }
        [ordered]@{ Name = 'auth.anonymous-caregiver-hide'; Method = 'POST'; Path = "$caregiverAdmin/$($caregiverRatingId['cgr1'])/hide"; Body = $hideBody }
        [ordered]@{ Name = 'auth.anonymous-caregiver-restore'; Method = 'POST'; Path = "$caregiverAdmin/$($caregiverRatingId['cgr1'])/restore"; Body = $restoreBody }
        [ordered]@{ Name = 'auth.anonymous-carehome-ratings'; Method = 'GET'; Path = $careHomeAdmin }
        [ordered]@{ Name = 'auth.anonymous-carehome-hide'; Method = 'POST'; Path = "$careHomeAdmin/$($careHomeRatingId['chr1'])/hide"; Body = $careHomeHideBody }
        [ordered]@{ Name = 'auth.anonymous-carehome-keep'; Method = 'POST'; Path = "$careHomeAdmin/$($careHomeRatingId['chr1'])/keep"; Body = $careHomeKeepBody }
        [ordered]@{ Name = 'auth.anonymous-carehome-restore'; Method = 'POST'; Path = "$careHomeAdmin/$($careHomeRatingId['chr1'])/restore"; Body = $careHomeRestoreBody }
        [ordered]@{ Name = 'auth.anonymous-owner-ratings'; Method = 'GET'; Path = $ownerRoute }
        [ordered]@{ Name = 'auth.anonymous-owner-flag'; Method = 'POST'; Path = "$ownerRoute/$($careHomeRatingId['chr1'])/flag"; Body = $flagBody }
    )
    foreach ($call in $anonymousCalls) {
        $plan += New-F7HttpStep -Name $call.Name -Method $call.Method -Path $call.Path -Actor 'anonymous' `
            -Token $null -Body $call.Body -ExpectedStatus 401 `
            -Expected 'HTTP 401 without a bearer token' -Assert $helpers.Problem
    }

    $deniedCalls = @(
        [ordered]@{ Name = 'role.content-admin-denied-caregiver-ratings'; Actor = 'content-admin'; Method = 'GET'; Path = $caregiverAdmin }
        [ordered]@{ Name = 'role.content-admin-denied-carehome-ratings'; Actor = 'content-admin'; Method = 'GET'; Path = $careHomeAdmin }
        [ordered]@{ Name = 'role.content-admin-denied-caregiver-hide'; Actor = 'content-admin'; Method = 'POST'; Path = "$caregiverAdmin/$($caregiverRatingId['cgr1'])/hide"; Body = $hideBody }
        [ordered]@{ Name = 'role.content-admin-denied-owner-ratings'; Actor = 'content-admin'; Method = 'GET'; Path = $ownerRoute }
        [ordered]@{ Name = 'role.finance-admin-denied-caregiver-ratings'; Actor = 'finance-admin'; Method = 'GET'; Path = $caregiverAdmin }
        [ordered]@{ Name = 'role.finance-admin-denied-carehome-ratings'; Actor = 'finance-admin'; Method = 'GET'; Path = $careHomeAdmin }
        [ordered]@{ Name = 'role.finance-admin-denied-caregiver-restore'; Actor = 'finance-admin'; Method = 'POST'; Path = "$caregiverAdmin/$($caregiverRatingId['cgr1'])/restore"; Body = $restoreBody }
        [ordered]@{ Name = 'role.finance-admin-denied-owner-ratings'; Actor = 'finance-admin'; Method = 'GET'; Path = $ownerRoute }
        [ordered]@{ Name = 'role.caregiver-denied-caregiver-ratings'; Actor = 'caregiver'; Method = 'GET'; Path = $caregiverAdmin }
        [ordered]@{ Name = 'role.caregiver-denied-carehome-ratings'; Actor = 'caregiver'; Method = 'GET'; Path = $careHomeAdmin }
        [ordered]@{ Name = 'role.caregiver-denied-carehome-keep'; Actor = 'caregiver'; Method = 'POST'; Path = "$careHomeAdmin/$($careHomeRatingId['chr1'])/keep"; Body = $careHomeKeepBody }
        [ordered]@{ Name = 'role.caregiver-denied-owner-ratings'; Actor = 'caregiver'; Method = 'GET'; Path = $ownerRoute }
        [ordered]@{ Name = 'role.family-denied-caregiver-ratings'; Actor = 'family'; Method = 'GET'; Path = $caregiverAdmin }
        [ordered]@{ Name = 'role.family-denied-carehome-ratings'; Actor = 'family'; Method = 'GET'; Path = $careHomeAdmin }
        [ordered]@{ Name = 'role.family-denied-caregiver-hide'; Actor = 'family'; Method = 'POST'; Path = "$caregiverAdmin/$($caregiverRatingId['cgr1'])/hide"; Body = $hideBody }
        [ordered]@{ Name = 'role.family-denied-carehome-hide'; Actor = 'family'; Method = 'POST'; Path = "$careHomeAdmin/$($careHomeRatingId['chr1'])/hide"; Body = $careHomeHideBody }
        [ordered]@{ Name = 'role.family-denied-owner-ratings'; Actor = 'family'; Method = 'GET'; Path = $ownerRoute }
        [ordered]@{ Name = 'role.support-admin-denied-owner-ratings'; Actor = 'support-admin'; Method = 'GET'; Path = $ownerRoute }
        [ordered]@{ Name = 'role.support-admin-denied-owner-flag'; Actor = 'support-admin'; Method = 'POST'; Path = "$ownerRoute/$($careHomeRatingId['chr1'])/flag"; Body = $flagBody }
        [ordered]@{ Name = 'role.owner-denied-caregiver-ratings'; Actor = 'owner'; Method = 'GET'; Path = $caregiverAdmin }
        [ordered]@{ Name = 'role.owner-denied-caregiver-hide'; Actor = 'owner'; Method = 'POST'; Path = "$caregiverAdmin/$($caregiverRatingId['cgr1'])/hide"; Body = $hideBody }
        [ordered]@{ Name = 'role.owner-denied-caregiver-restore'; Actor = 'owner'; Method = 'POST'; Path = "$caregiverAdmin/$($caregiverRatingId['cgr1'])/restore"; Body = $restoreBody }
        [ordered]@{ Name = 'role.owner-denied-carehome-ratings'; Actor = 'owner'; Method = 'GET'; Path = $careHomeAdmin }
        [ordered]@{ Name = 'role.owner-denied-carehome-hide'; Actor = 'owner'; Method = 'POST'; Path = "$careHomeAdmin/$($careHomeRatingId['chr1'])/hide"; Body = $careHomeHideBody }
        [ordered]@{ Name = 'role.owner-denied-carehome-keep'; Actor = 'owner'; Method = 'POST'; Path = "$careHomeAdmin/$($careHomeRatingId['chr1'])/keep"; Body = $careHomeKeepBody }
        [ordered]@{ Name = 'role.owner-denied-carehome-restore'; Actor = 'owner'; Method = 'POST'; Path = "$careHomeAdmin/$($careHomeRatingId['chr1'])/restore"; Body = $careHomeRestoreBody }
        [ordered]@{ Name = 'role.owner-two-denied-caregiver-ratings'; Actor = 'owner2'; Method = 'GET'; Path = $caregiverAdmin }
        [ordered]@{ Name = 'role.owner-two-denied-carehome-ratings'; Actor = 'owner2'; Method = 'GET'; Path = $careHomeAdmin }
    )
    foreach ($call in $deniedCalls) {
        $plan += New-F7HttpStep -Name $call.Name -Method $call.Method -Path $call.Path -Actor $call.Actor `
            -Token $Tokens[$call.Actor] -Body $call.Body -ExpectedStatus 403 `
            -Expected "HTTP 403: the $($call.Actor) identity holds none of the claims the route's policy requires" `
            -Assert $helpers.Problem
    }

    # -- Section 2: admin caregiver-rating reads -----------------------------
    $plan += New-F7HttpStep -Name 'read.caregiver-ratings-baseline' -Method 'GET' `
        -Path "${caregiverAdmin}?caregiverId=$caregiverId" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 200 `
        -Expected 'HTTP 200: the three visible fixture ratings newest-first with the pinned 12-field item shape, the retained content and no reviewer identity' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 3 -PageProperty $pageProperties -ExactPageProperties `
            -ItemProperty $caregiverItemProperties -ExactItemProperties -ForbiddenToken $caregiverForbidden `
            -ExpectedItemStarOrder '3,4,5' -ItemCheck $caregiverItemCheck)
    $plan += New-F7HttpStep -Name 'read.caregiver-ratings-second-page' -Method 'GET' `
        -Path "${caregiverAdmin}?caregiverId=$caregiverId&page=2&pageSize=2" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 200 `
        -Expected 'HTTP 200: paging is applied after filtering, so the second page carries exactly the oldest fixture rating and totalCount stays 3' `
        -Assert (New-F7PageAssert -Page 2 -PageSize 2 -TotalCount 3 -PageProperty $pageProperties -ExactPageProperties `
            -ItemProperty $caregiverItemProperties -ExactItemProperties -ForbiddenToken $caregiverForbidden `
            -ExpectedItemStarOrder '5' -ItemCheck $caregiverItemCheck)
    $plan += New-F7HttpStep -Name 'read.caregiver-ratings-stars-filter' -Method 'GET' `
        -Path "${caregiverAdmin}?caregiverId=$caregiverId&stars=4" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 200 `
        -Expected 'HTTP 200: the stars filter narrows totalCount and the items to the single 4-star fixture rating' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 1 -ItemProperty $caregiverItemProperties -ExactItemProperties `
            -ForbiddenToken $caregiverForbidden -ExpectedItemStarOrder '4')
    $plan += New-F7HttpStep -Name 'read.caregiver-ratings-visible-filter' -Method 'GET' `
        -Path "${caregiverAdmin}?caregiverId=$caregiverId&moderation=visible" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 200 `
        -Expected 'HTTP 200: the visible filter is the canonical three-rating page' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 3 -ItemProperty $caregiverItemProperties -ExactItemProperties `
            -ForbiddenToken $caregiverForbidden -ExpectedItemStarOrder '3,4,5' -ItemCheck $caregiverItemCheck)
    $plan += New-F7HttpStep -Name 'read.caregiver-ratings-hidden-filter-empty' -Method 'GET' `
        -Path "${caregiverAdmin}?caregiverId=$caregiverId&moderation=hidden" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 200 `
        -Expected 'HTTP 200 with an empty page (and totalCount 0) while every fixture rating is visible' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 0 -MaxItemCount 0 -PageProperty $pageProperties -ExactPageProperties)
    $plan += New-F7HttpStep -Name 'read.caregiver-ratings-unknown-caregiver' -Method 'GET' `
        -Path "${caregiverAdmin}?caregiverId=$UnknownGuid" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 200 `
        -Expected 'HTTP 200 with an empty page for an unknown caregiver id: the admin read is a filter, never a 404' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 0 -MaxItemCount 0 -PageProperty $pageProperties -ExactPageProperties)
    $plan += New-F7HttpStep -Name 'read.caregiver-ratings-invalid-page' -Method 'GET' `
        -Path "${caregiverAdmin}?caregiverId=$caregiverId&page=0" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 400 -Expected 'HTTP 400 Caregivers.Rating.InvalidQuery: page 0 is refused, never silently clamped' `
        -Assert (New-F7CodeAssert -Code 'Caregivers.Rating.InvalidQuery')
    $plan += New-F7HttpStep -Name 'read.caregiver-ratings-invalid-page-size' -Method 'GET' `
        -Path "${caregiverAdmin}?caregiverId=$caregiverId&pageSize=101" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 400 -Expected 'HTTP 400 Caregivers.Rating.InvalidQuery: pageSize above the 100 bound is refused' `
        -Assert (New-F7CodeAssert -Code 'Caregivers.Rating.InvalidQuery')
    $plan += New-F7HttpStep -Name 'read.caregiver-ratings-invalid-stars' -Method 'GET' `
        -Path "${caregiverAdmin}?caregiverId=$caregiverId&stars=6" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 400 -Expected 'HTTP 400 Caregivers.Rating.InvalidQuery: a star value outside 1..5 is refused' `
        -Assert (New-F7CodeAssert -Code 'Caregivers.Rating.InvalidQuery')
    $plan += New-F7HttpStep -Name 'read.caregiver-ratings-invalid-moderation' -Method 'GET' `
        -Path "${caregiverAdmin}?caregiverId=$caregiverId&moderation=archived" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 400 -Expected 'HTTP 400 Caregivers.Rating.InvalidQuery: only visible|hidden|all are accepted' `
        -Assert (New-F7CodeAssert -Code 'Caregivers.Rating.InvalidQuery')

    # -- Section 3: admin Care Home-rating reads -----------------------------
    $plan += New-F7HttpStep -Name 'read.care-home-ratings-baseline' -Method 'GET' `
        -Path "${careHomeAdmin}?facilityId=$facility1Id" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 200 `
        -Expected 'HTTP 200: the three visible fixture ratings newest-first with the pinned 15-field item shape, the unresolved-flag fields null and no reviewer identity' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 3 -PageProperty $pageProperties -ExactPageProperties `
            -ItemProperty $careHomeItemProperties -ExactItemProperties -ForbiddenToken $caregiverForbidden `
            -ExpectedItemStarOrder '3,4,5' -ItemCheck $careHomeItemCheck)
    $plan += New-F7HttpStep -Name 'read.care-home-ratings-second-page' -Method 'GET' `
        -Path "${careHomeAdmin}?facilityId=$facility1Id&page=2&pageSize=2" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 200 `
        -Expected 'HTTP 200: the second page carries exactly the oldest fixture rating and totalCount stays 3' `
        -Assert (New-F7PageAssert -Page 2 -PageSize 2 -TotalCount 3 -ItemProperty $careHomeItemProperties -ExactItemProperties `
            -ForbiddenToken $caregiverForbidden -ExpectedItemStarOrder '5' -ItemCheck $careHomeItemCheck)
    $plan += New-F7HttpStep -Name 'read.care-home-ratings-stars-filter' -Method 'GET' `
        -Path "${careHomeAdmin}?facilityId=$facility1Id&stars=5" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 200 `
        -Expected 'HTTP 200: the stars filter narrows totalCount and the items to the single 5-star fixture rating' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 1 -ItemProperty $careHomeItemProperties -ExactItemProperties `
            -ForbiddenToken $caregiverForbidden -ExpectedItemStarOrder '5')
    $plan += New-F7HttpStep -Name 'read.care-home-ratings-visible-filter' -Method 'GET' `
        -Path "${careHomeAdmin}?facilityId=$facility1Id&moderation=visible" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 200 `
        -Expected 'HTTP 200: the visible filter is the canonical three-rating page' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 3 -ItemProperty $careHomeItemProperties -ExactItemProperties `
            -ForbiddenToken $caregiverForbidden -ExpectedItemStarOrder '3,4,5' -ItemCheck $careHomeItemCheck)
    $plan += New-F7HttpStep -Name 'read.care-home-ratings-hidden-filter-empty' -Method 'GET' `
        -Path "${careHomeAdmin}?facilityId=$facility1Id&moderation=hidden" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 200 `
        -Expected 'HTTP 200 with an empty page (and totalCount 0) while every fixture rating is visible' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 0 -MaxItemCount 0 -PageProperty $pageProperties -ExactPageProperties)
    $plan += New-F7HttpStep -Name 'read.care-home-ratings-flagged-false' -Method 'GET' `
        -Path "${careHomeAdmin}?facilityId=$facility1Id&flagged=false" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 200 `
        -Expected 'HTTP 200: flagged=false is a valid filter and returns the three unflagged fixture ratings' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 3 -ItemProperty $careHomeItemProperties -ExactItemProperties `
            -ForbiddenToken $caregiverForbidden -ExpectedItemStarOrder '3,4,5' -ItemCheck $careHomeItemCheck)
    $plan += New-F7HttpStep -Name 'read.care-home-ratings-flagged-true-empty' -Method 'GET' `
        -Path "${careHomeAdmin}?facilityId=$facility1Id&flagged=true" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 200 `
        -Expected 'HTTP 200 with an empty page (and totalCount 0) while no owner flag is open' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 0 -MaxItemCount 0 -PageProperty $pageProperties -ExactPageProperties)
    $plan += New-F7HttpStep -Name 'read.care-home-ratings-other-facility' -Method 'GET' `
        -Path "${careHomeAdmin}?facilityId=$facility2Id" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 200 `
        -Expected 'HTTP 200 with an empty page for the second fixture facility, which carries no rating' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 0 -MaxItemCount 0 -PageProperty $pageProperties -ExactPageProperties)
    $plan += New-F7HttpStep -Name 'read.care-home-ratings-unfiltered-contains-fixture' -Method 'GET' `
        -Path "${careHomeAdmin}?page=1&pageSize=100" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 200 `
        -Expected 'HTTP 200: the unfiltered admin list publishes its own 4-field page and contains the three fixture ratings (they are read by id, so other local ratings cannot make this check fail)' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 100 -PageProperty $pageProperties -ExactPageProperties `
            -ItemProperty $careHomeItemProperties -ExactItemProperties -ForbiddenToken $caregiverForbidden `
            -PageCheck $careHomePageContainsFixture)
    $plan += New-F7HttpStep -Name 'read.care-home-ratings-invalid-page' -Method 'GET' `
        -Path "${careHomeAdmin}?facilityId=$facility1Id&page=0" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 400 -Expected 'HTTP 400 CareHomes.Rating.InvalidQuery: page 0 is refused, never silently clamped' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.InvalidQuery')
    $plan += New-F7HttpStep -Name 'read.care-home-ratings-invalid-page-size' -Method 'GET' `
        -Path "${careHomeAdmin}?facilityId=$facility1Id&pageSize=500" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 400 -Expected 'HTTP 400 CareHomes.Rating.InvalidQuery: pageSize above the 100 bound is refused' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.InvalidQuery')
    $plan += New-F7HttpStep -Name 'read.care-home-ratings-invalid-stars' -Method 'GET' `
        -Path "${careHomeAdmin}?facilityId=$facility1Id&stars=0" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 400 -Expected 'HTTP 400 CareHomes.Rating.InvalidQuery: a star value outside 1..5 is refused' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.InvalidQuery')
    $plan += New-F7HttpStep -Name 'read.care-home-ratings-invalid-moderation' -Method 'GET' `
        -Path "${careHomeAdmin}?facilityId=$facility1Id&moderation=all-time" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 400 -Expected 'HTTP 400 CareHomes.Rating.InvalidQuery: only visible|hidden|all are accepted' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.InvalidQuery')
    $plan += New-F7HttpStep -Name 'read.care-home-ratings-invalid-flagged' -Method 'GET' `
        -Path "${careHomeAdmin}?facilityId=$facility1Id&flagged=maybe" -Actor 'support-admin' -Token $supportAdminToken `
        -ExpectedStatus 400 -Expected 'HTTP 400: a non-boolean flagged value is refused at the model boundary' `
        -Assert $helpers.Plain

    # -- Section 4: the owner flag lifecycle ---------------------------------
    $ownerNoneFlag = @{ Status = 'None' }
    $ownerChr2Open = @{ Status = 'Open'; Reason = $flagReason }
    $ownerChr2Resolved = @{ Status = 'Resolved'; Reason = $flagReason }
    $ownerChr1Open = @{ Status = 'Open'; Reason = $keepFlagReason }
    $ownerChr1Resolved = @{ Status = 'Resolved'; Reason = $keepFlagReason }
    $ownerChr1Reopened = @{ Status = 'Open'; Reason = $keepFlagRepeatReason }
    $ownerChr1ResolvedAgain = @{ Status = 'Resolved'; Reason = $keepFlagRepeatReason }

    # Until this run flags a rating, the owner read shows whatever the retained
    # fixture already carries for the fixture owner: no case at all on a pristine
    # database, the resolved case an earlier run left behind otherwise.
    $ownerHistoricalFlag = @{}
    foreach ($short in @('chr1', 'chr2', 'chr3')) {
        $ownerHistoricalFlag[$short] = New-F7OwnerHistoricalExpectation -Baseline $Baseline -Short $short -OwnerUserId $ownerUserId
    }

    $ownerExpectationBaseline = @{ chr1 = $ownerHistoricalFlag['chr1']; chr2 = $ownerHistoricalFlag['chr2']; chr3 = $ownerHistoricalFlag['chr3'] }
    $ownerExpectationChr2Open = @{ chr1 = $ownerHistoricalFlag['chr1']; chr2 = $ownerChr2Open; chr3 = $ownerHistoricalFlag['chr3'] }
    $ownerExpectationChr2Resolved = @{ chr1 = $ownerHistoricalFlag['chr1']; chr2 = $ownerChr2Resolved; chr3 = $ownerHistoricalFlag['chr3'] }
    $ownerExpectationChr1Open = @{ chr1 = $ownerChr1Open; chr2 = $ownerChr2Resolved; chr3 = $ownerNoneFlag }
    $ownerExpectationChr1Resolved = @{ chr1 = $ownerChr1Resolved; chr2 = $ownerChr2Resolved; chr3 = $ownerNoneFlag }
    $ownerExpectationChr1Reopened = @{ chr1 = $ownerChr1Reopened; chr2 = $ownerChr2Resolved; chr3 = $ownerNoneFlag }
    $ownerExpectationChr1ResolvedAgain = @{ chr1 = $ownerChr1ResolvedAgain; chr2 = $ownerChr2Resolved; chr3 = $ownerNoneFlag }
    $ownerExpectationChr1Hidden = @{ chr2 = $ownerChr2Resolved; chr3 = $ownerNoneFlag }
    $ownerExpectationChr2Hidden = @{ chr1 = $ownerHistoricalFlag['chr1']; chr3 = $ownerHistoricalFlag['chr3'] }

    $plan += New-F7HttpStep -Name 'owner.read-baseline' -Method 'GET' -Path $ownerRoute `
        -Actor 'owner' -Token $ownerToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the owner sees the three ratings about their own facility newest-first, with the pinned 7-field anonymous item shape, their own flag facts and the visible-only aggregates' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 3 -AverageStars 4.00 -RatingCount 3 `
            -PageProperty $ownerPageProperties -ExactPageProperties `
            -ItemProperty $ownerItemProperties -ExactItemProperties -ForbiddenToken $ownerForbidden `
            -ExpectedItemStarOrder '3,4,5' `
            -ItemCheck (New-F7OwnerItemAssert -Expectation $ownerExpectationBaseline -ReviewByRating $careHomeReviewByRating -ShortByRating $shortByRating))
    $plan += New-F7HttpStep -Name 'owner.read-second-page' -Method 'GET' -Path "${ownerRoute}?page=2&pageSize=2" `
        -Actor 'owner' -Token $ownerToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the owner page pages newest-first, so the second page carries exactly the oldest fixture rating (the 5-star one) while both aggregates stay over the whole visible set' `
        -Assert (New-F7PageAssert -Page 2 -PageSize 2 -TotalCount 3 -AverageStars 4.00 -RatingCount 3 `
            -ItemProperty $ownerItemProperties -ExactItemProperties -ForbiddenToken $ownerForbidden `
            -ExpectedItemStarOrder '5' `
            -ItemCheck (New-F7OwnerItemAssert -Expectation $ownerExpectationBaseline -ReviewByRating $careHomeReviewByRating -ShortByRating $shortByRating))
    $plan += New-F7HttpStep -Name 'owner.read-invalid-page' -Method 'GET' -Path "${ownerRoute}?page=0" `
        -Actor 'owner' -Token $ownerToken -ExpectedStatus 400 `
        -Expected 'HTTP 400 CareHomes.Rating.InvalidQuery: page 0 is refused, never silently clamped' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.InvalidQuery')
    $plan += New-F7HttpStep -Name 'owner.read-invalid-page-size' -Method 'GET' -Path "${ownerRoute}?pageSize=101" `
        -Actor 'owner' -Token $ownerToken -ExpectedStatus 400 `
        -Expected 'HTTP 400 CareHomes.Rating.InvalidQuery: pageSize above the 100 bound is refused' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.InvalidQuery')
    $plan += New-F7HttpStep -Name 'owner.read-second-owner-facility' -Method 'GET' -Path $ownerRoute `
        -Actor 'owner2' -Token $owner2Token -ExpectedStatus 200 `
        -Expected 'HTTP 200 with an empty page and both aggregates at zero: the owner read resolves the caller''s own facility, not a facility id from the request' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 0 -MaxItemCount 0 -AverageStars 0 -RatingCount 0 `
            -PageProperty $ownerPageProperties -ExactPageProperties)

    $plan += New-F7HttpStep -Name 'owner.flag-unknown-rating' -Method 'POST' -Path "$ownerRoute/$UnknownGuid/flag" `
        -Actor 'owner' -Token $ownerToken -Body $flagBody -ExpectedStatus 404 `
        -Expected 'HTTP 404 CareHomes.Rating.NotFound: an unknown rating is never confirmed to exist' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.NotFound')
    $plan += New-F7HttpStep -Name 'owner.flag-rating-of-another-facility' -Method 'POST' -Path "$ownerRoute/$($careHomeRatingId['chr1'])/flag" `
        -Actor 'owner2' -Token $owner2Token -Body $flagBody -ExpectedStatus 404 `
        -Expected 'HTTP 404 CareHomes.Rating.NotFound: an owner cannot flag a rating about somebody else''s facility' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.NotFound')
    $plan += New-F7HttpStep -Name 'owner.flag-unknown-rating-bad-reason' -Method 'POST' -Path "$ownerRoute/$UnknownGuid/flag" `
        -Actor 'owner' -Token $ownerToken -Body $blankReasonBody -ExpectedStatus 404 `
        -Expected 'HTTP 404: ownership is resolved before the reason shape, so an unknown rating is never a 400' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.NotFound')
    $plan += New-F7HttpStep -Name 'owner.flag-blank-reason' -Method 'POST' -Path "$ownerRoute/$($careHomeRatingId['chr2'])/flag" `
        -Actor 'owner' -Token $ownerToken -Body $blankReasonBody -ExpectedStatus 400 `
        -Expected 'HTTP 400 CareHomes.Rating.InvalidReason: a flag needs a reason' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.InvalidReason')
    $plan += New-F7HttpStep -Name 'owner.flag-whitespace-reason' -Method 'POST' -Path "$ownerRoute/$($careHomeRatingId['chr2'])/flag" `
        -Actor 'owner' -Token $ownerToken -Body $spaceReasonBody -ExpectedStatus 400 `
        -Expected 'HTTP 400 CareHomes.Rating.InvalidReason: a whitespace-only reason is not a reason' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.InvalidReason')
    $plan += New-F7HttpStep -Name 'owner.flag-over-500-reason' -Method 'POST' -Path "$ownerRoute/$($careHomeRatingId['chr2'])/flag" `
        -Actor 'owner' -Token $ownerToken -Body $overReasonBody -ExpectedStatus 400 `
        -Expected 'HTTP 400 CareHomes.Rating.InvalidReason: a 501-character reason is refused' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.InvalidReason')

    $plan += New-F7HttpStep -Name 'owner.flag-second-rating' -Method 'POST' -Path "$ownerRoute/$($careHomeRatingId['chr2'])/flag" `
        -Actor 'owner' -Token $ownerToken -Body $flagBody -ExpectedStatus 204 `
        -Expected 'HTTP 204: the owner opens the first flag over the second Care Home rating' `
        -Assert $helpers.Problem
    $plan += New-F7SqlStep -Name 'sql.owner-flag-open' -Tag 'owner-flag-open' `
        -Expected 'exactly one open flag row for the second Care Home rating, owned by the fixture owner with the given reason and no resolution, and no other open flag anywhere' `
        -Assert (New-F7StateExpectation -Capture $Capture -Expect @{
            chr2State = '0'
            chr2OpenFlagCount = '1'
            chr2LastFlagStatus = '0'
            chr2LastFlagReason = $flagReason
            chr2LastFlagOwnerUserId = $ownerUserId
            chr2LastFlagResolution = ''
            chr2LastFlagResolvedBy = ''
            chr2LastFlagResolvedOnPresent = 'false'
            chr1OpenFlagCount = '0'; chr3OpenFlagCount = '0'
            facility1OpenFlagCount = '1'
            careHomeOpenFlagRowCount = '1'
        } -CounterDelta @{
            chr2FlagRowCount = @('chr2FlagBase', 1)
            chr1FlagRowCount = @('chr1FlagBase', 0)
            chr3FlagRowCount = @('chr3FlagBase', 0)
            facility1FlagRowCount = @('facility1FlagBase', 1)
            careHomeFlagRowCount = @('careHomeFlagBase', 1)
        })

    $plan += New-F7HttpStep -Name 'owner.read-chr2-open' -Method 'GET' -Path $ownerRoute `
        -Actor 'owner' -Token $ownerToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the owner page reports the open flag on the second rating with the owner''s own reason, and None for the two ratings never flagged' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 3 -AverageStars 4.00 -RatingCount 3 `
            -ItemProperty $ownerItemProperties -ExactItemProperties -ForbiddenToken $ownerForbidden `
            -ExpectedItemStarOrder '3,4,5' `
            -ItemCheck (New-F7OwnerItemAssert -Expectation $ownerExpectationChr2Open -ReviewByRating $careHomeReviewByRating -ShortByRating $shortByRating))
    $plan += New-F7HttpStep -Name 'owner.flag-repeat-writes-nothing' -Method 'POST' -Path "$ownerRoute/$($careHomeRatingId['chr2'])/flag" `
        -Actor 'owner' -Token $ownerToken -Body $flagRepeatBody -ExpectedStatus 204 `
        -Expected 'HTTP 204 with a distinguishable reason: a repeat flag while one is open writes nothing at all' `
        -Assert $helpers.Problem
    $plan += New-F7SqlStep -Name 'sql.owner-flag-repeat' -Tag 'owner-flag-repeat' `
        -Expected 'the repeat flag added no row and did not overwrite the open flag''s reason: the open flag still carries the first reason' `
        -Assert (New-F7StateExpectation -Capture $Capture -Expect @{
            chr2OpenFlagCount = '1'
            chr2LastFlagReason = $flagReason
            facility1OpenFlagCount = '1'
            careHomeOpenFlagRowCount = '1'
        } -CounterDelta @{
            chr2FlagRowCount = @('chr2FlagBase', 1)
            facility1FlagRowCount = @('facility1FlagBase', 1)
            careHomeFlagRowCount = @('careHomeFlagBase', 1)
        })

    $plan += New-F7HttpStep -Name 'admin.read-flagged-true' -Method 'GET' -Path "${careHomeAdmin}?facilityId=$facility1Id&flagged=true" `
        -Actor 'support-admin' -Token $supportAdminToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the flagged filter returns exactly the flagged rating with the 15-field item shape carrying the owner''s flag reason and no reviewer identity' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 1 `
            -ItemProperty $careHomeItemProperties -ExactItemProperties -ForbiddenToken $caregiverForbidden `
            -ExpectedItemStarOrder '4' -ItemCheck $careHomeFlaggedChr2ItemCheck)
    $plan += New-F7HttpStep -Name 'admin.read-flagged-first-unfiltered' -Method 'GET' -Path "${careHomeAdmin}?facilityId=$facility1Id" `
        -Actor 'support-admin' -Token $supportAdminToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the unfiltered admin page still publishes all three ratings and puts the one with an unresolved owner flag first' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 3 `
            -ItemProperty $careHomeItemProperties -ExactItemProperties -ForbiddenToken $caregiverForbidden `
            -PageCheck $careHomePageFirstIsFlagged)
    $plan += New-F7HttpStep -Name 'admin.read-flagged-false' -Method 'GET' -Path "${careHomeAdmin}?facilityId=$facility1Id&flagged=false" `
        -Actor 'support-admin' -Token $supportAdminToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: flagged=false is a valid filter and returns exactly the two unflagged ratings, newest-first' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 2 `
            -ItemProperty $careHomeItemProperties -ExactItemProperties -ForbiddenToken $caregiverForbidden `
            -ExpectedItemStarOrder '3,5' -ItemCheck $careHomeItemCheck)

    # -- Section 5: an admin hide resolves the open owner flag ---------------
    $plan += New-F7HttpStep -Name 'admin.hide-chr2' -Method 'POST' -Path "$careHomeAdmin/$($careHomeRatingId['chr2'])/hide" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $careHomeHideBody -ExpectedStatus 204 `
        -Expected 'HTTP 204: hiding the flagged Care Home rating succeeds and resolves its open owner flag as Hidden with the same admin attributed' `
        -Assert $helpers.Problem
    $plan += New-F7HttpStep -Name 'admin.hide-chr2-again' -Method 'POST' -Path "$careHomeAdmin/$($careHomeRatingId['chr2'])/hide" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $careHomeHideBody -ExpectedStatus 409 `
        -Expected 'HTTP 409 CareHomes.Rating.AlreadyHidden: hiding a hidden rating is a conflict, never a silent no-op' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.AlreadyHidden')
    $plan += New-F7HttpStep -Name 'admin.hide-chr2-blank-reason' -Method 'POST' -Path "$careHomeAdmin/$($careHomeRatingId['chr1'])/hide" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $blankReasonBody -ExpectedStatus 400 `
        -Expected 'HTTP 400 CareHomes.Rating.InvalidReason: a hide needs a reason' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.InvalidReason')
    $plan += New-F7HttpStep -Name 'admin.hide-chr2-whitespace-reason' -Method 'POST' -Path "$careHomeAdmin/$($careHomeRatingId['chr1'])/hide" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $spaceReasonBody -ExpectedStatus 400 `
        -Expected 'HTTP 400 CareHomes.Rating.InvalidReason: a whitespace-only reason is not a reason' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.InvalidReason')
    $plan += New-F7HttpStep -Name 'admin.hide-chr2-over-500-reason' -Method 'POST' -Path "$careHomeAdmin/$($careHomeRatingId['chr1'])/hide" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $overReasonBody -ExpectedStatus 400 `
        -Expected 'HTTP 400 CareHomes.Rating.InvalidReason: a 501-character reason is refused' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.InvalidReason')
    $plan += New-F7HttpStep -Name 'admin.hide-unknown-rating' -Method 'POST' -Path "$careHomeAdmin/$UnknownGuid/hide" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $careHomeHideBody -ExpectedStatus 404 `
        -Expected 'HTTP 404 CareHomes.Rating.NotFound for an unknown Care Home rating id' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.NotFound')
    $plan += New-F7HttpStep -Name 'admin.restore-chr1-while-visible' -Method 'POST' -Path "$careHomeAdmin/$($careHomeRatingId['chr1'])/restore" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $careHomeRestoreBody -ExpectedStatus 409 `
        -Expected 'HTTP 409 CareHomes.Rating.AlreadyVisible: restoring a visible rating is a conflict' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.AlreadyVisible')

    $plan += New-F7SqlStep -Name 'sql.carehome-hidden-chr2' -Tag 'carehome-hidden-chr2' `
        -Expected 'the second Care Home rating is hidden with the admin''s attribution and reason, its owner flag is resolved as Hidden by the same admin, and exactly one moderation action row was appended' `
        -Assert (New-F7StateExpectation -Capture $Capture -Expect @{
            chr2State = '1'
            chr2HiddenByUserId = $supportAdminUserId
            chr2HiddenOnPresent = 'true'
            chr2HideReason = $careHomeHideReason
            chr2HideReasonLength = "$($careHomeHideReason.Length)"
            chr2ReviewText = $careHomeReview['chr2']
            chr2OpenFlagCount = '0'
            chr2LastFlagStatus = '1'
            chr2LastFlagResolution = '0'
            chr2LastFlagResolvedBy = $supportAdminUserId
            chr2LastFlagResolvedOnPresent = 'true'
            chr2LastFlagReason = $flagReason
            careHomeOpenFlagRowCount = '0'
            facility1OpenFlagCount = '0'
            facility1HiddenRatingCount = '1'
            facility1VisibleRatingCount = '2'
            careHomeActionLastType = '0'
            careHomeActionLastActor = $supportAdminUserId
            careHomeActionLastRatingId = $careHomeRatingId['chr2']
            careHomeActionLastReason = $careHomeHideReason
        } -CounterDelta @{
            chr2FlagRowCount = @('chr2FlagBase', 1)
            careHomeFlagRowCount = @('careHomeFlagBase', 1)
            facility1FlagRowCount = @('facility1FlagBase', 1)
            careHomeActionRowCount = @('careHomeActionBase', 1)
        })

    $plan += New-F7HttpStep -Name 'owner.read-chr2-hidden' -Method 'GET' -Path $ownerRoute `
        -Actor 'owner' -Token $ownerToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the hidden rating leaves the owner page and its aggregate, so the owner sees two ratings and an average over two' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 2 -AverageStars 4.00 -RatingCount 2 `
            -PageProperty $ownerPageProperties -ExactPageProperties `
            -ItemProperty $ownerItemProperties -ExactItemProperties -ForbiddenToken $ownerForbidden `
            -ForbiddenItemId $careHomeRatingId['chr2'] -ExpectedItemStarOrder '3,5' `
            -ItemCheck (New-F7OwnerItemAssert -Expectation $ownerExpectationChr2Hidden -ReviewByRating $careHomeReviewByRating -ShortByRating $shortByRating))
    $plan += New-F7HttpStep -Name 'admin.read-chr2-hidden-filter' -Method 'GET' -Path "${careHomeAdmin}?facilityId=$facility1Id&moderation=hidden" `
        -Actor 'support-admin' -Token $supportAdminToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the hidden filter returns exactly the hidden rating with its attribution, reason, retained content and no unresolved flag' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 1 `
            -ItemProperty $careHomeItemProperties -ExactItemProperties -ForbiddenToken $caregiverForbidden `
            -ExpectedItemStarOrder '4' `
            -ItemCheck ({
                param($item)
                return @(& $careHomeHiddenItemCheck $item $careHomeHideReason)
            }).GetNewClosure())
    $plan += New-F7HttpStep -Name 'admin.read-visible-after-hide' -Method 'GET' -Path "${careHomeAdmin}?facilityId=$facility1Id&moderation=visible" `
        -Actor 'support-admin' -Token $supportAdminToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the visible filter now returns the two remaining ratings, newest-first' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 2 `
            -ItemProperty $careHomeItemProperties -ExactItemProperties -ForbiddenToken $caregiverForbidden `
            -ExpectedItemStarOrder '3,5' -ItemCheck $careHomeItemCheck)
    $plan += New-F7HttpStep -Name 'admin.read-flagged-true-after-hide' -Method 'GET' -Path "${careHomeAdmin}?facilityId=$facility1Id&flagged=true" `
        -Actor 'support-admin' -Token $supportAdminToken -ExpectedStatus 200 `
        -Expected 'HTTP 200 with an empty page: resolving the flag on hide removed it from the flagged filter' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 0 -MaxItemCount 0 -PageProperty $pageProperties -ExactPageProperties)

    # -- Section 6: an admin restore returns the rating without reopening the
    #    resolved flag ------------------------------------------------------
    $plan += New-F7HttpStep -Name 'admin.restore-chr2' -Method 'POST' -Path "$careHomeAdmin/$($careHomeRatingId['chr2'])/restore" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $careHomeRestoreBody -ExpectedStatus 204 `
        -Expected 'HTTP 204: the hidden Care Home rating returns to every non-admin read and to both aggregates' `
        -Assert $helpers.Problem
    $plan += New-F7HttpStep -Name 'admin.restore-chr2-again' -Method 'POST' -Path "$careHomeAdmin/$($careHomeRatingId['chr2'])/restore" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $careHomeRestoreBody -ExpectedStatus 409 `
        -Expected 'HTTP 409 CareHomes.Rating.AlreadyVisible: restoring a visible rating is a conflict' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.AlreadyVisible')
    $plan += New-F7HttpStep -Name 'admin.restore-unknown-rating' -Method 'POST' -Path "$careHomeAdmin/$UnknownGuid/restore" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $careHomeRestoreBody -ExpectedStatus 404 `
        -Expected 'HTTP 404 CareHomes.Rating.NotFound for an unknown Care Home rating id' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.NotFound')
    $plan += New-F7HttpStep -Name 'admin.restore-unknown-rating-blank-reason' -Method 'POST' -Path "$careHomeAdmin/$UnknownGuid/restore" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $blankReasonBody -ExpectedStatus 404 `
        -Expected 'HTTP 404: the pinned precedence resolves the rating before the reason shape, so an unknown id is never a 400' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.NotFound')
    $plan += New-F7HttpStep -Name 'admin.restore-visible-blank-reason' -Method 'POST' -Path "$careHomeAdmin/$($careHomeRatingId['chr1'])/restore" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $blankReasonBody -ExpectedStatus 400 `
        -Expected 'HTTP 400 CareHomes.Rating.InvalidReason: the reason shape is resolved before the visibility transition' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.InvalidReason')

    $plan += New-F7SqlStep -Name 'sql.carehome-restored-chr2' -Tag 'carehome-restored-chr2' `
        -Expected 'the second Care Home rating is visible again with every hide fact cleared, its resolved flag stays resolved and keeps its reason, and one restore row was appended' `
        -Assert (New-F7StateExpectation -Capture $Capture -Expect @{
            chr2State = '0'
            chr2HiddenByUserId = ''
            chr2HiddenOnPresent = 'false'
            chr2HideReason = ''
            chr2HideReasonLength = '0'
            chr2Stars = '4'
            chr2ReviewText = $careHomeReview['chr2']
            chr2OpenFlagCount = '0'
            chr2LastFlagStatus = '1'
            chr2LastFlagResolution = '0'
            chr2LastFlagResolvedBy = $supportAdminUserId
            chr2LastFlagReason = $flagReason
            chr2FlagRowCount = '1'
            facility1HiddenRatingCount = '0'
            facility1VisibleRatingCount = '3'
            careHomeOpenFlagRowCount = '0'
            careHomeActionLastType = '1'
            careHomeActionLastActor = $supportAdminUserId
            careHomeActionLastRatingId = $careHomeRatingId['chr2']
            careHomeActionLastReason = $careHomeRestoreReason
            careHomeActionLastReasonLength = "$($careHomeRestoreReason.Length)"
        } -CounterDelta @{
            careHomeActionRowCount = @('careHomeActionBase', 2)
            careHomeFlagRowCount = @('careHomeFlagBase', 1)
            facility1FlagRowCount = @('facility1FlagBase', 1)
        })

    $plan += New-F7HttpStep -Name 'owner.read-chr2-resolved' -Method 'GET' -Path $ownerRoute `
        -Actor 'owner' -Token $ownerToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the restored rating is back on the owner page with the owner''s flag reported as Resolved and the reason the owner gave still attached' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 3 -AverageStars 4.00 -RatingCount 3 `
            -ItemProperty $ownerItemProperties -ExactItemProperties -ForbiddenToken $ownerForbidden `
            -ExpectedItemStarOrder '3,4,5' `
            -ItemCheck (New-F7OwnerItemAssert -Expectation $ownerExpectationChr2Resolved -ReviewByRating $careHomeReviewByRating -ShortByRating $shortByRating))
    $plan += New-F7HttpStep -Name 'admin.read-after-restore-chr2' -Method 'GET' -Path "${careHomeAdmin}?facilityId=$facility1Id" `
        -Actor 'support-admin' -Token $supportAdminToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the restored rating publishes no unresolved flag, so the admin page is back to the plain newest-first three-rating page' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 3 `
            -ItemProperty $careHomeItemProperties -ExactItemProperties -ForbiddenToken $caregiverForbidden `
            -ExpectedItemStarOrder '3,4,5' -ItemCheck $careHomeItemCheck)

    # -- Section 7: keep resolves a flag without changing visibility ---------
    $plan += New-F7HttpStep -Name 'admin.keep-without-flag' -Method 'POST' -Path "$careHomeAdmin/$($careHomeRatingId['chr1'])/keep" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $careHomeKeepBody -ExpectedStatus 409 `
        -Expected 'HTTP 409 CareHomes.Rating.NoOpenFlag: keeping a rating nobody flagged is a conflict' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.NoOpenFlag')
    $plan += New-F7HttpStep -Name 'owner.flag-first-rating' -Method 'POST' -Path "$ownerRoute/$($careHomeRatingId['chr1'])/flag" `
        -Actor 'owner' -Token $ownerToken -Body $keepFlagBody -ExpectedStatus 204 `
        -Expected 'HTTP 204: the owner opens a flag on the first Care Home rating' `
        -Assert $helpers.Problem
    $plan += New-F7SqlStep -Name 'sql.owner-flag-chr1-open' -Tag 'owner-flag-chr1-open' `
        -Expected 'a second owner flag row exists for the first rating and it is the only open flag' `
        -Assert (New-F7StateExpectation -Capture $Capture -Expect @{
            chr1OpenFlagCount = '1'
            chr1LastFlagStatus = '0'
            chr1LastFlagReason = $keepFlagReason
            chr1LastFlagOwnerUserId = $ownerUserId
            chr2OpenFlagCount = '0'
            chr3OpenFlagCount = '0'
            facility1OpenFlagCount = '1'
            careHomeOpenFlagRowCount = '1'
        } -CounterDelta @{
            chr1FlagRowCount = @('chr1FlagBase', 1)
            careHomeFlagRowCount = @('careHomeFlagBase', 2)
            facility1FlagRowCount = @('facility1FlagBase', 2)
        })
    $plan += New-F7HttpStep -Name 'owner.read-chr1-open' -Method 'GET' -Path $ownerRoute `
        -Actor 'owner' -Token $ownerToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the owner page reports the first rating''s flag as Open and the second''s resolved case as Resolved, each with its own reason' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 3 -AverageStars 4.00 -RatingCount 3 `
            -ItemProperty $ownerItemProperties -ExactItemProperties -ForbiddenToken $ownerForbidden `
            -ExpectedItemStarOrder '3,4,5' `
            -ItemCheck (New-F7OwnerItemAssert -Expectation $ownerExpectationChr1Open -ReviewByRating $careHomeReviewByRating -ShortByRating $shortByRating))
    $plan += New-F7HttpStep -Name 'admin.read-flagged-true-chr1' -Method 'GET' -Path "${careHomeAdmin}?facilityId=$facility1Id&flagged=true" `
        -Actor 'support-admin' -Token $supportAdminToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the flagged filter follows the open flag to the first rating' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 1 `
            -ItemProperty $careHomeItemProperties -ExactItemProperties -ForbiddenToken $caregiverForbidden `
            -ExpectedItemStarOrder '5' -ItemCheck $careHomeFlaggedChr1ItemCheck)

    $plan += New-F7HttpStep -Name 'admin.keep-chr1' -Method 'POST' -Path "$careHomeAdmin/$($careHomeRatingId['chr1'])/keep" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $careHomeKeepBody -ExpectedStatus 204 `
        -Expected 'HTTP 204: keeping the flagged rating visible closes the owner''s case as Kept with the admin attributed, and appends no moderation action to the rating' `
        -Assert $helpers.Problem
    $plan += New-F7SqlStep -Name 'sql.carehome-kept-chr1' -Tag 'carehome-kept-chr1' `
        -Expected 'the first rating is still visible with no flag open, its flag is resolved as Kept by the admin with a resolution instant, and no moderation action row was appended' `
        -Assert (New-F7StateExpectation -Capture $Capture -Expect @{
            chr1State = '0'
            chr1HiddenByUserId = ''
            chr1HideReason = ''
            chr1OpenFlagCount = '0'
            chr1LastFlagStatus = '1'
            chr1LastFlagResolution = '1'
            chr1LastFlagResolvedBy = $supportAdminUserId
            chr1LastFlagResolvedOnPresent = 'true'
            chr1LastFlagReason = $keepFlagReason
            chr2OpenFlagCount = '0'
            chr3OpenFlagCount = '0'
            facility1OpenFlagCount = '0'
            facility1VisibleRatingCount = '3'
            careHomeOpenFlagRowCount = '0'
            careHomeActionLastType = '1'
        } -CounterDelta @{
            chr1FlagRowCount = @('chr1FlagBase', 1)
            careHomeFlagRowCount = @('careHomeFlagBase', 2)
            facility1FlagRowCount = @('facility1FlagBase', 2)
            careHomeActionRowCount = @('careHomeActionBase', 2)
        })
    $plan += New-F7HttpStep -Name 'admin.keep-chr1-again' -Method 'POST' -Path "$careHomeAdmin/$($careHomeRatingId['chr1'])/keep" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $careHomeKeepBody -ExpectedStatus 409 `
        -Expected 'HTTP 409 CareHomes.Rating.NoOpenFlag: a resolved flag cannot be resolved twice' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.NoOpenFlag')
    $plan += New-F7HttpStep -Name 'owner.read-chr1-resolved' -Method 'GET' -Path $ownerRoute `
        -Actor 'owner' -Token $ownerToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the owner sees the kept case as Resolved with the reason the owner gave' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 3 -AverageStars 4.00 -RatingCount 3 `
            -ItemProperty $ownerItemProperties -ExactItemProperties -ForbiddenToken $ownerForbidden `
            -ExpectedItemStarOrder '3,4,5' `
            -ItemCheck (New-F7OwnerItemAssert -Expectation $ownerExpectationChr1Resolved -ReviewByRating $careHomeReviewByRating -ShortByRating $shortByRating))

    $plan += New-F7HttpStep -Name 'owner.reflag-chr1-after-resolution' -Method 'POST' -Path "$ownerRoute/$($careHomeRatingId['chr1'])/flag" `
        -Actor 'owner' -Token $ownerToken -Body $keepFlagRepeatBody -ExpectedStatus 204 `
        -Expected 'HTTP 204: a flag raised after a resolution opens a fresh case instead of reusing the closed one' `
        -Assert $helpers.Problem
    $plan += New-F7SqlStep -Name 'sql.owner-flag-chr1-reopened' -Tag 'owner-flag-chr1-reopened' `
        -Expected 'a third flag row exists for the first rating, it is open, and it carries the second reason while the resolved row keeps the first' `
        -Assert (New-F7StateExpectation -Capture $Capture -Expect @{
            chr1OpenFlagCount = '1'
            chr1LastFlagStatus = '0'
            chr1LastFlagReason = $keepFlagRepeatReason
            chr1LastFlagResolution = ''
            chr1LastFlagResolvedOnPresent = 'false'
            facility1OpenFlagCount = '1'
            careHomeOpenFlagRowCount = '1'
            chr2OpenFlagCount = '0'
        } -CounterDelta @{
            chr1FlagRowCount = @('chr1FlagBase', 2)
            careHomeFlagRowCount = @('careHomeFlagBase', 3)
            facility1FlagRowCount = @('facility1FlagBase', 3)
        })
    $plan += New-F7HttpStep -Name 'owner.read-chr1-reopened' -Method 'GET' -Path $ownerRoute `
        -Actor 'owner' -Token $ownerToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the fresh flag is reported as Open and carries the fresh reason' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 3 -AverageStars 4.00 -RatingCount 3 `
            -ItemProperty $ownerItemProperties -ExactItemProperties -ForbiddenToken $ownerForbidden `
            -ExpectedItemStarOrder '3,4,5' `
            -ItemCheck (New-F7OwnerItemAssert -Expectation $ownerExpectationChr1Reopened -ReviewByRating $careHomeReviewByRating -ShortByRating $shortByRating))
    $plan += New-F7HttpStep -Name 'admin.keep-chr1-reopened' -Method 'POST' -Path "$careHomeAdmin/$($careHomeRatingId['chr1'])/keep" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $keepFlagBody -ExpectedStatus 204 `
        -Expected 'HTTP 204: the admin keeps the rating visible again, closing the fresh case as Kept' `
        -Assert $helpers.Problem
    $plan += New-F7SqlStep -Name 'sql.carehome-kept-chr1-again' -Tag 'carehome-kept-chr1-again' `
        -Expected 'no flag is open anywhere, the newest first-rating flag is resolved as Kept, and no moderation action row was appended by either keep' `
        -Assert (New-F7StateExpectation -Capture $Capture -Expect @{
            chr1State = '0'
            chr1OpenFlagCount = '0'
            chr1LastFlagStatus = '1'
            chr1LastFlagResolution = '1'
            chr1LastFlagResolvedBy = $supportAdminUserId
            chr1LastFlagResolvedOnPresent = 'true'
            chr1LastFlagReason = $keepFlagRepeatReason
            facility1VisibleRatingCount = '3'
            facility1HiddenRatingCount = '0'
            facility1OpenFlagCount = '0'
            careHomeOpenFlagRowCount = '0'
            careHomeActionLastType = '1'
        } -CounterDelta @{
            chr1FlagRowCount = @('chr1FlagBase', 2)
            careHomeFlagRowCount = @('careHomeFlagBase', 3)
            facility1FlagRowCount = @('facility1FlagBase', 3)
            careHomeActionRowCount = @('careHomeActionBase', 2)
        })
    $plan += New-F7HttpStep -Name 'owner.read-chr1-resolved-again' -Method 'GET' -Path $ownerRoute `
        -Actor 'owner' -Token $ownerToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the owner sees the second case resolved with the second reason' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 3 -AverageStars 4.00 -RatingCount 3 `
            -ItemProperty $ownerItemProperties -ExactItemProperties -ForbiddenToken $ownerForbidden `
            -ExpectedItemStarOrder '3,4,5' `
            -ItemCheck (New-F7OwnerItemAssert -Expectation $ownerExpectationChr1ResolvedAgain -ReviewByRating $careHomeReviewByRating -ShortByRating $shortByRating))

    # -- Section 8: hiding a rating nobody flagged, and the 500-character bound
    $plan += New-F7HttpStep -Name 'admin.hide-chr1-without-flag-500-reason' -Method 'POST' -Path "$careHomeAdmin/$($careHomeRatingId['chr1'])/hide" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $reason500Body -ExpectedStatus 204 `
        -Expected 'HTTP 204: a rating nobody flagged can still be hidden, and a reason of exactly the 500-character bound is accepted' `
        -Assert $helpers.Problem
    $plan += New-F7SqlStep -Name 'sql.carehome-hidden-chr1-500' -Tag 'carehome-hidden-chr1-500' `
        -Expected 'the first rating is hidden with the full 500-character reason stored verbatim, no flag exists for it, and exactly one action row was appended' `
        -Assert (New-F7StateExpectation -Capture $Capture -Expect @{
            chr1State = '1'
            chr1HiddenByUserId = $supportAdminUserId
            chr1HiddenOnPresent = 'true'
            chr1HideReason = $reason500
            chr1HideReasonLength = '500'
            chr1Stars = '5'
            chr1ReviewText = $careHomeReview['chr1']
            chr1OpenFlagCount = '0'
            chr1LastFlagStatus = '1'
            chr1LastFlagResolution = '1'
            chr1LastFlagReason = $keepFlagRepeatReason
            facility1HiddenRatingCount = '1'
            facility1VisibleRatingCount = '2'
            facility1OpenFlagCount = '0'
            careHomeActionLastType = '0'
            careHomeActionLastActor = $supportAdminUserId
            careHomeActionLastRatingId = $careHomeRatingId['chr1']
            careHomeActionLastReason = $reason500
            careHomeActionLastReasonLength = '500'
        } -CounterDelta @{
            careHomeActionRowCount = @('careHomeActionBase', 3)
            chr1FlagRowCount = @('chr1FlagBase', 2)
            careHomeFlagRowCount = @('careHomeFlagBase', 3)
        })
    $plan += New-F7HttpStep -Name 'admin.read-chr1-hidden-500' -Method 'GET' -Path "${careHomeAdmin}?facilityId=$facility1Id&moderation=hidden" `
        -Actor 'support-admin' -Token $supportAdminToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the hidden filter returns the hidden rating with its retained content and the full 500-character reason' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 1 `
            -ItemProperty $careHomeItemProperties -ExactItemProperties -ForbiddenToken $caregiverForbidden `
            -ExpectedItemStarOrder '5' -ItemCheck $careHomeHiddenLongReasonItemCheck)
    $plan += New-F7HttpStep -Name 'admin.hide-chr1-hidden-blank-reason' -Method 'POST' -Path "$careHomeAdmin/$($careHomeRatingId['chr1'])/hide" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $blankReasonBody -ExpectedStatus 400 `
        -Expected 'HTTP 400 CareHomes.Rating.InvalidReason: the reason shape is resolved before the visibility transition' `
        -Assert (New-F7CodeAssert -Code 'CareHomes.Rating.InvalidReason')
    $plan += New-F7HttpStep -Name 'admin.restore-chr1-500' -Method 'POST' -Path "$careHomeAdmin/$($careHomeRatingId['chr1'])/restore" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $careHomeRestoreBody -ExpectedStatus 204 `
        -Expected 'HTTP 204: the hidden rating is restored, and its 500-character hide reason is cleared from the rating while the append-only action row keeps it' `
        -Assert $helpers.Problem
    $plan += New-F7SqlStep -Name 'sql.carehome-restored-chr1' -Tag 'carehome-restored-chr1' `
        -Expected 'the first rating is visible again with every hide fact cleared and no stored reason, while the newest action row still carries the full hide reason' `
        -Assert (New-F7StateExpectation -Capture $Capture -Expect @{
            chr1State = '0'
            chr1HiddenByUserId = ''
            chr1HiddenOnPresent = 'false'
            chr1HideReason = ''
            chr1HideReasonLength = '0'
            chr1ReviewText = $careHomeReview['chr1']
            facility1HiddenRatingCount = '0'
            facility1VisibleRatingCount = '3'
            careHomeOpenFlagRowCount = '0'
            careHomeActionLastType = '1'
            careHomeActionLastActor = $supportAdminUserId
            careHomeActionLastRatingId = $careHomeRatingId['chr1']
            careHomeActionLastReason = $careHomeRestoreReason
            careHomeActionLastReasonLength = "$($careHomeRestoreReason.Length)"
        } -CounterDelta @{
            careHomeActionRowCount = @('careHomeActionBase', 4)
            chr1FlagRowCount = @('chr1FlagBase', 2)
            careHomeFlagRowCount = @('careHomeFlagBase', 3)
        })
    $plan += New-F7HttpStep -Name 'admin.read-hidden-empty-after-restore' -Method 'GET' -Path "${careHomeAdmin}?facilityId=$facility1Id&moderation=hidden" `
        -Actor 'support-admin' -Token $supportAdminToken -ExpectedStatus 200 `
        -Expected 'HTTP 200 with an empty page: every fixture Care Home rating is visible again' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 0 -MaxItemCount 0 -PageProperty $pageProperties -ExactPageProperties)

    # -- Section 9: the caregiver baseline agrees across all consumers -------
    $plan += New-F7HttpStep -Name 'consumer.profile-baseline' -Method 'GET' -Path "$caregiversRoute/$caregiverId" `
        -Actor 'family' -Token $familyToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the public caregiver profile publishes the stored 4.00 over 3 aggregate and no rating rows, reviewer identity or moderation fact' `
        -Assert (New-F7ConsumerAssert -Kind 'profile' -Average 4.00 -Count 3 -CaregiverId $caregiverId -CaregiverName $caregiverName)
    $plan += New-F7HttpStep -Name 'consumer.search-baseline' -Method 'GET' -Path "${caregiversRoute}?search=$searchQuery&pageSize=10" `
        -Actor 'family' -Token $familyToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: search reads the stored aggregate, so its card publishes the same 4.00 over 3 as the profile' `
        -Assert (New-F7ConsumerAssert -Kind 'search' -Average 4.00 -Count 3 -CaregiverId $caregiverId -CaregiverName $caregiverName)
    $plan += New-F7HttpStep -Name 'consumer.search-min-rating-baseline' -Method 'GET' -Path "${caregiversRoute}?search=$searchQuery&minRating=3.6" `
        -Actor 'family' -Token $familyToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the minRating filter reads the stored aggregate, so a 4.00 caregiver passes a 3.6 floor' `
        -Assert (New-F7ConsumerAssert -Kind 'search' -Average 4.00 -Count 3 -CaregiverId $caregiverId -CaregiverName $caregiverName)
    $plan += New-F7HttpStep -Name 'consumer.earnings-baseline' -Method 'GET' -Path "$caregiversRoute/$caregiverId/earnings/summary" `
        -Actor 'caregiver' -Token $caregiverToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the caregiver earnings summary publishes the same stored 4.00 aggregate' `
        -Assert (New-F7ConsumerAssert -Kind 'earnings' -Average 4.00 -Count 3 -CaregiverId $caregiverId -CaregiverName $caregiverName)
    $plan += New-F7HttpStep -Name 'consumer.top-rated-baseline' -Method 'GET' -Path $topRatedRoute `
        -Actor 'family' -Token $familyToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the family top-10 aggregates the visible ratings itself and agrees with the stored aggregate' `
        -Assert (New-F7ConsumerAssert -Kind 'topRated' -Average 4.00 -Count 3 -CaregiverId $caregiverId -CaregiverName $caregiverName)

    # -- Section 10: hiding the 3-star caregiver rating with a 500-character
    #    reason, and restoring it ---------------------------------------------
    $plan += New-F7HttpStep -Name 'admin.hide-cgr3-500-reason' -Method 'POST' -Path "$caregiverAdmin/$($caregiverRatingId['cgr3'])/hide" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $reason500Body -ExpectedStatus 204 `
        -Expected 'HTTP 204: the oldest caregiver rating is hidden with a reason of exactly the 500-character bound' `
        -Assert $helpers.Problem
    $plan += New-F7SqlStep -Name 'sql.caregiver-hidden-cgr3' -Tag 'caregiver-hidden-cgr3' `
        -Expected 'the 3-star caregiver rating is hidden with its full reason and attribution, the stored caregiver aggregate has already been recomputed to 4.50 over 2 and matches the SQL-computed one, and one action row was appended' `
        -Assert (New-F7StateExpectation -Capture $Capture -Expect @{
            cgr3State = '1'
            cgr3HiddenByUserId = $supportAdminUserId
            cgr3HiddenOnPresent = 'true'
            cgr3HideReason = $reason500
            cgr3HideReasonLength = '500'
            cgr3Stars = '3'
            cgr3ReviewText = $caregiverReview['cgr3']
            caregiverHiddenRatingCount = '1'
            caregiverVisibleCount = '2'
            caregiverRatingRowCount = '3'
            caregiverStoredCount = '2'
            caregiverLastActionType = '0'
            caregiverLastActionActor = $supportAdminUserId
            caregiverLastActionRatingId = $caregiverRatingId['cgr3']
            caregiverLastActionReason = $reason500
            caregiverLastActionReasonLength = '500'
        } -Agree @{
            caregiverStoredAverage = 'caregiverVisibleAverage'
        } -CounterDelta @{
            caregiverActionRowCount = @('caregiverActionBase', 1)
        })
    $plan += New-F7SqlStep -Name 'sql.caregiver-hidden-cgr3-average' -Tag 'caregiver-hidden-cgr3-average' `
        -Expected 'the recomputed stored aggregate is 4.50 over 2, not the pre-hide 4.00 over 3' `
        -Assert (New-F7StateExpectation -Expect @{ caregiverStoredAverage = '4.50'; caregiverStoredCount = '2' })

    $plan += New-F7HttpStep -Name 'consumer.profile-cgr3-hidden' -Method 'GET' -Path "$caregiversRoute/$caregiverId" `
        -Actor 'family' -Token $familyToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: hiding the 3-star rating moved the public profile to 4.50 over 2' `
        -Assert (New-F7ConsumerAssert -Kind 'profile' -Average 4.50 -Count 2 -CaregiverId $caregiverId -CaregiverName $caregiverName)
    $plan += New-F7HttpStep -Name 'consumer.search-cgr3-hidden' -Method 'GET' -Path "${caregiversRoute}?search=$searchQuery&pageSize=10" `
        -Actor 'family' -Token $familyToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the search card moved with the stored aggregate' `
        -Assert (New-F7ConsumerAssert -Kind 'search' -Average 4.50 -Count 2 -CaregiverId $caregiverId -CaregiverName $caregiverName)
    # The minRating floor is the discriminator: 4.4 is above the canonical 4.00
    # the baseline search did not match and below the 4.50 the hide rewrote, so
    # the same request proves the filter reads the stored aggregate.
    $plan += New-F7HttpStep -Name 'consumer.search-min-rating-cgr3-hidden' -Method 'GET' -Path "${caregiversRoute}?search=$searchQuery&minRating=4.4" `
        -Actor 'family' -Token $familyToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the stored aggregate is now 4.50, above the 4.4 floor the 4.00 baseline did not pass, so the filter reads what the hide rewrote' `
        -Assert (New-F7ConsumerAssert -Kind 'search' -Average 4.50 -Count 2 -CaregiverId $caregiverId -CaregiverName $caregiverName)
    $plan += New-F7HttpStep -Name 'consumer.earnings-cgr3-hidden' -Method 'GET' -Path "$caregiversRoute/$caregiverId/earnings/summary" `
        -Actor 'caregiver' -Token $caregiverToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the earnings summary moved with the stored aggregate' `
        -Assert (New-F7ConsumerAssert -Kind 'earnings' -Average 4.50 -Count 2 -CaregiverId $caregiverId -CaregiverName $caregiverName)
    $plan += New-F7HttpStep -Name 'consumer.top-rated-cgr3-hidden' -Method 'GET' -Path $topRatedRoute `
        -Actor 'family' -Token $familyToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the top-10 SQL aggregate excludes the hidden rating and agrees with the stored one' `
        -Assert (New-F7ConsumerAssert -Kind 'topRated' -Average 4.50 -Count 2 -CaregiverId $caregiverId -CaregiverName $caregiverName)
    $plan += New-F7HttpStep -Name 'admin.read-cgr3-hidden' -Method 'GET' -Path "${caregiverAdmin}?caregiverId=$caregiverId&moderation=hidden" `
        -Actor 'support-admin' -Token $supportAdminToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the admin still sees the hidden rating itself, with its content, attribution and full reason' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 1 `
            -ItemProperty $caregiverItemProperties -ExactItemProperties -ForbiddenToken $caregiverForbidden `
            -ExpectedItemStarOrder '3' -ItemCheck $caregiverHiddenLongReasonItemCheck)

    $plan += New-F7HttpStep -Name 'admin.restore-cgr3' -Method 'POST' -Path "$caregiverAdmin/$($caregiverRatingId['cgr3'])/restore" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $restoreBody -ExpectedStatus 204 `
        -Expected 'HTTP 204: the hidden caregiver rating is restored and the stored aggregate is recomputed again' `
        -Assert $helpers.Problem
    $plan += New-F7SqlStep -Name 'sql.caregiver-restored-cgr3' -Tag 'caregiver-restored-cgr3' `
        -Expected 'the rating is visible with every hide fact cleared, the stored aggregate is back to 4.00 over 3 and agrees with the SQL-computed one, and the append-only row still carries the 500-character hide reason' `
        -Assert (New-F7StateExpectation -Capture $Capture -Expect @{
            cgr3State = '0'
            cgr3HiddenByUserId = ''
            cgr3HiddenOnPresent = 'false'
            cgr3HideReason = ''
            cgr3HideReasonLength = '0'
            cgr3ReviewText = $caregiverReview['cgr3']
            caregiverHiddenRatingCount = '0'
            caregiverVisibleCount = '3'
            caregiverStoredCount = '3'
            caregiverLastActionType = '1'
            caregiverLastActionActor = $supportAdminUserId
            caregiverLastActionRatingId = $caregiverRatingId['cgr3']
            caregiverLastActionReason = $restoreReason
            caregiverLastActionReasonLength = "$($restoreReason.Length)"
        } -Agree @{
            caregiverStoredAverage = 'caregiverVisibleAverage'
        } -CounterDelta @{
            caregiverActionRowCount = @('caregiverActionBase', 2)
        })
    $plan += New-F7HttpStep -Name 'consumer.profile-cgr3-restored' -Method 'GET' -Path "$caregiversRoute/$caregiverId" `
        -Actor 'family' -Token $familyToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the restored rating brought the public profile back to 4.00 over 3' `
        -Assert (New-F7ConsumerAssert -Kind 'profile' -Average 4.00 -Count 3 -CaregiverId $caregiverId -CaregiverName $caregiverName)
    $plan += New-F7HttpStep -Name 'consumer.search-min-rating-cgr3-restored' -Method 'GET' -Path "${caregiversRoute}?search=$searchQuery&minRating=4.4" `
        -Actor 'family' -Token $familyToken -ExpectedStatus 200 `
        -Expected 'HTTP 200 with an empty page: the restored 4.00 aggregate is below the 4.4 floor again, so the filter reads what the restore rewrote' `
        -Assert (New-F7JsonAssert -ActualText 'the minRating filter matched nothing' -Check $emptySearchPageCheck)

    # -- Section 11: a hidden rating survives a family re-rating -------------
    $plan += New-F7HttpStep -Name 'admin.hide-cgr1' -Method 'POST' -Path "$caregiverAdmin/$($caregiverRatingId['cgr1'])/hide" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $hideBody -ExpectedStatus 204 `
        -Expected 'HTTP 204: the 5-star caregiver rating is hidden' `
        -Assert $helpers.Problem
    $plan += New-F7SqlStep -Name 'sql.caregiver-hidden-cgr1' -Tag 'caregiver-hidden-cgr1' `
        -Expected 'the 5-star rating is hidden and the stored aggregate is 3.50 over 2, equal to the SQL-computed one, with one hide row appended' `
        -Assert (New-F7StateExpectation -Capture $Capture -Expect @{
            cgr1State = '1'
            cgr1HiddenByUserId = $supportAdminUserId
            cgr1HiddenOnPresent = 'true'
            cgr1HideReason = $hideReason
            cgr1HideReasonLength = "$($hideReason.Length)"
            cgr1Stars = '5'
            cgr1ReviewText = $caregiverReview['cgr1']
            caregiverHiddenRatingCount = '1'
            caregiverVisibleCount = '2'
            caregiverStoredCount = '2'
            caregiverLastActionType = '0'
            caregiverLastActionRatingId = $caregiverRatingId['cgr1']
            caregiverLastActionReason = $hideReason
        } -Agree @{
            caregiverStoredAverage = 'caregiverVisibleAverage'
        } -CounterDelta @{
            caregiverActionRowCount = @('caregiverActionBase', 3)
        })
    $plan += New-F7SqlStep -Name 'sql.caregiver-hidden-cgr1-average' -Tag 'caregiver-hidden-cgr1-average' `
        -Expected 'the recomputed stored aggregate is 3.50 over 2' `
        -Assert (New-F7StateExpectation -Expect @{ caregiverStoredAverage = '3.50'; caregiverStoredCount = '2' })

    $plan += New-F7HttpStep -Name 'consumer.profile-cgr1-hidden' -Method 'GET' -Path "$caregiversRoute/$caregiverId" `
        -Actor 'family' -Token $familyToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the public profile reads the recomputed 3.50 over 2' `
        -Assert (New-F7ConsumerAssert -Kind 'profile' -Average 3.50 -Count 2 -CaregiverId $caregiverId -CaregiverName $caregiverName)
    $plan += New-F7HttpStep -Name 'consumer.search-cgr1-hidden' -Method 'GET' -Path "${caregiversRoute}?search=$searchQuery&pageSize=10" `
        -Actor 'family' -Token $familyToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the search card reads the stored aggregate' `
        -Assert (New-F7ConsumerAssert -Kind 'search' -Average 3.50 -Count 2 -CaregiverId $caregiverId -CaregiverName $caregiverName)
    $plan += New-F7HttpStep -Name 'consumer.earnings-cgr1-hidden' -Method 'GET' -Path "$caregiversRoute/$caregiverId/earnings/summary" `
        -Actor 'caregiver' -Token $caregiverToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the earnings summary reads the stored aggregate' `
        -Assert (New-F7ConsumerAssert -Kind 'earnings' -Average 3.50 -Count 2 -CaregiverId $caregiverId -CaregiverName $caregiverName)
    $plan += New-F7HttpStep -Name 'consumer.top-rated-cgr1-hidden' -Method 'GET' -Path $topRatedRoute `
        -Actor 'family' -Token $familyToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the family top-10 excludes the hidden rating and still agrees with the stored aggregate' `
        -Assert (New-F7ConsumerAssert -Kind 'topRated' -Average 3.50 -Count 2 -CaregiverId $caregiverId -CaregiverName $caregiverName)

    $plan += New-F7HttpStep -Name 'family.edit-hidden-rating' -Method 'PUT' -Path "$ratingRoute/$booking1" `
        -Actor 'family' -Token $familyToken -Body $familyEditBody -ExpectedStatus 200 `
        -Expected 'HTTP 200: the family edits the rating behind the hidden one, and the edit is stored without changing its visibility' `
        -Assert (New-F7JsonAssert -ActualText 'ratingId and stars only' -Check ({
            param($body)
            $problems = @()
            if ([string]$body.ratingId -ne $caregiverRatingId['cgr1']) { $problems += "ratingId is '$($body.ratingId)', expected the fixture rating" }
            if ([int]$body.stars -ne 1) { $problems += "stars is $($body.stars), expected 1" }
            return $problems
        }).GetNewClosure())
    $plan += New-F7SqlStep -Name 'sql.caregiver-cgr1-edited-while-hidden' -Tag 'caregiver-cgr1-edited-while-hidden' `
        -Expected 'the family edit changed the stars and text and the updated instant, but the rating is still hidden with its attribution and reason, so the edit added no action row and did not move the aggregate' `
        -Assert (New-F7StateExpectation -Capture $Capture -Expect @{
            cgr1State = '1'
            cgr1HiddenByUserId = $supportAdminUserId
            cgr1HiddenOnPresent = 'true'
            cgr1HideReason = $hideReason
            cgr1Stars = '1'
            cgr1ReviewText = $familyEditReview
            cgr1ReviewTextLength = "$($familyEditReview.Length)"
            cgr1UpdatedAfterCreated = 'true'
            caregiverHiddenRatingCount = '1'
            caregiverVisibleCount = '2'
            caregiverStoredCount = '2'
            caregiverLastActionType = '0'
            caregiverLastActionRatingId = $caregiverRatingId['cgr1']
            caregiverLastActionReason = $hideReason
        } -Agree @{
            caregiverStoredAverage = 'caregiverVisibleAverage'
        } -CounterDelta @{
            caregiverActionRowCount = @('caregiverActionBase', 3)
        })
    $plan += New-F7HttpStep -Name 'admin.read-cgr1-hidden-after-edit' -Method 'GET' -Path "${caregiverAdmin}?caregiverId=$caregiverId&moderation=hidden" `
        -Actor 'support-admin' -Token $supportAdminToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the admin sees the edited content of the still-hidden rating with its moderation facts unchanged' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 1 `
            -ItemProperty $caregiverItemProperties -ExactItemProperties -ForbiddenToken $caregiverForbidden `
            -ExpectedItemStarOrder '1' -ItemCheck $caregiverHiddenAfterEditItemCheck)
    $plan += New-F7HttpStep -Name 'consumer.profile-after-family-edit' -Method 'GET' -Path "$caregiversRoute/$caregiverId" `
        -Actor 'family' -Token $familyToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the family edit did not unhide the rating, so the aggregate is still 3.50 over 2' `
        -Assert (New-F7ConsumerAssert -Kind 'profile' -Average 3.50 -Count 2 -CaregiverId $caregiverId -CaregiverName $caregiverName)
    $plan += New-F7HttpStep -Name 'consumer.top-rated-after-family-edit' -Method 'GET' -Path $topRatedRoute `
        -Actor 'family' -Token $familyToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the edit did not bring the rating back into the top-10 aggregate either' `
        -Assert (New-F7ConsumerAssert -Kind 'topRated' -Average 3.50 -Count 2 -CaregiverId $caregiverId -CaregiverName $caregiverName)

    $plan += New-F7HttpStep -Name 'admin.restore-cgr1' -Method 'POST' -Path "$caregiverAdmin/$($caregiverRatingId['cgr1'])/restore" `
        -Actor 'support-admin' -Token $supportAdminToken -Body $restoreBody -ExpectedStatus 204 `
        -Expected 'HTTP 204: the edited rating is restored, so the two-thirds over three aggregate of the edited set is now what every consumer reads' `
        -Assert $helpers.Problem
    $plan += New-F7SqlStep -Name 'sql.caregiver-restored-cgr1' -Tag 'caregiver-restored-cgr1' `
        -Expected 'the rating is visible with the family''s edited content and no hide fact, and the stored aggregate (2.67 over 3) equals the SQL-computed one' `
        -Assert (New-F7StateExpectation -Capture $Capture -Expect @{
            cgr1State = '0'
            cgr1HiddenByUserId = ''
            cgr1HiddenOnPresent = 'false'
            cgr1HideReason = ''
            cgr1HideReasonLength = '0'
            cgr1Stars = '1'
            cgr1ReviewText = $familyEditReview
            caregiverHiddenRatingCount = '0'
            caregiverVisibleCount = '3'
            caregiverStoredCount = '3'
            caregiverStoredAverage = '2.67'
            caregiverLastActionType = '1'
            caregiverLastActionActor = $supportAdminUserId
            caregiverLastActionRatingId = $caregiverRatingId['cgr1']
            caregiverLastActionReason = $restoreReason
            caregiverLastActionReasonLength = "$($restoreReason.Length)"
        } -Agree @{
            caregiverStoredAverage = 'caregiverVisibleAverage'
        } -CounterDelta @{
            caregiverActionRowCount = @('caregiverActionBase', 4)
        })
    $plan += New-F7HttpStep -Name 'consumer.profile-after-restore-edit' -Method 'GET' -Path "$caregiversRoute/$caregiverId" `
        -Actor 'family' -Token $familyToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the restored rating is back in the aggregate, so the profile reads 2.67 over 3' `
        -Assert (New-F7ConsumerAssert -Kind 'profile' -Average 2.67 -Count 3 -CaregiverId $caregiverId -CaregiverName $caregiverName)
    $plan += New-F7HttpStep -Name 'consumer.search-after-restore-edit' -Method 'GET' -Path "${caregiversRoute}?search=$searchQuery&pageSize=10" `
        -Actor 'family' -Token $familyToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the search card reads the same 2.67 over 3' `
        -Assert (New-F7ConsumerAssert -Kind 'search' -Average 2.67 -Count 3 -CaregiverId $caregiverId -CaregiverName $caregiverName)
    $plan += New-F7HttpStep -Name 'consumer.earnings-after-restore-edit' -Method 'GET' -Path "$caregiversRoute/$caregiverId/earnings/summary" `
        -Actor 'caregiver' -Token $caregiverToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the earnings summary reads the same 2.67' `
        -Assert (New-F7ConsumerAssert -Kind 'earnings' -Average 2.67 -Count 3 -CaregiverId $caregiverId -CaregiverName $caregiverName)
    $plan += New-F7HttpStep -Name 'consumer.top-rated-after-restore-edit' -Method 'GET' -Path $topRatedRoute `
        -Actor 'family' -Token $familyToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the SQL-aggregated top-10 reads the same 2.67 over 3 as the stored aggregate' `
        -Assert (New-F7ConsumerAssert -Kind 'topRated' -Average 2.67 -Count 3 -CaregiverId $caregiverId -CaregiverName $caregiverName)

    $plan += New-F7HttpStep -Name 'family.restore-canonical-rating' -Method 'PUT' -Path "$ratingRoute/$booking1" `
        -Actor 'family' -Token $familyToken -Body $familyCanonicalBody -ExpectedStatus 200 `
        -Expected 'HTTP 200: the family re-rates the booking back to its canonical 5-star fixture content, leaving the fixture in the state every earlier section started from' `
        -Assert (New-F7JsonAssert -ActualText "ratingId=$($caregiverRatingId['cgr1'])" -Check ({
            param($body)
            $problems = @()
            if ([string]$body.ratingId -ne $caregiverRatingId['cgr1']) { $problems += "ratingId is '$($body.ratingId)', expected the fixture rating" }
            if ([int]$body.stars -ne 5) { $problems += "stars is $($body.stars), expected 5" }
            return $problems
        }).GetNewClosure())

    # -- Section 12: the whole fixture agrees again --------------------------
    $plan += New-F7SqlStep -Name 'sql.final-agreement' -Tag 'final-agreement' `
        -Expected 'every fixture rating is visible with its canonical content, no flag is open and no flag field is left over, the stored caregiver aggregate equals the SQL-computed one, and the append-only tables grew by exactly the rows this run appended' `
        -Assert (New-F7StateExpectation -Capture $Capture -Expect @{
            cgr1State = '0'; cgr2State = '0'; cgr3State = '0'
            chr1State = '0'; chr2State = '0'; chr3State = '0'
            cgr1Stars = '5'; cgr2Stars = '4'; cgr3Stars = '3'
            chr1Stars = '5'; chr2Stars = '4'; chr3Stars = '3'
            cgr1HiddenByUserId = ''; cgr2HiddenByUserId = ''; cgr3HiddenByUserId = ''
            chr1HiddenByUserId = ''; chr2HiddenByUserId = ''; chr3HiddenByUserId = ''
            cgr1HiddenOnPresent = 'false'; cgr2HiddenOnPresent = 'false'; cgr3HiddenOnPresent = 'false'
            chr1HiddenOnPresent = 'false'; chr2HiddenOnPresent = 'false'; chr3HiddenOnPresent = 'false'
            cgr1HideReason = ''; cgr2HideReason = ''; cgr3HideReason = ''
            chr1HideReason = ''; chr2HideReason = ''; chr3HideReason = ''
            cgr1HideReasonLength = '0'; cgr2HideReasonLength = '0'; cgr3HideReasonLength = '0'
            chr1HideReasonLength = '0'; chr2HideReasonLength = '0'; chr3HideReasonLength = '0'
            cgr1ReviewText = $caregiverReview['cgr1']; cgr2ReviewText = $caregiverReview['cgr2']; cgr3ReviewText = $caregiverReview['cgr3']
            chr1ReviewText = $careHomeReview['chr1']; chr2ReviewText = $careHomeReview['chr2']; chr3ReviewText = $careHomeReview['chr3']
            cgr1Exists = 'true'; cgr2Exists = 'true'; cgr3Exists = 'true'
            chr1Exists = 'true'; chr2Exists = 'true'; chr3Exists = 'true'
            cgr1UpdatedAfterCreated = 'true'
            caregiverStoredAverage = '4.00'; caregiverStoredCount = '3'
            caregiverVisibleAverage = '4.00'; caregiverVisibleCount = '3'
            caregiverHiddenRatingCount = '0'; caregiverRatingRowCount = '3'
            caregiverShowProfile = 'true'; caregiverShowRating = 'true'
            caregiverStatus = '4'; caregiverAvailability = '1'; caregiverType = '1'
            facility1VisibleRatingCount = '3'; facility1HiddenRatingCount = '0'; facility1RatingRowCount = '3'
            facility1OpenFlagCount = '0'
            facility2RatingRowCount = '0'
            chr1OpenFlagCount = '0'; chr2OpenFlagCount = '0'; chr3OpenFlagCount = '0'
            careHomeOpenFlagRowCount = '0'
            caregiverLastActionType = '1'
            caregiverLastActionReason = $restoreReason
            careHomeActionLastType = '1'
            careHomeActionLastReason = $careHomeRestoreReason
            familyBookingStatus = '5'
            familyBookingCaregiverId = $caregiverId
            caregiverId = $caregiverId
            facility1Id = $facility1Id
            facility1OwnerUserId = $ownerUserId
            familyId = $familyId
        } -Agree @{
            caregiverStoredAverage = 'caregiverVisibleAverage'
            caregiverStoredCount = 'caregiverVisibleCount'
        } -CounterDelta @{
            caregiverActionRowCount = @('caregiverActionBase', 4)
            careHomeActionRowCount = @('careHomeActionBase', 4)
            careHomeFlagRowCount = @('careHomeFlagBase', 3)
            facility1FlagRowCount = @('facility1FlagBase', 3)
            chr1FlagRowCount = @('chr1FlagBase', 2)
            chr2FlagRowCount = @('chr2FlagBase', 1)
            chr3FlagRowCount = @('chr3FlagBase', 0)
        })

    $plan += New-F7HttpStep -Name 'final.consumer.profile' -Method 'GET' -Path "$caregiversRoute/$caregiverId" `
        -Actor 'family' -Token $familyToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the public profile is back to the canonical 4.00 over 3' `
        -Assert (New-F7ConsumerAssert -Kind 'profile' -Average 4.00 -Count 3 -CaregiverId $caregiverId -CaregiverName $caregiverName)
    $plan += New-F7HttpStep -Name 'final.consumer.search' -Method 'GET' -Path "${caregiversRoute}?search=$searchQuery&pageSize=10" `
        -Actor 'family' -Token $familyToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: search is back to the canonical 4.00 over 3' `
        -Assert (New-F7ConsumerAssert -Kind 'search' -Average 4.00 -Count 3 -CaregiverId $caregiverId -CaregiverName $caregiverName)
    $plan += New-F7HttpStep -Name 'final.consumer.search-min-rating' -Method 'GET' -Path "${caregiversRoute}?search=$searchQuery&minRating=3.6" `
        -Actor 'family' -Token $familyToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the canonical stored aggregate passes the 3.6 floor' `
        -Assert (New-F7ConsumerAssert -Kind 'search' -Average 4.00 -Count 3 -CaregiverId $caregiverId -CaregiverName $caregiverName)
    $plan += New-F7HttpStep -Name 'final.consumer.earnings' -Method 'GET' -Path "$caregiversRoute/$caregiverId/earnings/summary" `
        -Actor 'caregiver' -Token $caregiverToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the earnings summary is back to the canonical 4.00' `
        -Assert (New-F7ConsumerAssert -Kind 'earnings' -Average 4.00 -Count 3 -CaregiverId $caregiverId -CaregiverName $caregiverName)
    $plan += New-F7HttpStep -Name 'final.consumer.top-rated' -Method 'GET' -Path $topRatedRoute `
        -Actor 'family' -Token $familyToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the SQL-aggregated top-10 is back in agreement with the stored aggregate' `
        -Assert (New-F7ConsumerAssert -Kind 'topRated' -Average 4.00 -Count 3 -CaregiverId $caregiverId -CaregiverName $caregiverName)

    $plan += New-F7HttpStep -Name 'final.admin.caregiver-ratings' -Method 'GET' -Path "${caregiverAdmin}?caregiverId=$caregiverId" `
        -Actor 'support-admin' -Token $supportAdminToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the admin caregiver page is back to the canonical three visible ratings' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 3 `
            -ItemProperty $caregiverItemProperties -ExactItemProperties -ForbiddenToken $caregiverForbidden `
            -ExpectedItemStarOrder '3,4,5' -ItemCheck $caregiverItemCheck)
    $plan += New-F7HttpStep -Name 'final.admin.care-home-ratings' -Method 'GET' -Path "${careHomeAdmin}?facilityId=$facility1Id" `
        -Actor 'support-admin' -Token $supportAdminToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the admin Care Home page is back to the canonical three visible ratings with no unresolved flag' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 3 `
            -ItemProperty $careHomeItemProperties -ExactItemProperties -ForbiddenToken $caregiverForbidden `
            -ExpectedItemStarOrder '3,4,5' -ItemCheck $careHomeItemCheck)
    $plan += New-F7HttpStep -Name 'final.admin.care-home-hidden-empty' -Method 'GET' -Path "${careHomeAdmin}?facilityId=$facility1Id&moderation=hidden" `
        -Actor 'support-admin' -Token $supportAdminToken -ExpectedStatus 200 `
        -Expected 'HTTP 200 with an empty page: no Care Home rating is left hidden' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 0 -MaxItemCount 0 -PageProperty $pageProperties -ExactPageProperties)
    $plan += New-F7HttpStep -Name 'final.admin.flagged-empty' -Method 'GET' -Path "${careHomeAdmin}?facilityId=$facility1Id&flagged=true" `
        -Actor 'support-admin' -Token $supportAdminToken -ExpectedStatus 200 `
        -Expected 'HTTP 200 with an empty page: every owner flag is resolved, so nothing is left in the flagged work queue' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 0 -MaxItemCount 0 -PageProperty $pageProperties -ExactPageProperties)
    $plan += New-F7HttpStep -Name 'final.owner.read' -Method 'GET' -Path $ownerRoute `
        -Actor 'owner' -Token $ownerToken -ExpectedStatus 200 `
        -Expected 'HTTP 200: the owner page is back to three visible ratings, with both closed cases reported as Resolved and their reasons retained' `
        -Assert (New-F7PageAssert -Page 1 -PageSize 25 -TotalCount 3 -AverageStars 4.00 -RatingCount 3 `
            -PageProperty $ownerPageProperties -ExactPageProperties `
            -ItemProperty $ownerItemProperties -ExactItemProperties -ForbiddenToken $ownerForbidden `
            -ExpectedItemStarOrder '3,4,5' `
            -ItemCheck (New-F7OwnerItemAssert -Expectation $ownerExpectationChr1ResolvedAgain -ReviewByRating $careHomeReviewByRating -ShortByRating $shortByRating))

    # -- Section 13: log every session this run created out again ------------
    foreach ($actor in @('support-admin', 'content-admin', 'finance-admin', 'owner', 'owner2', 'caregiver', 'family')) {
        $plan += New-F7HttpStep -Name "session.logout.$actor" -Method 'POST' -Path $logoutRoute `
            -Actor $actor -Token $Tokens[$actor] -ExpectedStatus 204 `
            -Expected 'HTTP 204, revoking the session this run created' -Assert $helpers.Plain
    }

    return $plan
}

# ---------------------------------------------------------------------------
# Session identities (runtime)
# ---------------------------------------------------------------------------

function Get-F7SessionIdentityDefinition {
    <#
        The signed-in actors this run creates. Every one is logged out again.
        All seven are F7's own dedicated fixtures (the L1 Minimal identity is
        only a read-only data reference for the shared password hash), so this
        gate never depends on how many sessions a shared identity happens to be
        holding.
    #>
    [CmdletBinding()]
    param()

    $definition = Get-F7FixtureDefinition
    return [ordered]@{
        'support-admin' = [ordered]@{ Email = $definition.SupportAdmin.Email; DeviceName = 'f7-ratings-moderation-support-admin'; NewSession = $true }
        'content-admin' = [ordered]@{ Email = $definition.ContentAdmin.Email; DeviceName = 'f7-ratings-moderation-content-admin'; NewSession = $true }
        'finance-admin' = [ordered]@{ Email = $definition.FinanceAdmin.Email; DeviceName = 'f7-ratings-moderation-finance-admin'; NewSession = $true }
        'owner'         = [ordered]@{ Email = $definition.Owner.Email; DeviceName = 'f7-ratings-moderation-owner'; NewSession = $true }
        'owner2'        = [ordered]@{ Email = $definition.Owner2.Email; DeviceName = 'f7-ratings-moderation-owner-two'; NewSession = $true }
        'caregiver'     = [ordered]@{ Email = $definition.Caregiver.Email; DeviceName = 'f7-ratings-moderation-caregiver'; NewSession = $true }
        'family'        = [ordered]@{ Email = $definition.Family.Email; DeviceName = 'f7-ratings-moderation-family'; NewSession = $true }
    }
}

# ---------------------------------------------------------------------------
# psql helpers (runtime)
# ---------------------------------------------------------------------------

function Get-F7PsqlPath {
    <#
        Locates the local PostgreSQL client. A missing client fails the run
        closed before any write or read occurs.
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

function Get-F7SafeOutputSummary {
    [CmdletBinding()]
    param([string[]]$Lines, [int]$MaxLength = 300)

    $all = @($Lines)
    for ($index = $all.Count - 1; $index -ge 0; $index--) {
        if ([string]::IsNullOrWhiteSpace($all[$index])) { continue }
        return (Get-TrimmedRedactedLine -Text $all[$index] -MaxLength $MaxLength)
    }
    return 'no client output'
}

function Invoke-F7Psql {
    <#
        Runs one ASCII-only SQL script against the resolved local database with
        the connection supplied through the standard connection environment,
        then restores the caller environment. With -ExpectJson the single JSON
        line the readback prints is parsed and also returned raw, because the L1
        outcome evaluator re-parses the body text exactly like an HTTP response.
    #>
    [CmdletBinding()]
    param(
        [string]$PsqlPath,
        [hashtable]$Connection,
        [string]$Sql,
        [string]$Tag,
        [switch]$ExpectJson
    )

    $tempFile = Join-Path ([System.IO.Path]::GetTempPath()) ("sanad-f7-" + $Tag + "-" + [Guid]::NewGuid().ToString('N') + ".sql")
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
        return @{ Ok = $false; Value = $null; Raw = ''; ErrorSummary = "psql exited with $exitCode`: $(Get-F7SafeOutputSummary -Lines $output)" }
    }

    if (-not $ExpectJson) {
        return @{ Ok = $true; Value = $null; Raw = ''; ErrorSummary = '' }
    }

    # The readback prints one line that starts with the opening brace; anything
    # the client printed before it (a notice, a warning) is not the value and
    # must never be parsed as one.
    # The cast is deliberate: a single captured line would otherwise arrive as a
    # scalar string, so $lines[$index] would be a character and Trim() would throw.
    $lines = [string[]]@($output | ForEach-Object { [string]$_ })
    $jsonLine = $null
    for ($index = $lines.Count - 1; $index -ge 0; $index--) {
        $candidate = $lines[$index].Trim()
        if ($candidate.StartsWith('{')) { $jsonLine = $candidate; break }
    }

    if ([string]::IsNullOrWhiteSpace($jsonLine)) {
        return @{ Ok = $false; Value = $null; Raw = ''; ErrorSummary = "the read-only query printed no JSON value: $(Get-F7SafeOutputSummary -Lines $output)" }
    }

    $value = ConvertFrom-SafeJson $jsonLine
    if ($null -eq $value) {
        return @{ Ok = $false; Value = $null; Raw = $jsonLine; ErrorSummary = 'the read-only query did not return parseable JSON' }
    }

    return @{ Ok = $true; Value = $value; Raw = $jsonLine; ErrorSummary = '' }
}

# ---------------------------------------------------------------------------
# Step runner
# ---------------------------------------------------------------------------

function Invoke-F7Steps {
    <#
        Runs the whole plan in order. An HTTP step goes through the L1 request
        helper and the L1 outcome evaluator exactly like an F6 check; a SQL step
        runs the local client and feeds the parsed readback through the same
        evaluator with status 0, so both land in one uniform report. A SQL step
        may promote readback values into the shared capture table, which is what
        makes an assertion on an append-only table state a delta this run
        appended rather than a hard-coded row count a crashed earlier run would
        have broken.
    #>
    [CmdletBinding()]
    param(
        [System.Net.Http.HttpClient]$Client,
        [string]$Url,
        [hashtable[]]$Plan,
        [string[]]$LogPath,
        [string]$PsqlPath,
        [hashtable]$Connection,
        [hashtable]$Capture
    )

    $results = @()
    foreach ($step in $Plan) {
        if ($step.Kind -eq 'sql') {
            $read = Invoke-F7Psql -PsqlPath $PsqlPath -Connection $Connection -Sql $step.Sql -Tag 'readback' -ExpectJson
            if (-not $read.Ok) {
                $results += [ordered]@{
                    name             = $step.Name
                    method           = 'SQL'
                    path             = $step.Path
                    actor            = 'psql'
                    expected         = $step.Expected
                    actual           = 'the local readback did not run'
                    passed           = $false
                    problems         = @($read.ErrorSummary)
                    traceId          = $null
                    traceCorrelation = 'not-applicable'
                    localLocation    = $null
                }
                continue
            }

            $readback = $read.Value
            foreach ($key in @($step.Capture.Keys)) {
                $Capture[$key] = [string]$readback.($step.Capture[$key])
            }

            $results += Get-CheckOutcome -Check $step -Status 0 -BodyText $read.Raw -TraceId $null -Location $null
            continue
        }

        $response = Invoke-SanadHttpRequest -Client $Client -Method $step.Method -Url "$Url$($step.Path)" -Body $step.Body -Token $step.Token
        $parsed = ConvertFrom-SafeJson $response.Body
        $traceId = $null
        if ($null -ne $parsed -and $parsed -isnot [System.Array] -and $parsed.PSObject.Properties.Name -contains 'traceId') {
            $traceId = [string]$parsed.traceId
        }

        # The response has already been produced, so the matching server log
        # line - when one exists - is on disk now. Read the captured log per
        # failure rather than up front, and always with a share mode that
        # tolerates the API still writing to it.
        $location = $null
        if ($traceId) {
            $location = Get-TraceLocation -TraceId $traceId -LogLines (Get-CapturedLogLines -Path $LogPath)
        }

        $results += Get-CheckOutcome -Check $step -Status $response.Status -BodyText $response.Body -TraceId $traceId -Location $location
    }

    return $results
}

# ---------------------------------------------------------------------------
# Result writer
# ---------------------------------------------------------------------------

function Write-F7Result {
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
        workflow           = 'f7-ratings-visibility-moderation-verification'
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
            'The fixture rows are retained verification data: nothing this script writes is updated, deleted or reset. A rerun repairs only the visibility facts it depends on, with the published restore/keep verbs and an idempotent family re-rating.',
            'The moderation-action and owner-flag tables are append-only, so their row counts are asserted as deltas from a capture taken inside this run rather than as absolute numbers.',
            'The family top-10 consensus check needs the fixture caregiver to be inside the local top ten, and the "no open flag anywhere" assertions need the local database to hold no unrelated open flag: both hold on the verified local fixture target.',
            'Trace correlation is reported as unavailable when no captured local log line carries the trace id of a failed check.'
        )
    }
}

# ---------------------------------------------------------------------------

if ($f7LibraryOnly) { return }

# ---------------------------------------------------------------------------
# Main flow
# ---------------------------------------------------------------------------

$repoRoot = Get-RepositoryRoot

if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'AGENTS.md') -PathType Leaf)) {
    throw 'Run this script from the Sanad API repository checkout.'
}

$head = Get-PinnedHead $repoRoot
$uri = Assert-LoopbackBaseUrl $f7BaseUrl
$baseUrl = "$($uri.Scheme)://$($uri.Authority)"

$apiDirectory = Join-Path $repoRoot 'src/API/Sanad.API'
if ([string]::IsNullOrWhiteSpace($f7ApiAssemblyPath)) {
    $f7ApiAssemblyPath = Join-Path $apiDirectory 'bin/Release/net10.0/Sanad.API.dll'
}
$assemblyPath = Resolve-FullPath $f7ApiAssemblyPath
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw 'The built API assembly was not found. Build Release first (dotnet build Sanad.slnx -c Release).'
}

# The verification and the seed look at the same pinned local target; the
# serving API never re-seeds.
$seedEnvironment = Get-LocalFixtureEnvironment -Password $f7FixturePassword -EnableSeed
$serveEnvironment = Get-LocalFixtureEnvironment -Password $f7FixturePassword

$failureNotes = @()
$checkResults = @()
$tokens = @{}
$capture = @{}
$fixtureState = $null
$apiLogPaths = @()
$apiProcess = $null
$canRunChecks = $false
$client = $null
$apiReused = Test-BaseUrlReachable -Url $baseUrl

# 1. Read-only target verification before any write.
$preflight = Invoke-TargetVerification -RepoRoot $repoRoot -AssemblyPath $assemblyPath -Environment $seedEnvironment -ApiDirectory $apiDirectory

$psqlPath = Get-F7PsqlPath
$connectionSource = Resolve-F7ConnectionStringSource -RepoRoot $repoRoot
$connection = ConvertFrom-F7PostgresConnectionString -ConnectionString $connectionSource.Value
if (@($connection.Problems).Count -gt 0) {
    $failureNotes += ('The local database connection could not be resolved: ' + (@($connection.Problems) -join '; '))
}

$guard = Test-F7FixtureTarget -VerificationPassed $preflight.passed -Report $preflight.report -Connection $connection -PsqlPath $psqlPath

try {
    if (-not $preflight.passed -or -not $guard.Ok) {
        $failureNotes += 'The read-only target verification or the fixture guard failed; no fixture write, no API start and no HTTP check occurred.'
        Write-F7Result -Passed $false -Phase 'target-verification' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    if ($f7SkipFixtureWrite) {
        $reportPath = Join-Path ([System.IO.Path]::GetTempPath()) ('sanad-f7-fixtures-' + [Guid]::NewGuid().ToString('N') + '.sql')
        [System.IO.File]::WriteAllText($reportPath, (Get-F7FixtureSql), (New-Object System.Text.UTF8Encoding($false)))
        $failureNotes += "SkipFixtureWrite is set: the provisioning SQL was written to $reportPath and nothing else ran."
        Write-F7Result -Passed $false -Phase 'report-only' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    # 2. Provision the fixtures (INSERT-only and guarded), then read them back.
    $fixtureWrite = Invoke-F7Psql -PsqlPath $psqlPath -Connection $connection -Sql (Get-F7FixtureSql) -Tag 'provision'
    if (-not $fixtureWrite.Ok) {
        $failureNotes += ('The fixture provisioning failed: ' + $fixtureWrite.ErrorSummary)
        Write-F7Result -Passed $false -Phase 'fixture-provisioning' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    $stateRead = Invoke-F7Psql -PsqlPath $psqlPath -Connection $connection -Sql (Get-F7StateQuery) -Tag 'state' -ExpectJson
    if (-not $stateRead.Ok) {
        $failureNotes += ('The fixture readback failed: ' + $stateRead.ErrorSummary)
        Write-F7Result -Passed $false -Phase 'fixture-readback' -FixtureReport $null | ConvertTo-Json -Depth 8
        exit 1
    }

    $fixtureState = $stateRead.Value
    $definition = Get-F7FixtureDefinition

    $fixtureProblems = @()
    if ([string]$fixtureState.passwordSourceUserId -eq '' -or [int]$fixtureState.minimalFixturePresentCount -lt 1) {
        $fixtureProblems += 'the shared L1 Minimal fixture identity the fixture password hash is copied from is absent'
    }
    foreach ($actor in @(
            [ordered]@{ Name = 'supportAdmin'; Expected = $definition.SupportAdmin }
            [ordered]@{ Name = 'contentAdmin'; Expected = $definition.ContentAdmin }
            [ordered]@{ Name = 'financeAdmin'; Expected = $definition.FinanceAdmin }
            [ordered]@{ Name = 'owner'; Expected = $definition.Owner }
            [ordered]@{ Name = 'owner2'; Expected = $definition.Owner2 }
            [ordered]@{ Name = 'caregiver'; Expected = $definition.Caregiver }
            [ordered]@{ Name = 'family'; Expected = $definition.Family })) {
        $prefix = $actor.Name
        if ([string]$fixtureState."${prefix}UserId" -ne [string]$actor.Expected.UserId) {
            $fixtureProblems += "the $prefix identity was not provisioned with the expected id"
        }
        if ([int]$fixtureState."${prefix}IdentityStatus" -ne 2) {
            $fixtureProblems += "the $prefix identity is not active"
        }
        if (@(([string]$fixtureState."${prefix}AccountTypes") -split ',') -notcontains [string]$actor.Expected.AccountType) {
            $fixtureProblems += "the $prefix identity does not hold the expected account type"
        }
    }
    if ([string]$fixtureState.caregiverId -ne $definition.CaregiverProfile.Id -or
        [string]$fixtureState.caregiverUserId -ne $definition.Caregiver.UserId) {
        $fixtureProblems += 'the fixture caregiver profile was not provisioned'
    }
    if ([string]$fixtureState.familyId -ne $definition.FamilyActive.Id -or
        [string]$fixtureState.familyOwnerUserId -ne $definition.Family.UserId) {
        $fixtureProblems += 'the fixture family was not provisioned'
    }
    if ([string]$fixtureState.familyBookingId -ne $definition.CaregiverBookings['caregiverBooking1'] -or
        [string]$fixtureState.familyBookingCaregiverId -ne $definition.CaregiverProfile.Id) {
        $fixtureProblems += 'the fixture completed family booking was not provisioned'
    }
    if ([string]$fixtureState.facility1Id -ne $definition.Facility1.Id -or
        [string]$fixtureState.facility1OwnerUserId -ne $definition.Owner.UserId) {
        $fixtureProblems += 'the first fixture facility was not provisioned for the fixture owner'
    }
    if ([string]$fixtureState.facility2Id -ne $definition.Facility2.Id -or
        [string]$fixtureState.facility2OwnerUserId -ne $definition.Owner2.UserId) {
        $fixtureProblems += 'the second fixture facility was not provisioned for the second fixture owner'
    }
    foreach ($rating in $definition.CaregiverRatings) {
        if ([string]$fixtureState.($rating.Short + 'Exists') -ne 'true') {
            $fixtureProblems += "the fixture caregiver rating $($rating.Short) is absent"
        }
    }
    foreach ($rating in $definition.CareHomeRatings) {
        if ([string]$fixtureState.($rating.Short + 'Exists') -ne 'true') {
            $fixtureProblems += "the fixture Care Home rating $($rating.Short) is absent"
        }
    }
    if ([int]$fixtureState.caregiverRatingRowCount -ne $definition.CaregiverRatings.Count) {
        $fixtureProblems += 'the fixture caregiver does not carry exactly its three ratings'
    }
    if ([int]$fixtureState.facility1RatingRowCount -ne $definition.CareHomeRatings.Count) {
        $fixtureProblems += 'the first fixture facility does not carry exactly its three ratings'
    }
    if ([int]$fixtureState.facility2RatingRowCount -ne 0) {
        $fixtureProblems += 'the second fixture facility unexpectedly carries a rating'
    }
    if ([int]$fixtureState.careHomeOpenFlagRowCount -ne 0) {
        $fixtureProblems += 'an unrelated open owner flag exists in the local database, so the flag checks would not be deterministic'
    }

    if ($fixtureProblems.Count -gt 0) {
        $failureNotes += $fixtureProblems
        Write-F7Result -Passed $false -Phase 'fixture-readback' -FixtureReport $fixtureState | ConvertTo-Json -Depth 8
        exit 1
    }

    # 3. Start or reuse the API once.
    $apiLogPaths = @()
    if (-not $apiReused -and -not $f7SkipApiStart) {
        $logDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sanad-f7-verify-' + [Guid]::NewGuid().ToString('N'))
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
            Write-F7Result -Passed $false -Phase 'api-start' -FixtureReport $fixtureState | ConvertTo-Json -Depth 8
            exit 1
        }

        $apiLogPaths = @($outputLog, $errorLog)
    }

    $canRunChecks = $true
    $client = New-SanadHttpClient

    # 4. Login preflight for every actor this run needs, then one ordered pass
    #    over the whole plan (session logout checks last).
    $sessions = Get-F7SessionIdentityDefinition
    foreach ($actor in $sessions.Keys) {
        $tokens[$actor] = Get-FixtureToken -Client $client -Url $baseUrl `
            -Email $sessions[$actor].Email -Password $f7FixturePassword -DeviceName $sessions[$actor].DeviceName
    }

    $plan = New-F7StepPlan -Tokens $tokens -Baseline $fixtureState -Capture $capture -UnknownGuid ([Guid]::NewGuid().ToString())
    $checkResults += Invoke-F7Steps -Client $client -Url $baseUrl -Plan $plan -LogPath $apiLogPaths `
        -PsqlPath $psqlPath -Connection $connection -Capture $capture
}
catch {
    $failureNotes += ('The verification could not complete: ' + (Get-TrimmedRedactedLine $_.Exception.Message))
    if ($canRunChecks -and $tokens.Count -gt 0 -and $checkResults.Count -eq 0) {
        # The plan never ran: log out what was created so a failed run cannot
        # leave the fixture identities holding sessions.
        try {
            $logoutOnly = @(New-F7StepPlan -Tokens $tokens -Baseline $fixtureState -Capture $capture -UnknownGuid ([Guid]::NewGuid().ToString()) |
                Where-Object { $_.Name -like 'session.logout.*' })
            $checkResults += Invoke-F7Steps -Client $client -Url $baseUrl -Plan $logoutOnly -LogPath $apiLogPaths `
                -PsqlPath $psqlPath -Connection $connection -Capture $capture
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

Write-F7Result -Passed $overallPassed -Phase 'complete' -FixtureReport $fixtureState | ConvertTo-Json -Depth 8

if (-not $overallPassed) { exit 1 }
