#requires -Version 5.1
<#
    F2 read-completeness verification (bounded Owner-authorized extension).

    Runs authenticated loopback HTTP GET proofs for the three routes covered by
    the F2 slice:

      * GET /api/v1/care-homes/booking-requests/mine
      * GET /api/v1/admin/care-homes/bookings
      * GET /api/v1/chat/conversations

    The verifier fails closed unless the read-only target verification proves the
    persistent local Development database for all nine module contexts, then it
    provisions the smallest stable synthetic fixture set needed for the proofs
    with INSERT-only SQL executed through the local psql client. Fixture writes
    are deterministic (fixed ids + ON CONFLICT (id) DO NOTHING) and guarded by
    in-transaction prerequisite and read-back blocks that raise and roll back.

    No domain or HTTP path can create a paid care-home booking locally without a
    live payment provider call (the Paymob webhook answers 503 without the local
    HMAC secret), so the fixtures mirror the exact persisted shapes produced by
    CareHomeFacility.CreateDraft, CareHomeRoomType.Create, the booking factories
    and Conversation.Create rather than editing status columns of other rows.

    The script never prints connection strings, credentials, tokens or captured
    payloads. Use -LibraryOnly to dot-source the helpers without running the
    verification.
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:5235',
    [string]$ExpectedWorktreeRoot,
    [string]$ApiAssemblyPath,
    [string]$FixturePassword = 'Test-1234!',
    [switch]$LibraryOnly,
    [switch]$SkipFixtureProvisioning,
    [switch]$SkipApiStart
)

$ErrorActionPreference = 'Stop'

# Dot-sourcing the shared library runs it in this scope, and that library's own
# parameters carry the same names as the ones above, so the dot-source would
# overwrite them (its -LibraryOnly switch would even make this script's own
# guard true). Take private copies first and use only those from here on.
$f2LibraryOnly             = [bool]$LibraryOnly
$f2BaseUrl                 = [string]$BaseUrl
$f2ExpectedWorktreeRoot    = [string]$ExpectedWorktreeRoot
$f2ApiAssemblyPath         = [string]$ApiAssemblyPath
$f2FixturePassword         = [string]$FixturePassword
$f2SkipFixtureProvisioning = [bool]$SkipFixtureProvisioning
$f2SkipApiStart            = [bool]$SkipApiStart

# The L1 verifier library owns the target verification, environment clearing,
# HTTP client, login, check evaluation and redaction helpers reused here.
. (Join-Path $PSScriptRoot 'Invoke-LocalFixtureVerification.ps1') -LibraryOnly

# ---------------------------------------------------------------------------
# Pure helpers (offline-testable; no file system, database or network access)
# ---------------------------------------------------------------------------

function Get-F2FixtureDefinition {
    <#
        The deterministic synthetic fixture set added by this verification.

        Every id is fixed so repeated runs reuse the same rows, and the shape is
        the persisted shape the CareHomes/Chat factories produce: a Draft
        facility with no revision, one shared room type, a paid
        awaiting-decision booking with a live decision deadline, an accepted paid
        base booking plus its extension, and the chat conversations that give
        the provisioned owner identity a direct row plus a booking-scoped row on
        a booking its own facility owns.

        Each conversation carries the party rule its stored participant pair
        must satisfy (see Test-F2ConversationLinkage); 'sqlOnlyLegacy' marks the
        one pre-existing row whose pair the local API could not have produced,
        so it is reported as such instead of being used as domain evidence.
    #>
    [CmdletBinding()]
    param()

    return [ordered]@{
        ownerUserId           = 'f2000000-0000-4000-8000-000000000001'
        ownerAccountId        = 'f2000000-0000-4000-8000-000000000011'
        ownerEmail            = 'carehome.owner.fixture@test.sanad.local'
        ownerPhone            = '+201000000007'
        ownerEnglishName      = 'Test Care Home Owner Fixture'
        ownerAccountType      = 8
        adminUserId           = 'f2000000-0000-4000-8000-000000000002'
        adminAccountId        = 'f2000000-0000-4000-8000-000000000012'
        adminEmail            = 'support.admin.fixture@test.sanad.local'
        adminPhone            = '+201000000008'
        adminEnglishName      = 'Test Support Admin Fixture'
        adminAccountType      = 7
        credentialSourceEmail = 'content.admin@test.sanad.local'
        activeUserStatus      = 2
        facilityId            = 'f2000000-0000-4000-8000-000000000101'
        facilityStatusDraft   = 1
        roomTypeId            = 'f2000000-0000-4000-8000-000000000102'
        roomTypeEnglishName   = 'Test Shared Room'
        roomTypeMonthlyPrice  = '12000.00'
        roomTypeSharedMode    = 1
        awaitingBookingId     = 'f2000000-0000-4000-8000-000000000201'
        baseBookingId         = 'f2000000-0000-4000-8000-000000000202'
        extensionBookingId    = 'f2000000-0000-4000-8000-000000000203'
        awaitingMerchantReference  = 'chb_f2000000000040008000000000000201'
        baseMerchantReference      = 'chb_f2000000000040008000000000000202'
        extensionMerchantReference = 'chb_f2000000000040008000000000000203'
        awaitingBookingStatus = 2
        acceptedBookingStatus = 3
        paidPaymentStatus     = 2
        refundStatusNone      = 0
        awaitingTaxAmount     = '1680.00'
        awaitingTotalAmount   = '14880.00'
        baseTaxAmount         = '2100.00'
        baseTotalAmount       = '18600.00'
        baseVersion           = 3
        extensionVersion      = 3
        elderlyId             = '01a0c6b4-63a6-7779-80ac-faea48242536'
        familyId              = '01a0c6b4-6344-7158-85fb-9427d235be0d'
        elderlyEnglishName    = 'Test Grandfather'
        elderlyAge            = 76
        contactName           = 'Test Contact Fixture'
        contactPhone          = '+201000000099'
        contactRelationship   = 'Son'
        careNeedsNotes        = 'Requires assistance with daily mobility.'
        medicalSnapshotMarker = 'synthetic-medical-snapshot'
        directConversationId  = 'f2000000-0000-4000-8000-000000000301'
        bookingConversationId = 'f2000000-0000-4000-8000-000000000302'
        ownerBookingConversationId = 'f2000000-0000-4000-8000-000000000303'
        ownerFacilityBookingConversationId = 'f2000000-0000-4000-8000-000000000304'
        conversations = @(
            [ordered]@{
                key = 'direct'
                id = 'f2000000-0000-4000-8000-000000000301'
                scope = 0
                partyRule = 'none'
                bookingSnapshotField = ''
                participantRoles = @('fixtureOwner', 'familyOwner')
                lastActivityOffsetHours = 1
            }
            [ordered]@{
                key = 'booking'
                id = 'f2000000-0000-4000-8000-000000000302'
                scope = 1
                partyRule = 'familiesBooking'
                bookingSnapshotField = 'chatBookingId'
                participantRoles = @('familyOwner', 'medicalCaregiver')
                lastActivityOffsetHours = 2
            }
            [ordered]@{
                # Pre-existing verification row: it binds the provisioned care
                # home owner to the reused caregiver booking, a pair that
                # ApiChatBookingGateway could never resolve (a Families booking
                # maps its creator to the booked caregiver). It is kept and
                # reported, but never used as booking-scope domain evidence.
                key = 'ownerBooking'
                id = 'f2000000-0000-4000-8000-000000000303'
                scope = 1
                partyRule = 'sqlOnlyLegacy'
                bookingSnapshotField = 'chatBookingId'
                participantRoles = @('fixtureOwner', 'medicalCaregiver')
                lastActivityOffsetHours = 3
            }
            [ordered]@{
                key = 'ownerFacilityBooking'
                id = 'f2000000-0000-4000-8000-000000000304'
                scope = 1
                partyRule = 'careHomeBooking'
                bookingSnapshotField = 'baseBookingId'
                participantRoles = @('fixtureOwner', 'familyOwner')
                lastActivityOffsetHours = 4
            }
        )
        expectedConversationAccounts = [ordered]@{
            fixtureOwner     = 8
            fixtureAdmin     = 7
            familyOwner      = 1
            medicalCaregiver = 2
            contentAdmin     = 6
        }
    }
}

function Get-F2NormalizedPair {
    <#
        Orders a participant pair exactly as Conversation.NormalizePair does
        (Guid.CompareTo), so a provisioned conversation row is in the canonical
        stored order rather than an arbitrary one.
    #>
    [CmdletBinding()]
    param(
        [string]$First,
        [string]$Second
    )

    $a = [Guid]::Parse($First)
    $b = [Guid]::Parse($Second)
    if ($a.CompareTo($b) -le 0) {
        return [ordered]@{ One = $a.ToString(); Two = $b.ToString() }
    }
    return [ordered]@{ One = $b.ToString(); Two = $a.ToString() }
}

function ConvertTo-F2Utf8Hex {
    <#
        UTF-8 hex of a string. Used to compare stored Arabic values with the
        values a JSON response carries without depending on the console
        encoding of the local psql client.
    #>
    [CmdletBinding()]
    param([AllowNull()][AllowEmptyString()][string]$Value)

    if ($null -eq $Value) { return '' }
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    $builder = New-Object System.Text.StringBuilder
    foreach ($byte in $bytes) {
        [void]$builder.Append($byte.ToString('x2'))
    }
    return $builder.ToString()
}

function ConvertTo-F2Instant {
    <#
        Parses an ISO-8601 instant into a comparable DateTimeOffset, or returns
        $null when the value is absent or unparseable.
    #>
    [CmdletBinding()]
    param([AllowNull()][object]$Value)

    if ($null -eq $Value) { return $null }
    $text = [string]$Value
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    $parsed = [DateTimeOffset]::MinValue
    $styles = [System.Globalization.DateTimeStyles]::AdjustToUniversal -bor [System.Globalization.DateTimeStyles]::AllowWhiteSpaces
    $ok = [DateTimeOffset]::TryParse($text, [System.Globalization.CultureInfo]::InvariantCulture, $styles, [ref]$parsed)
    if (-not $ok) { return $null }
    return $parsed
}

function Test-F2SameInstant {
    <#
        True when two instants are the same up to a one second tolerance, which
        absorbs the differing sub-second precision of the JSON serializer and the
        read-back query.
    #>
    [CmdletBinding()]
    param([AllowNull()][object]$Left, [AllowNull()][object]$Right)

    $l = ConvertTo-F2Instant -Value $Left
    $r = ConvertTo-F2Instant -Value $Right
    if ($null -eq $l -or $null -eq $r) { return $false }
    return ([math]::Abs(($l - $r).TotalSeconds) -lt 1)
}

function ConvertFrom-F2PostgresConnectionString {
    <#
        Minimal Npgsql-style connection string parser for the loopback guard. It
        deliberately returns the parsed values so the guard can assert the target
        before any write; callers must never print the password.
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

function Test-F2FixtureTarget {
    <#
        Fail-closed guard evaluated before any fixture write. It requires the
        read-only target verification to have passed against the persistent local
        Development database for all nine module contexts, and the resolved psql
        connection to point at the same loopback database.
    #>
    [CmdletBinding()]
    param(
        [bool]$VerificationPassed,
        [AllowNull()][object]$Report,
        [AllowNull()][hashtable]$Connection
    )

    $problems = @()
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
        if ([string]::IsNullOrWhiteSpace([string]$Connection.Password)) { $problems += 'the resolved connection carries no password for the local client' }
    }

    return @{ Ok = ($problems.Count -eq 0); Problems = @($problems) }
}

function Get-F2RoleMap {
    <#
        Maps the fixture roles used by the conversation definitions onto the real
        user ids (fixed for the provisioned identities, resolved from the database
        for the pre-existing synthetic ones) and their persisted account types.
    #>
    [CmdletBinding()]
    param([hashtable]$Definition, [object]$Snapshot)

    $accounts = $Definition.expectedConversationAccounts

    return [ordered]@{
        fixtureOwner = [ordered]@{
            userId      = [string]$Definition.ownerUserId
            accountType = [int]$accounts.fixtureOwner
        }
        fixtureAdmin = [ordered]@{
            userId      = [string]$Definition.adminUserId
            accountType = [int]$accounts.fixtureAdmin
        }
        familyOwner = [ordered]@{
            userId      = [string]$Snapshot.familyOwnerUserId
            accountType = [int]$accounts.familyOwner
        }
        medicalCaregiver = [ordered]@{
            userId      = [string]$Snapshot.medicalCaregiverUserId
            accountType = [int]$accounts.medicalCaregiver
        }
        contentAdmin = [ordered]@{
            userId      = [string]$Snapshot.contentAdminUserId
            accountType = [int]$accounts.contentAdmin
        }
    }
}

function Get-F2VisibleConversations {
    <#
        The conversations a requester role participates in, newest stored activity
        first, which is the order the route returns them in.
    #>
    [CmdletBinding()]
    param([hashtable]$Definition, [string]$RequesterRole)

    $visible = @($Definition.conversations | Where-Object { @($_.participantRoles) -contains $RequesterRole })
    $visible = @($visible | Sort-Object -Property lastActivityOffsetHours)
    return $visible
}

function Get-F2ConversationExpectations {
    <#
        Expected conversation rows for one requester role: the stored scope, the
        stored booking reference (empty string for a direct conversation), and the
        counterpart participant identity the route must resolve.
    #>
    [CmdletBinding()]
    param(
        [hashtable]$Definition,
        [hashtable]$Roles,
        [object]$Snapshot,
        [string]$RequesterRole
    )

    $expectations = @()
    foreach ($conversation in (Get-F2VisibleConversations -Definition $Definition -RequesterRole $RequesterRole)) {
        $counterpart = @($conversation.participantRoles | Where-Object { $_ -ne $RequesterRole })
        $counterpartRole = [string]$counterpart[0]
        $bookingId = ''
        if ([int]$conversation.scope -eq 1 -and -not [string]::IsNullOrWhiteSpace([string]$conversation.bookingSnapshotField)) {
            $field = [string]$conversation.bookingSnapshotField
            $bookingId = [string]$Snapshot.$field
        }

        $expectations += [ordered]@{
            key                   = [string]$conversation.key
            id                    = [string]$conversation.id
            scope                 = [int]$conversation.scope
            bookingId             = $bookingId
            counterpartRole       = $counterpartRole
            counterpartUserId     = [string]$Roles[$counterpartRole].userId
            counterpartAccountType = [int]$Roles[$counterpartRole].accountType
        }
    }

    return @($expectations)
}

function Test-F2ConversationLinkage {
    <#
        Re-derives the participant pair a stored booking-scoped conversation
        must have from the booking it references, using the same party rules the
        API applies (ApiChatBookingGateway):

          * a Families booking maps its creator (the family user) to the booked
            caregiver;
          * a Care Home booking maps its FamilyUserId to the owning facility's
            owner.

        A conversation whose stored pair is not that pair is reported as a
        problem: SQL-only fixture rows are never presented as domain-consistent
        authorization evidence. The 'sqlOnlyLegacy' classification returns
        DomainConsistent = $false without a problem, because that row is kept on
        purpose and excluded from the linkage proof.
    #>
    [CmdletBinding()]
    param([hashtable]$Definition, [object]$Snapshot)

    $results = @()

    foreach ($conversation in @($Definition.conversations)) {
        $key = [string]$conversation.key
        $problems = @()
        $domainConsistent = $false
        $partyRule = [string]$conversation.partyRule

        $expectedBookingId = ''
        if ([int]$conversation.scope -eq 1 -and -not [string]::IsNullOrWhiteSpace([string]$conversation.bookingSnapshotField)) {
            $expectedBookingId = [string]$Snapshot.([string]$conversation.bookingSnapshotField)
        }

        $storedBookingId = [string]$Snapshot."$($key)ConversationBookingId"
        $storedOne = [string]$Snapshot."$($key)ConversationParticipantOne"
        $storedTwo = [string]$Snapshot."$($key)ConversationParticipantTwo"

        switch ($partyRule) {
            'none' {
                if ([int]$conversation.scope -ne 0) { $problems += "the '$key' fixture is classified direct but is stored with a booking scope" }
                elseif ($storedBookingId -ne '') { $problems += "the '$key' direct conversation is stored against booking $storedBookingId" }
                else { $domainConsistent = $true }
            }
            'familiesBooking' {
                if ([string]::IsNullOrWhiteSpace([string]$Snapshot.chatBookingCreatedByUserId) -or
                    [string]::IsNullOrWhiteSpace([string]$Snapshot.chatBookingCaregiverUserId)) {
                    $problems += "the '$key' Families booking parties could not be resolved from the database"
                }
                elseif ($expectedBookingId -eq '' -or $storedBookingId -ne $expectedBookingId) {
                    $problems += "the '$key' conversation is stored against booking $storedBookingId, not the booking its party rule resolves"
                }
                else {
                    $pair = Get-F2NormalizedPair -First ([string]$Snapshot.chatBookingCreatedByUserId) -Second ([string]$Snapshot.chatBookingCaregiverUserId)
                    if ($storedOne -ne [string]$pair.One -or $storedTwo -ne [string]$pair.Two) {
                        $problems += "the '$key' conversation pair is not the reused Families booking's creator/caregiver pair"
                    }
                    else { $domainConsistent = $true }
                }
            }
            'careHomeBooking' {
                if ([string]::IsNullOrWhiteSpace([string]$Snapshot.careHomeBookingFamilyUserId) -or
                    [string]::IsNullOrWhiteSpace([string]$Snapshot.careHomeBookingFacilityOwnerUserId)) {
                    $problems += "the '$key' Care Home booking parties could not be resolved from the database"
                }
                elseif ($expectedBookingId -eq '' -or $storedBookingId -ne $expectedBookingId) {
                    $problems += "the '$key' conversation is stored against booking $storedBookingId, not the fixture facility's own booking"
                }
                elseif ([string]$Snapshot.baseBookingFacilityId -ne [string]$Definition.facilityId) {
                    $problems += "the '$key' referenced booking is not owned by the fixture facility"
                }
                else {
                    $pair = Get-F2NormalizedPair -First ([string]$Snapshot.careHomeBookingFamilyUserId) -Second ([string]$Snapshot.careHomeBookingFacilityOwnerUserId)
                    if ($storedOne -ne [string]$pair.One -or $storedTwo -ne [string]$pair.Two) {
                        $problems += "the '$key' conversation pair is not the Care Home booking's family user / facility owner pair"
                    }
                    else { $domainConsistent = $true }
                }
            }
            'sqlOnlyLegacy' {
                # Kept row, reported but never counted as domain evidence.
            }
            default {
                $problems += "the '$key' fixture carries the unknown party rule '$partyRule'"
            }
        }

        $results += [ordered]@{
            key              = $key
            id               = [string]$conversation.id
            scope            = [int]$conversation.scope
            partyRule        = $partyRule
            expectedBookingId = $expectedBookingId
            storedBookingId  = $storedBookingId
            domainConsistent = $domainConsistent
            problems         = @($problems)
        }
    }

    return @($results)
}

function Get-F2RowFieldNames {
    <#
        Field names of one response row, whether it was parsed from JSON into a
        PSObject or built as a dictionary in an offline test.
    #>
    [CmdletBinding()]
    param([AllowNull()][object]$Row)

    if ($null -eq $Row) { return @() }
    if ($Row -is [System.Collections.IDictionary]) {
        return @($Row.Keys | ForEach-Object { [string]$_ })
    }
    return @($Row.PSObject.Properties.Name)
}

function Test-F2OwnerListBody {
    <#
        Evaluates the owner booking-request list response against the stored
        fixture state: facility/room scoping, the stored resident and responsible
        contact snapshots, the stored decision deadline, and the absence of the
        clinical medical snapshot.
    #>
    [CmdletBinding()]
    param([AllowNull()][object]$Body, [hashtable]$Definition, [object]$Snapshot)

    $problems = @()
    $items = @($Body)
    $actual = "rows=$($items.Count)"

    if ($items.Count -ne 3) {
        $problems += "expected the three owned fixture bookings, got $($items.Count) rows"
        return @{ Actual = $actual; Problems = @($problems) }
    }

    $byId = @{}
    $forbidden = @('medicalSnapshotJson', 'medicalSnapshot', 'medicalSnapshotEncrypted', 'merchantReference', 'paymobTransactionId')
    foreach ($item in $items) {
        $names = @(Get-F2RowFieldNames -Row $item)
        $byId[[string]$item.id] = $item
        if ([string]$item.facilityId -ne [string]$Definition.facilityId) { $problems += "row $($item.id) is not scoped to the owned facility" }
        if ([string]$item.roomTypeId -ne [string]$Definition.roomTypeId) { $problems += "row $($item.id) does not carry the owned room type" }
        if ([string]$item.currency -ne 'EGP') { $problems += "row $($item.id) currency is '$($item.currency)'" }
        foreach ($name in $forbidden) {
            if ($names -contains $name) { $problems += "row $($item.id) exposes $name" }
        }
    }

    foreach ($expectedId in @([string]$Definition.awaitingBookingId, [string]$Definition.extensionBookingId, [string]$Definition.baseBookingId)) {
        if (-not $byId.ContainsKey($expectedId)) { $problems += "the expected fixture booking $expectedId is missing" }
    }
    if ($byId.Count -ne 3) { $problems += "the owner scope returned $($byId.Count) distinct bookings, expected 3" }

    $awaiting = $byId[[string]$Definition.awaitingBookingId]
    if ($null -ne $awaiting) {
        if ([int]$awaiting.status -ne [int]$Definition.awaitingBookingStatus) { $problems += "the awaiting-decision row status is $($awaiting.status)" }
        if ([int]$awaiting.paymentStatus -ne [int]$Definition.paidPaymentStatus) { $problems += "the awaiting-decision row payment status is $($awaiting.paymentStatus)" }
        if ($null -eq $awaiting.decisionHoldUntilUtc) {
            $problems += 'the awaiting-decision row is missing the stored decision deadline'
        }
        elseif (-not (Test-F2SameInstant -Left $awaiting.decisionHoldUntilUtc -Right $Snapshot.awaitingDecisionHoldUntilUtc)) {
            $problems += 'the awaiting-decision row deadline does not match the stored value'
        }
        if ([string]::IsNullOrWhiteSpace([string]$awaiting.elderlyArabicName)) { $problems += 'the awaiting-decision row carries no resident Arabic name' }
        elseif ((ConvertTo-F2Utf8Hex ([string]$awaiting.elderlyArabicName)) -ne [string]$Snapshot.awaitingElderlyArabicNameHex) {
            $problems += 'the awaiting-decision row resident Arabic name does not match the stored value'
        }
        if ([string]$awaiting.elderlyEnglishName -ne [string]$Definition.elderlyEnglishName) { $problems += "the awaiting-decision row resident English name is '$($awaiting.elderlyEnglishName)'" }
        if ([int]$awaiting.elderlyAge -ne [int]$Definition.elderlyAge) { $problems += "the awaiting-decision row resident age is $($awaiting.elderlyAge)" }
        if ([string]$awaiting.responsibleContactName -ne [string]$Definition.contactName) { $problems += 'the awaiting-decision row responsible contact name does not match the stored value' }
        if ([string]$awaiting.responsibleContactPhone -ne [string]$Definition.contactPhone) { $problems += 'the awaiting-decision row responsible contact phone does not match the stored value' }
        if ([string]$awaiting.responsibleContactRelationship -ne [string]$Definition.contactRelationship) { $problems += 'the awaiting-decision row responsible contact relationship does not match the stored value' }
        if ([string]$awaiting.careNeedsNotes -ne [string]$Definition.careNeedsNotes) { $problems += 'the awaiting-decision row care notes do not match the stored value' }
        if ([decimal]$awaiting.totalAmount -ne [decimal]$Snapshot.awaitingTotalAmount) { $problems += 'the awaiting-decision row total amount does not match the stored value' }
    }

    foreach ($acceptedId in @([string]$Definition.baseBookingId, [string]$Definition.extensionBookingId)) {
        $row = $byId[$acceptedId]
        if ($null -eq $row) { continue }
        if ([int]$row.status -ne [int]$Definition.acceptedBookingStatus) { $problems += "row $acceptedId status is $($row.status), expected the accepted fixture state" }
        if ($null -ne $row.decisionHoldUntilUtc) { $problems += "row $acceptedId is not awaiting a decision but still exposes a decision deadline" }
    }

    $actual = "rows=$($items.Count) awaitingStatus=$($awaiting.status) deadlinePresent=$($null -ne $awaiting.decisionHoldUntilUtc)"
    return @{ Actual = $actual; Problems = @($problems) }
}

function Test-F2AdminListBody {
    <#
        Evaluates the admin booking page response for the three verification
        modes: the default page (stored tax amounts plus the base/extension link
        distinction), the booking-status filter, and the paging metadata.
    #>
    [CmdletBinding()]
    param(
        [AllowNull()][object]$Body,
        [hashtable]$Definition,
        [object]$Snapshot,
        [string]$Mode = 'default'
    )

    $problems = @()
    if ($null -eq $Body) {
        return @{ Actual = 'body is not valid JSON'; Problems = @('the admin page body is not valid JSON') }
    }

    $items = @($Body.items)
    $actual = "mode=$Mode page=$($Body.page) pageSize=$($Body.pageSize) totalCount=$($Body.totalCount) items=$($items.Count)"

    $byId = @{}
    foreach ($item in $items) { $byId[[string]$item.bookingId] = $item }
    $extensionLinks = @($items | Where-Object { $null -ne $_.extensionOfBookingId })

    switch ($Mode) {
        'default' {
            if ([int]$Body.totalCount -ne 3) { $problems += "the admin page total count is $($Body.totalCount), expected 3" }
            if ($items.Count -ne 3) { $problems += "the admin page returned $($items.Count) rows, expected 3" }
            foreach ($expectedId in @([string]$Definition.awaitingBookingId, [string]$Definition.extensionBookingId, [string]$Definition.baseBookingId)) {
                if (-not $byId.ContainsKey($expectedId)) { $problems += "the admin page is missing fixture booking $expectedId" }
            }
            if ($extensionLinks.Count -ne 1) { $problems += "expected exactly one extension link in the admin page, found $($extensionLinks.Count)" }

            $base = $byId[[string]$Definition.baseBookingId]
            if ($null -ne $base) {
                if ($null -ne $base.extensionOfBookingId) { $problems += 'the accepted base booking is reported as an extension' }
                if ([decimal]$base.taxAmount -ne [decimal]$Snapshot.baseTaxAmount) { $problems += 'the accepted base booking tax amount does not match the stored value' }
                if ([int]$base.bookingStatus -ne [int]$Definition.acceptedBookingStatus) { $problems += "the accepted base booking status is $($base.bookingStatus)" }
                if ([int]$base.paymentStatus -ne [int]$Definition.paidPaymentStatus) { $problems += "the accepted base booking payment status is $($base.paymentStatus)" }
                if ([decimal]$base.totalAmount -ne [decimal]$Definition.baseTotalAmount) { $problems += 'the accepted base booking total amount does not match the stored value' }
            }

            $extension = $byId[[string]$Definition.extensionBookingId]
            if ($null -ne $extension) {
                if ([string]$extension.extensionOfBookingId -ne [string]$Definition.baseBookingId) { $problems += 'the extension booking does not link back to the accepted base booking' }
                if ([decimal]$extension.taxAmount -ne [decimal]$Snapshot.extensionTaxAmount) { $problems += 'the extension booking tax amount does not match the stored value' }
            }

            $awaiting = $byId[[string]$Definition.awaitingBookingId]
            if ($null -ne $awaiting) {
                if ([decimal]$awaiting.taxAmount -ne [decimal]$Snapshot.awaitingTaxAmount) { $problems += 'the awaiting-decision booking tax amount does not match the stored value' }
                if ([int]$awaiting.bookingStatus -ne [int]$Definition.awaitingBookingStatus) { $problems += "the awaiting-decision booking status is $($awaiting.bookingStatus)" }
            }

            foreach ($item in $items) {
                if ([string]$item.facilityId -ne [string]$Definition.facilityId) { $problems += "row $($item.bookingId) is not scoped to the fixture facility" }
                if ([string]$item.currency -ne 'EGP') { $problems += "row $($item.bookingId) currency is '$($item.currency)'" }
                if ([int]$item.refundStatus -ne [int]$Definition.refundStatusNone) { $problems += "row $($item.bookingId) refund status is $($item.refundStatus)" }
            }
        }
        'statusFilter' {
            if ([int]$Body.totalCount -ne 2) { $problems += "the accepted-status filter total count is $($Body.totalCount), expected 2" }
            if ($items.Count -ne 2) { $problems += "the accepted-status filter returned $($items.Count) rows, expected 2" }
            foreach ($item in $items) {
                if ([int]$item.bookingStatus -ne [int]$Definition.acceptedBookingStatus) { $problems += "the accepted-status filter returned booking status $($item.bookingStatus)" }
            }
            foreach ($expectedId in @([string]$Definition.baseBookingId, [string]$Definition.extensionBookingId)) {
                if (-not $byId.ContainsKey($expectedId)) { $problems += "the accepted-status filter is missing fixture booking $expectedId" }
            }
            if ($byId.ContainsKey([string]$Definition.awaitingBookingId)) { $problems += 'the accepted-status filter returned the awaiting-decision booking' }
        }
        'paging' {
            if ($items.Count -ne 1) { $problems += "the single-item page returned $($items.Count) rows" }
            if ([int]$Body.pageSize -ne 1) { $problems += "the single-item page reports page size $($Body.pageSize)" }
            if ([int]$Body.totalCount -ne 3) { $problems += "the single-item page total count is $($Body.totalCount), expected 3" }
            if ([int]$Body.page -ne 1) { $problems += "the single-item page reports page $($Body.page)" }
            if ($null -ne $Body.totalPages -and [int]$Body.totalPages -ne 3) { $problems += "the single-item page total pages is $($Body.totalPages), expected 3" }
        }
        default { $problems += "unknown admin assertion mode '$Mode'" }
    }

    return @{ Actual = $actual; Problems = @($problems) }
}

function Test-F2ChatListBody {
    <#
        Evaluates the conversation list response for one requester: exactly the
        conversations that requester participates in, newest stored activity
        first, with the persisted numeric scope, the nullable/non-null booking
        reference, the counterpart participant identity, and the message
        aggregates of a conversation without messages.
    #>
    [CmdletBinding()]
    param(
        [AllowNull()][object]$Body,
        [hashtable]$Definition,
        [object[]]$Expectations
    )

    $problems = @()
    if ($null -eq $Body) {
        return @{ Actual = 'body is not valid JSON'; Problems = @('the conversation list body is not valid JSON') }
    }

    $items = @($Body.items)
    $expected = @($Expectations)
    $keys = @($expected | ForEach-Object { $_.key })
    $actual = "rows=$($items.Count) expectedKeys=$($keys -join ',')"

    if ($items.Count -ne $expected.Count) {
        $problems += "expected $($expected.Count) visible conversations, got $($items.Count)"
        return @{ Actual = $actual; Problems = @($problems) }
    }

    if ($null -ne $Body.totalCount -and [int]$Body.totalCount -ne $expected.Count) {
        $problems += "the conversation list total count is $($Body.totalCount), expected $($expected.Count)"
    }

    for ($index = 0; $index -lt $expected.Count; $index++) {
        $row = $items[$index]
        $want = $expected[$index]
        if ([string]$row.id -ne [string]$want.id) {
            $problems += "position $index is conversation $($row.id), expected the '$($want.key)' fixture conversation"
            continue
        }
        if ($row.scope -is [string]) { $problems += "conversation '$($want.key)' scope is serialized as a string, expected a number" }
        elseif ([int]$row.scope -ne [int]$want.scope) { $problems += "conversation '$($want.key)' scope is $($row.scope), expected $($want.scope)" }

        if ($want.scope -eq 0) {
            if ($null -ne $row.bookingId) { $problems += "direct conversation '$($want.key)' exposes booking reference $($row.bookingId)" }
        }
        else {
            if ($null -eq $row.bookingId) { $problems += "booking conversation '$($want.key)' has no booking reference" }
            else {
                $parsed = [Guid]::Empty
                if (-not [Guid]::TryParse([string]$row.bookingId, [ref]$parsed)) { $problems += "booking conversation '$($want.key)' booking reference is not a guid" }
                elseif ([string]$row.bookingId -ne [string]$want.bookingId) { $problems += "booking conversation '$($want.key)' references booking $($row.bookingId), expected the stored fixture booking" }
            }
        }

        if ($null -eq $row.participant) { $problems += "conversation '$($want.key)' carries no counterpart participant" }
        else {
            if ([string]$row.participant.userId -ne [string]$want.counterpartUserId) { $problems += "conversation '$($want.key)' counterpart is not the expected participant" }
            if ([int]$row.participant.accountType -ne [int]$want.counterpartAccountType) { $problems += "conversation '$($want.key)' counterpart account type is $($row.participant.accountType), expected $($want.counterpartAccountType)" }
            if ([string]::IsNullOrWhiteSpace([string]$row.participant.displayName)) { $problems += "conversation '$($want.key)' counterpart has no display name" }
        }

        if ($null -ne $row.lastMessage) { $problems += "conversation '$($want.key)' reports a last message although the fixture has none" }
        if ([int]$row.unreadCount -ne 0) { $problems += "conversation '$($want.key)' reports $($row.unreadCount) unread messages" }
    }

    return @{ Actual = $actual; Problems = @($problems) }
}

function Get-F2FixtureSql {
    <#
        Builds the INSERT-only provisioning script for the fixture set. The script
        is pure ASCII (Arabic literals use U& escapes; the stored elder name is
        copied server side), runs in one transaction, verifies its prerequisites
        before writing and re-reads every written row afterwards, raising an
        exception - which rolls the whole transaction back - when anything does
        not match. Existing rows are reused through ON CONFLICT (id) DO NOTHING,
        so a repeated run writes nothing. The chat read-back additionally
        asserts each booking-scoped pair against the party rule its referenced
        booking implies (Families: creator/caregiver; Care Home: family
        user/facility owner), so a fabricated pairing cannot pass as domain
        evidence.
    #>
    [CmdletBinding()]
    param([hashtable]$Definition, [hashtable]$Resolved)

    $map = @{
        '{credentialSourceEmail}'    = [string]$Definition.credentialSourceEmail
        '{activeUserStatus}'         = [string]$Definition.activeUserStatus
        '{ownerUserId}'              = [string]$Definition.ownerUserId
        '{ownerAccountId}'           = [string]$Definition.ownerAccountId
        '{ownerEmail}'               = [string]$Definition.ownerEmail
        '{ownerPhone}'               = [string]$Definition.ownerPhone
        '{ownerEnglishName}'         = [string]$Definition.ownerEnglishName
        '{ownerAccountType}'         = [string]$Definition.ownerAccountType
        '{adminUserId}'              = [string]$Definition.adminUserId
        '{adminAccountId}'           = [string]$Definition.adminAccountId
        '{adminEmail}'               = [string]$Definition.adminEmail
        '{adminPhone}'               = [string]$Definition.adminPhone
        '{adminEnglishName}'         = [string]$Definition.adminEnglishName
        '{adminAccountType}'         = [string]$Definition.adminAccountType
        '{facilityId}'               = [string]$Definition.facilityId
        '{facilityStatusDraft}'      = [string]$Definition.facilityStatusDraft
        '{roomTypeId}'               = [string]$Definition.roomTypeId
        '{roomTypeEnglishName}'      = [string]$Definition.roomTypeEnglishName
        '{roomTypeMonthlyPrice}'     = [string]$Definition.roomTypeMonthlyPrice
        '{roomTypeSharedMode}'       = [string]$Definition.roomTypeSharedMode
        '{familyOwnerUserId}'        = [string]$Resolved.familyOwnerUserId
        '{familyId}'                 = [string]$Definition.familyId
        '{elderlyId}'                = [string]$Definition.elderlyId
        '{chatBookingId}'            = [string]$Resolved.chatBookingId
        '{awaitingBookingId}'        = [string]$Definition.awaitingBookingId
        '{baseBookingId}'            = [string]$Definition.baseBookingId
        '{extensionBookingId}'       = [string]$Definition.extensionBookingId
        '{awaitingMerchantReference}'  = [string]$Definition.awaitingMerchantReference
        '{baseMerchantReference}'      = [string]$Definition.baseMerchantReference
        '{extensionMerchantReference}' = [string]$Definition.extensionMerchantReference
        '{awaitingBookingStatus}'    = [string]$Definition.awaitingBookingStatus
        '{acceptedBookingStatus}'    = [string]$Definition.acceptedBookingStatus
        '{paidPaymentStatus}'        = [string]$Definition.paidPaymentStatus
        '{awaitingTaxAmount}'        = [string]$Definition.awaitingTaxAmount
        '{awaitingTotalAmount}'      = [string]$Definition.awaitingTotalAmount
        '{baseTaxAmount}'            = [string]$Definition.baseTaxAmount
        '{baseTotalAmount}'          = [string]$Definition.baseTotalAmount
        '{baseVersion}'              = [string]$Definition.baseVersion
        '{extensionVersion}'         = [string]$Definition.extensionVersion
        '{elderlyEnglishName}'       = [string]$Definition.elderlyEnglishName
        '{elderlyAge}'               = [string]$Definition.elderlyAge
        '{contactName}'              = [string]$Definition.contactName
        '{contactPhone}'             = [string]$Definition.contactPhone
        '{contactRelationship}'      = [string]$Definition.contactRelationship
        '{careNeedsNotes}'           = [string]$Definition.careNeedsNotes
        '{medicalSnapshotMarker}'    = [string]$Definition.medicalSnapshotMarker
        '{directConversationId}'     = [string]$Definition.directConversationId
        '{bookingConversationId}'    = [string]$Definition.bookingConversationId
        '{ownerBookingConversationId}' = [string]$Definition.ownerBookingConversationId
        '{ownerFacilityBookingConversationId}' = [string]$Definition.ownerFacilityBookingConversationId
    }

    $roles = @{
        fixtureOwner     = [string]$Definition.ownerUserId
        familyOwner      = [string]$Resolved.familyOwnerUserId
        medicalCaregiver = [string]$Resolved.medicalCaregiverUserId
    }

    foreach ($conversation in @($Definition.conversations)) {
        $participants = @($conversation.participantRoles)
        $pair = Get-F2NormalizedPair -First $roles[$participants[0]] -Second $roles[$participants[1]]
        $map["{$($conversation.key)ParticipantOne}"] = $pair.One
        $map["{$($conversation.key)ParticipantTwo}"] = $pair.Two
    }

    $sql = @'
SET client_encoding TO 'UTF8';
SET statement_timeout TO '60s';
BEGIN;

DO $f2_pre$
DECLARE
    source_identity int;
    family_pair int;
    elderly_row int;
BEGIN
    SELECT count(*) INTO source_identity FROM identity.users WHERE email = '{credentialSourceEmail}';
    IF source_identity <> 1 THEN
        RAISE EXCEPTION 'F2 fixture prerequisite missing: the synthetic credential source identity is absent.';
    END IF;

    SELECT count(*) INTO family_pair
      FROM families.bookings b
      JOIN caregivers.caregivers cr ON cr.id = b.caregiver_id
      JOIN identity.users cg ON cg.id = cr.user_id AND cg.email = 'medical.caregiver@test.sanad.local'
      JOIN identity.users cb ON cb.id = b.created_by_user_id AND cb.email = 'family.owner@test.sanad.local';
    IF family_pair < 1 THEN
        RAISE EXCEPTION 'F2 fixture prerequisite missing: no existing family/caregiver booking for the booking-scoped chat fixture.';
    END IF;

    SELECT count(*) INTO elderly_row
      FROM families.elderlies e
     WHERE e.id = '{elderlyId}'::uuid
       AND e.family_id = '{familyId}'::uuid
       AND e.arabic_full_name <> '';
    IF elderly_row <> 1 THEN
        RAISE EXCEPTION 'F2 fixture prerequisite missing: the existing fixture elder is absent.';
    END IF;
END
$f2_pre$;

INSERT INTO identity.users (id, arabic_full_name, english_full_name, email, phone_number, password_hash,
    email_verified, phone_verified, status, created_on_utc, updated_on_utc)
SELECT '{ownerUserId}'::uuid, U&'\0645\0627\0644\0643 \062F\0627\0631 \0631\0639\0627\064A\0629',
    '{ownerEnglishName}', '{ownerEmail}', '{ownerPhone}', src.password_hash,
    true, true, {activeUserStatus}, now(), now()
FROM identity.users src
WHERE src.email = '{credentialSourceEmail}'
ON CONFLICT (id) DO NOTHING;

INSERT INTO identity.users (id, arabic_full_name, english_full_name, email, phone_number, password_hash,
    email_verified, phone_verified, status, created_on_utc, updated_on_utc)
SELECT '{adminUserId}'::uuid, U&'\062F\0639\0645 \0625\062F\0627\0631\064A \062A\062C\0631\064A\0628\064A',
    '{adminEnglishName}', '{adminEmail}', '{adminPhone}', src.password_hash,
    true, true, {activeUserStatus}, now(), now()
FROM identity.users src
WHERE src.email = '{credentialSourceEmail}'
ON CONFLICT (id) DO NOTHING;

INSERT INTO identity.user_accounts (id, account_type, created_on_utc, user_id)
VALUES ('{ownerAccountId}'::uuid, {ownerAccountType}, now(), '{ownerUserId}'::uuid)
ON CONFLICT (id) DO NOTHING;

INSERT INTO identity.user_accounts (id, account_type, created_on_utc, user_id)
VALUES ('{adminAccountId}'::uuid, {adminAccountType}, now(), '{adminUserId}'::uuid)
ON CONFLICT (id) DO NOTHING;

INSERT INTO care_homes.facilities (id, owner_user_id, status, version, created_on_utc, updated_on_utc)
VALUES ('{facilityId}'::uuid, '{ownerUserId}'::uuid, {facilityStatusDraft}, 1, now(), now())
ON CONFLICT (id) DO NOTHING;

INSERT INTO care_homes.room_types (id, care_home_id, "ArabicName", "EnglishName", "MonthlyPriceEgp",
    "AllocationMode", "IsArchived", "CreatedOnUtc", "UpdatedOnUtc", amenities_json)
VALUES ('{roomTypeId}'::uuid, '{facilityId}'::uuid, U&'\063A\0631\0641\0629 \0645\0634\062A\0631\0643\0629',
    '{roomTypeEnglishName}', {roomTypeMonthlyPrice}, {roomTypeSharedMode}, false, now(), now(), '[]'::jsonb)
ON CONFLICT (id) DO NOTHING;

INSERT INTO care_homes.bookings (id, "FacilityId", "FamilyUserId", "FamilyId", "ElderlyId", "RoomTypeId",
    "StartDate", "EndDate", "Status", "PaymentStatus",
    "BaseAmount", "PlatformFeeAmount", "TaxAmount", "TotalAmount", "ChargeRuleVersion",
    "ElderlyArabicName", "ElderlyEnglishName", "ElderlyAge", "MedicalSnapshotJson",
    "ResponsibleContactName", "ResponsibleContactPhone", "ResponsibleContactRelationship", "CareNeedsNotes",
    "MerchantReference", "PaymobTransactionId",
    "CheckoutHoldUntilUtc", "DecisionHoldUntilUtc", "DecidedOnUtc", "DecisionReason",
    "Version", "CreatedOnUtc", "UpdatedOnUtc", earliest_arrival_utc,
    refund_status, extension_of_booking_id, extension_root_booking_id, payment_completed_on_utc)
SELECT
    '{awaitingBookingId}'::uuid, '{facilityId}'::uuid, '{familyOwnerUserId}'::uuid, '{familyId}'::uuid,
    '{elderlyId}'::uuid, '{roomTypeId}'::uuid,
    (((now() - interval '6 hours') AT TIME ZONE 'Africa/Cairo')::date + 30),
    ((((now() - interval '6 hours') AT TIME ZONE 'Africa/Cairo')::date + 30) + interval '1 month')::date,
    {awaitingBookingStatus}, {paidPaymentStatus},
    12000.00, 1200.00, {awaitingTaxAmount}, {awaitingTotalAmount}, 1,
    (SELECT e.arabic_full_name FROM families.elderlies e WHERE e.id = '{elderlyId}'::uuid),
    '{elderlyEnglishName}', {elderlyAge}, jsonb_build_object('fixture', '{medicalSnapshotMarker}'),
    '{contactName}', '{contactPhone}', '{contactRelationship}', '{careNeedsNotes}',
    '{awaitingMerchantReference}', 90000000000201,
    (now() - interval '6 hours') + interval '15 minutes',
    now() + interval '23 hours',
    NULL, NULL,
    2, now() - interval '6 hours', now() - interval '1 hour',
    now() - interval '5 hours',
    0, NULL, NULL, now() - interval '1 hour'
ON CONFLICT (id) DO NOTHING;

INSERT INTO care_homes.bookings (id, "FacilityId", "FamilyUserId", "FamilyId", "ElderlyId", "RoomTypeId",
    "StartDate", "EndDate", "Status", "PaymentStatus",
    "BaseAmount", "PlatformFeeAmount", "TaxAmount", "TotalAmount", "ChargeRuleVersion",
    "ElderlyArabicName", "ElderlyEnglishName", "ElderlyAge", "MedicalSnapshotJson",
    "ResponsibleContactName", "ResponsibleContactPhone", "ResponsibleContactRelationship", "CareNeedsNotes",
    "MerchantReference", "PaymobTransactionId",
    "CheckoutHoldUntilUtc", "DecisionHoldUntilUtc", "DecidedOnUtc", "DecisionReason",
    "Version", "CreatedOnUtc", "UpdatedOnUtc", earliest_arrival_utc,
    refund_status, extension_of_booking_id, extension_root_booking_id, payment_completed_on_utc)
SELECT
    '{baseBookingId}'::uuid, '{facilityId}'::uuid, '{familyOwnerUserId}'::uuid, '{familyId}'::uuid,
    '{elderlyId}'::uuid, '{roomTypeId}'::uuid,
    (((now() - interval '20 days') AT TIME ZONE 'Africa/Cairo')::date + 3),
    ((((now() - interval '20 days') AT TIME ZONE 'Africa/Cairo')::date + 3) + interval '1 month')::date,
    {acceptedBookingStatus}, {paidPaymentStatus},
    15000.00, 1500.00, {baseTaxAmount}, {baseTotalAmount}, 1,
    (SELECT e.arabic_full_name FROM families.elderlies e WHERE e.id = '{elderlyId}'::uuid),
    '{elderlyEnglishName}', {elderlyAge}, jsonb_build_object('fixture', '{medicalSnapshotMarker}'),
    '{contactName}', '{contactPhone}', '{contactRelationship}', '{careNeedsNotes}',
    '{baseMerchantReference}', 90000000000202,
    (now() - interval '20 days') + interval '15 minutes',
    (now() - interval '20 days') + interval '2 minutes' + interval '24 hours',
    (now() - interval '20 days') + interval '2 minutes' + interval '3 hours',
    'Accepted by the facility during the F2 read-completeness fixture provisioning.',
    {baseVersion}, now() - interval '20 days',
    (now() - interval '20 days') + interval '2 minutes' + interval '3 hours',
    (now() - interval '20 days') + interval '24 hours',
    0, NULL, NULL, (now() - interval '20 days') + interval '2 minutes'
ON CONFLICT (id) DO NOTHING;

INSERT INTO care_homes.bookings (id, "FacilityId", "FamilyUserId", "FamilyId", "ElderlyId", "RoomTypeId",
    "StartDate", "EndDate", "Status", "PaymentStatus",
    "BaseAmount", "PlatformFeeAmount", "TaxAmount", "TotalAmount", "ChargeRuleVersion",
    "ElderlyArabicName", "ElderlyEnglishName", "ElderlyAge", "MedicalSnapshotJson",
    "ResponsibleContactName", "ResponsibleContactPhone", "ResponsibleContactRelationship", "CareNeedsNotes",
    "MerchantReference", "PaymobTransactionId",
    "CheckoutHoldUntilUtc", "DecisionHoldUntilUtc", "DecidedOnUtc", "DecisionReason",
    "Version", "CreatedOnUtc", "UpdatedOnUtc", earliest_arrival_utc,
    refund_status, extension_of_booking_id, extension_root_booking_id, payment_completed_on_utc)
SELECT
    '{extensionBookingId}'::uuid, '{facilityId}'::uuid, '{familyOwnerUserId}'::uuid, '{familyId}'::uuid,
    '{elderlyId}'::uuid, '{roomTypeId}'::uuid,
    (SELECT b."EndDate" FROM care_homes.bookings b WHERE b.id = '{baseBookingId}'::uuid),
    ((SELECT b."EndDate" FROM care_homes.bookings b WHERE b.id = '{baseBookingId}'::uuid) + interval '1 month')::date,
    {acceptedBookingStatus}, {paidPaymentStatus},
    15000.00, 1500.00, {baseTaxAmount}, {baseTotalAmount}, 1,
    (SELECT e.arabic_full_name FROM families.elderlies e WHERE e.id = '{elderlyId}'::uuid),
    '{elderlyEnglishName}', {elderlyAge}, jsonb_build_object('fixture', '{medicalSnapshotMarker}'),
    '{contactName}', '{contactPhone}', '{contactRelationship}', '{careNeedsNotes}',
    '{extensionMerchantReference}', 90000000000203,
    (now() - interval '19 days') + interval '15 minutes',
    (now() - interval '19 days') + interval '2 minutes' + interval '24 hours',
    (now() - interval '19 days') + interval '2 minutes' + interval '3 hours',
    'Accepted by the facility during the F2 read-completeness fixture provisioning.',
    {extensionVersion}, now() - interval '19 days',
    (now() - interval '19 days') + interval '2 minutes' + interval '3 hours',
    (now() - interval '19 days') + interval '24 hours',
    0, '{baseBookingId}'::uuid, '{baseBookingId}'::uuid, (now() - interval '19 days') + interval '2 minutes'
ON CONFLICT (id) DO NOTHING;

INSERT INTO chat."Conversations" ("Id", "ParticipantOne", "ParticipantTwo", "Scope", "BookingId",
    "HiddenForParticipantOne", "HiddenForParticipantTwo", "CreatedOnUtc", "LastActivityOnUtc")
VALUES ('{directConversationId}'::uuid, '{directParticipantOne}'::uuid, '{directParticipantTwo}'::uuid,
    0, NULL, false, false, now() - interval '3 hours', now() - interval '1 hour')
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO chat."Conversations" ("Id", "ParticipantOne", "ParticipantTwo", "Scope", "BookingId",
    "HiddenForParticipantOne", "HiddenForParticipantTwo", "CreatedOnUtc", "LastActivityOnUtc")
VALUES ('{bookingConversationId}'::uuid, '{bookingParticipantOne}'::uuid, '{bookingParticipantTwo}'::uuid,
    1, '{chatBookingId}'::uuid, false, false, now() - interval '4 hours', now() - interval '2 hours')
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO chat."Conversations" ("Id", "ParticipantOne", "ParticipantTwo", "Scope", "BookingId",
    "HiddenForParticipantOne", "HiddenForParticipantTwo", "CreatedOnUtc", "LastActivityOnUtc")
VALUES ('{ownerBookingConversationId}'::uuid, '{ownerBookingParticipantOne}'::uuid,
    '{ownerBookingParticipantTwo}'::uuid,
    1, '{chatBookingId}'::uuid, false, false, now() - interval '5 hours', now() - interval '3 hours')
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO chat."Conversations" ("Id", "ParticipantOne", "ParticipantTwo", "Scope", "BookingId",
    "HiddenForParticipantOne", "HiddenForParticipantTwo", "CreatedOnUtc", "LastActivityOnUtc")
VALUES ('{ownerFacilityBookingConversationId}'::uuid, '{ownerFacilityBookingParticipantOne}'::uuid,
    '{ownerFacilityBookingParticipantTwo}'::uuid,
    1, '{baseBookingId}'::uuid, false, false, now() - interval '6 hours', now() - interval '4 hours')
ON CONFLICT ("Id") DO NOTHING;

DO $f2_post$
DECLARE
    n int;
    extension_of uuid;
    extension_status int;
    extension_payment int;
BEGIN
    SELECT count(*) INTO n FROM care_homes.facilities
     WHERE id = '{facilityId}'::uuid AND owner_user_id = '{ownerUserId}'::uuid
       AND status = {facilityStatusDraft} AND version = 1;
    IF n <> 1 THEN
        RAISE EXCEPTION 'F2 fixture read-back failed: the facility row is not in the expected draft shape.';
    END IF;

    SELECT count(*) INTO n FROM care_homes.room_types
     WHERE id = '{roomTypeId}'::uuid AND care_home_id = '{facilityId}'::uuid AND "IsArchived" = false;
    IF n <> 1 THEN
        RAISE EXCEPTION 'F2 fixture read-back failed: the room type row is missing or archived.';
    END IF;

    SELECT count(*) INTO n FROM care_homes.bookings
     WHERE id IN ('{awaitingBookingId}'::uuid, '{baseBookingId}'::uuid, '{extensionBookingId}'::uuid);
    IF n <> 3 THEN
        RAISE EXCEPTION 'F2 fixture read-back failed: expected 3 care-home fixture bookings, found %.', n;
    END IF;

    SELECT count(*) INTO n FROM care_homes.bookings
     WHERE id = '{awaitingBookingId}'::uuid AND "Status" = {awaitingBookingStatus}
       AND "PaymentStatus" = {paidPaymentStatus} AND "DecisionHoldUntilUtc" > now()
       AND "MedicalSnapshotJson" IS NOT NULL AND "ElderlyArabicName" <> '' AND "ElderlyAge" = {elderlyAge}
       AND "ResponsibleContactName" <> '' AND "ResponsibleContactPhone" <> '' AND "CareNeedsNotes" <> ''
       AND "TaxAmount" = {awaitingTaxAmount} AND "Version" = 2;
    IF n <> 1 THEN
        RAISE EXCEPTION 'F2 fixture read-back failed: the paid-awaiting-decision booking is not stored with a live decision deadline and its snapshots.';
    END IF;

    SELECT count(*) INTO n FROM care_homes.bookings
     WHERE id = '{baseBookingId}'::uuid AND "Status" = {acceptedBookingStatus}
       AND "PaymentStatus" = {paidPaymentStatus} AND extension_of_booking_id IS NULL
       AND "DecisionHoldUntilUtc" IS NOT NULL AND "DecisionHoldUntilUtc" <= now()
       AND "TaxAmount" = {baseTaxAmount} AND "Version" = {baseVersion};
    IF n <> 1 THEN
        RAISE EXCEPTION 'F2 fixture read-back failed: the accepted base booking is not stored in the expected shape.';
    END IF;

    SELECT extension_of_booking_id, "Status", "PaymentStatus" INTO extension_of, extension_status, extension_payment
      FROM care_homes.bookings WHERE id = '{extensionBookingId}'::uuid;
    IF extension_of IS DISTINCT FROM '{baseBookingId}'::uuid
       OR extension_status <> {acceptedBookingStatus} OR extension_payment <> {paidPaymentStatus} THEN
        RAISE EXCEPTION 'F2 fixture read-back failed: the extension booking is not linked to its accepted paid base booking.';
    END IF;

    SELECT count(*) INTO n FROM chat."Conversations"
     WHERE "Id" IN ('{directConversationId}'::uuid, '{bookingConversationId}'::uuid,
                    '{ownerBookingConversationId}'::uuid, '{ownerFacilityBookingConversationId}'::uuid);
    IF n <> 4 THEN
        RAISE EXCEPTION 'F2 fixture read-back failed: expected 4 chat fixture conversations, found %.', n;
    END IF;

    SELECT count(*) INTO n FROM chat."Conversations"
     WHERE "Id" = '{directConversationId}'::uuid AND "Scope" = 0 AND "BookingId" IS NULL
       AND "HiddenForParticipantOne" = false AND "HiddenForParticipantTwo" = false;
    IF n <> 1 THEN
        RAISE EXCEPTION 'F2 fixture read-back failed: the direct conversation is not stored with a null booking reference.';
    END IF;

    SELECT count(*) INTO n FROM chat."Conversations"
     WHERE "Id" = '{ownerBookingConversationId}'::uuid
       AND "Scope" = 1 AND "BookingId" = '{chatBookingId}'::uuid
       AND "HiddenForParticipantOne" = false AND "HiddenForParticipantTwo" = false;
    IF n <> 1 THEN
        RAISE EXCEPTION 'F2 fixture read-back failed: the pre-existing booking-scoped conversation is not stored against the reused caregiver booking.';
    END IF;

    SELECT count(*) INTO n FROM chat."Conversations" c
      JOIN families.bookings b ON b.id = c."BookingId"
      JOIN caregivers.caregivers cr ON cr.id = b.caregiver_id
     WHERE c."Id" = '{bookingConversationId}'::uuid
       AND c."Scope" = 1 AND b.id = '{chatBookingId}'::uuid
       AND ((c."ParticipantOne" = b.created_by_user_id AND c."ParticipantTwo" = cr.user_id)
            OR (c."ParticipantOne" = cr.user_id AND c."ParticipantTwo" = b.created_by_user_id));
    IF n <> 1 THEN
        RAISE EXCEPTION 'F2 fixture read-back failed: the booking-scoped conversation pair is not the reused Families booking creator/caregiver pair.';
    END IF;

    SELECT count(*) INTO n FROM chat."Conversations" c
      JOIN care_homes.bookings b ON b.id = c."BookingId"
      JOIN care_homes.facilities f ON f.id = b."FacilityId"
     WHERE c."Id" = '{ownerFacilityBookingConversationId}'::uuid
       AND c."Scope" = 1 AND b.id = '{baseBookingId}'::uuid
       AND f.id = '{facilityId}'::uuid AND f.owner_user_id = '{ownerUserId}'::uuid
       AND ((c."ParticipantOne" = b."FamilyUserId" AND c."ParticipantTwo" = f.owner_user_id)
            OR (c."ParticipantOne" = f.owner_user_id AND c."ParticipantTwo" = b."FamilyUserId"));
    IF n <> 1 THEN
        RAISE EXCEPTION 'F2 fixture read-back failed: the owner booking-scoped conversation is not the fixture facility booking family user / facility owner pair.';
    END IF;
END
$f2_post$;

COMMIT;
'@

    foreach ($placeholder in $map.Keys) {
        $sql = $sql.Replace($placeholder, [string]$map[$placeholder])
    }

    return $sql
}

function Get-F2StateQuery {
    <#
        Read-only snapshot of the fixture-relevant persisted state: the resolved
        identities, the stored booking/chat values the HTTP responses must carry,
        the fixture row counts and the active session headroom of the identities
        the checks log in as. Every id is resolved from the database, so the
        before/after and repeat comparisons work without hard-coded row ids.
    #>
    [CmdletBinding()]
    param([hashtable]$Definition)

    $sql = @'
SET client_encoding TO 'UTF8';
SELECT (
  json_build_object(
  'careHomeFacilityCount', (SELECT count(*)::int FROM care_homes.facilities),
  'careHomeRoomTypeCount', (SELECT count(*)::int FROM care_homes.room_types),
  'careHomeBookingCount', (SELECT count(*)::int FROM care_homes.bookings),
  'chatConversationCount', (SELECT count(*)::int FROM chat."Conversations"),
  'fixtureIdentityUserCount', (SELECT count(*)::int FROM identity.users WHERE email IN ('{ownerEmail}', '{adminEmail}')),
  'fixtureIdentityAccountCount', (SELECT count(*)::int FROM identity.user_accounts WHERE id IN ('{ownerAccountId}'::uuid, '{adminAccountId}'::uuid)),
  'fixtureFacilityCount', (SELECT count(*)::int FROM care_homes.facilities WHERE id = '{facilityId}'::uuid),
  'fixtureRoomTypeCount', (SELECT count(*)::int FROM care_homes.room_types WHERE id = '{roomTypeId}'::uuid),
  'fixtureBookingCount', (SELECT count(*)::int FROM care_homes.bookings WHERE id IN ('{awaitingBookingId}'::uuid, '{baseBookingId}'::uuid, '{extensionBookingId}'::uuid)),
  'fixtureConversationCount', (SELECT count(*)::int FROM chat."Conversations" WHERE "Id" IN ('{directConversationId}'::uuid, '{bookingConversationId}'::uuid, '{ownerBookingConversationId}'::uuid, '{ownerFacilityBookingConversationId}'::uuid)),
  'familyOwnerUserId', coalesce((SELECT id::text FROM identity.users WHERE email = 'family.owner@test.sanad.local'), ''),
  'medicalCaregiverUserId', coalesce((SELECT id::text FROM identity.users WHERE email = 'medical.caregiver@test.sanad.local'), ''),
  'contentAdminUserId', coalesce((SELECT id::text FROM identity.users WHERE email = '{credentialSourceEmail}'), ''),
  'fixtureOwnerUserId', coalesce((SELECT id::text FROM identity.users WHERE email = '{ownerEmail}'), ''),
  'fixtureAdminUserId', coalesce((SELECT id::text FROM identity.users WHERE email = '{adminEmail}'), ''),
  'fixtureOwnerAccountType', coalesce((SELECT account_type::text FROM identity.user_accounts WHERE user_id = '{ownerUserId}'::uuid LIMIT 1), ''),
  'fixtureAdminAccountType', coalesce((SELECT account_type::text FROM identity.user_accounts WHERE user_id = '{adminUserId}'::uuid LIMIT 1), ''),
  'chatBookingId', coalesce((SELECT b.id::text FROM families.bookings b
      JOIN caregivers.caregivers cr ON cr.id = b.caregiver_id
      JOIN identity.users cg ON cg.id = cr.user_id AND cg.email = 'medical.caregiver@test.sanad.local'
      JOIN identity.users cb ON cb.id = b.created_by_user_id AND cb.email = 'family.owner@test.sanad.local'
     ORDER BY b.created_on_utc, b.id LIMIT 1), ''),
  'chatBookingCreatedByUserId', coalesce((SELECT b.created_by_user_id::text FROM families.bookings b
      JOIN caregivers.caregivers cr ON cr.id = b.caregiver_id
      JOIN identity.users cg ON cg.id = cr.user_id AND cg.email = 'medical.caregiver@test.sanad.local'
      JOIN identity.users cb ON cb.id = b.created_by_user_id AND cb.email = 'family.owner@test.sanad.local'
     ORDER BY b.created_on_utc, b.id LIMIT 1), ''),
  'chatBookingCaregiverUserId', coalesce((SELECT cr.user_id::text FROM families.bookings b
      JOIN caregivers.caregivers cr ON cr.id = b.caregiver_id
      JOIN identity.users cg ON cg.id = cr.user_id AND cg.email = 'medical.caregiver@test.sanad.local'
      JOIN identity.users cb ON cb.id = b.created_by_user_id AND cb.email = 'family.owner@test.sanad.local'
     ORDER BY b.created_on_utc, b.id LIMIT 1), ''),
  'baseBookingId', coalesce((SELECT b.id::text FROM care_homes.bookings b WHERE b.id = '{baseBookingId}'::uuid), ''),
  'baseBookingFacilityId', coalesce((SELECT b."FacilityId"::text FROM care_homes.bookings b WHERE b.id = '{baseBookingId}'::uuid), ''),
  'careHomeBookingFamilyUserId', coalesce((SELECT b."FamilyUserId"::text FROM care_homes.bookings b WHERE b.id = '{baseBookingId}'::uuid), ''),
  'careHomeBookingFacilityOwnerUserId', coalesce((SELECT f.owner_user_id::text FROM care_homes.facilities f WHERE f.id = '{facilityId}'::uuid), '')
  )::jsonb
  || json_build_object(
  'awaitingStatus', (SELECT "Status" FROM care_homes.bookings WHERE id = '{awaitingBookingId}'::uuid),
  'awaitingPaymentStatus', (SELECT "PaymentStatus" FROM care_homes.bookings WHERE id = '{awaitingBookingId}'::uuid),
  'awaitingTaxAmount', coalesce((SELECT "TaxAmount"::text FROM care_homes.bookings WHERE id = '{awaitingBookingId}'::uuid), ''),
  'awaitingTotalAmount', coalesce((SELECT "TotalAmount"::text FROM care_homes.bookings WHERE id = '{awaitingBookingId}'::uuid), ''),
  'awaitingDecisionHoldUntilUtc', coalesce((SELECT to_char("DecisionHoldUntilUtc" AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"') FROM care_homes.bookings WHERE id = '{awaitingBookingId}'::uuid), ''),
  'awaitingElderlyArabicNameHex', coalesce((SELECT encode(convert_to("ElderlyArabicName", 'UTF8'), 'hex') FROM care_homes.bookings WHERE id = '{awaitingBookingId}'::uuid), ''),
  'awaitingMerchantReference', coalesce((SELECT "MerchantReference" FROM care_homes.bookings WHERE id = '{awaitingBookingId}'::uuid), ''),
  'awaitingVersion', (SELECT "Version" FROM care_homes.bookings WHERE id = '{awaitingBookingId}'::uuid),
  'baseStatus', (SELECT "Status" FROM care_homes.bookings WHERE id = '{baseBookingId}'::uuid),
  'basePaymentStatus', (SELECT "PaymentStatus" FROM care_homes.bookings WHERE id = '{baseBookingId}'::uuid),
  'baseTaxAmount', coalesce((SELECT "TaxAmount"::text FROM care_homes.bookings WHERE id = '{baseBookingId}'::uuid), ''),
  'baseTotalAmount', coalesce((SELECT "TotalAmount"::text FROM care_homes.bookings WHERE id = '{baseBookingId}'::uuid), ''),
  'baseExtensionOfBookingId', coalesce((SELECT extension_of_booking_id::text FROM care_homes.bookings WHERE id = '{baseBookingId}'::uuid), ''),
  'baseVersion', (SELECT "Version" FROM care_homes.bookings WHERE id = '{baseBookingId}'::uuid),
  'extensionStatus', (SELECT "Status" FROM care_homes.bookings WHERE id = '{extensionBookingId}'::uuid),
  'extensionPaymentStatus', (SELECT "PaymentStatus" FROM care_homes.bookings WHERE id = '{extensionBookingId}'::uuid),
  'extensionTaxAmount', coalesce((SELECT "TaxAmount"::text FROM care_homes.bookings WHERE id = '{extensionBookingId}'::uuid), ''),
  'extensionExtensionOfBookingId', coalesce((SELECT extension_of_booking_id::text FROM care_homes.bookings WHERE id = '{extensionBookingId}'::uuid), ''),
  'extensionExtensionRootBookingId', coalesce((SELECT extension_root_booking_id::text FROM care_homes.bookings WHERE id = '{extensionBookingId}'::uuid), ''),
  'extensionStartDate', coalesce((SELECT to_char("StartDate", 'YYYY-MM-DD') FROM care_homes.bookings WHERE id = '{extensionBookingId}'::uuid), ''),
  'baseEndDate', coalesce((SELECT to_char("EndDate", 'YYYY-MM-DD') FROM care_homes.bookings WHERE id = '{baseBookingId}'::uuid), '')
  )::jsonb
  || json_build_object(
  'directConversationScope', (SELECT "Scope" FROM chat."Conversations" WHERE "Id" = '{directConversationId}'::uuid),
  'directConversationBookingId', coalesce((SELECT "BookingId"::text FROM chat."Conversations" WHERE "Id" = '{directConversationId}'::uuid), ''),
  'directConversationParticipantOne', coalesce((SELECT "ParticipantOne"::text FROM chat."Conversations" WHERE "Id" = '{directConversationId}'::uuid), ''),
  'directConversationParticipantTwo', coalesce((SELECT "ParticipantTwo"::text FROM chat."Conversations" WHERE "Id" = '{directConversationId}'::uuid), ''),
  'bookingConversationScope', (SELECT "Scope" FROM chat."Conversations" WHERE "Id" = '{bookingConversationId}'::uuid),
  'bookingConversationBookingId', coalesce((SELECT "BookingId"::text FROM chat."Conversations" WHERE "Id" = '{bookingConversationId}'::uuid), ''),
  'bookingConversationParticipantOne', coalesce((SELECT "ParticipantOne"::text FROM chat."Conversations" WHERE "Id" = '{bookingConversationId}'::uuid), ''),
  'bookingConversationParticipantTwo', coalesce((SELECT "ParticipantTwo"::text FROM chat."Conversations" WHERE "Id" = '{bookingConversationId}'::uuid), ''),
  'ownerBookingConversationScope', (SELECT "Scope" FROM chat."Conversations" WHERE "Id" = '{ownerBookingConversationId}'::uuid),
  'ownerBookingConversationBookingId', coalesce((SELECT "BookingId"::text FROM chat."Conversations" WHERE "Id" = '{ownerBookingConversationId}'::uuid), ''),
  'ownerBookingConversationParticipantOne', coalesce((SELECT "ParticipantOne"::text FROM chat."Conversations" WHERE "Id" = '{ownerBookingConversationId}'::uuid), ''),
  'ownerBookingConversationParticipantTwo', coalesce((SELECT "ParticipantTwo"::text FROM chat."Conversations" WHERE "Id" = '{ownerBookingConversationId}'::uuid), ''),
  'ownerFacilityBookingConversationScope', (SELECT "Scope" FROM chat."Conversations" WHERE "Id" = '{ownerFacilityBookingConversationId}'::uuid),
  'ownerFacilityBookingConversationBookingId', coalesce((SELECT "BookingId"::text FROM chat."Conversations" WHERE "Id" = '{ownerFacilityBookingConversationId}'::uuid), ''),
  'ownerFacilityBookingConversationParticipantOne', coalesce((SELECT "ParticipantOne"::text FROM chat."Conversations" WHERE "Id" = '{ownerFacilityBookingConversationId}'::uuid), ''),
  'ownerFacilityBookingConversationParticipantTwo', coalesce((SELECT "ParticipantTwo"::text FROM chat."Conversations" WHERE "Id" = '{ownerFacilityBookingConversationId}'::uuid), '')
  )::jsonb
  || json_build_object(
  'activeSessionsFixtureOwner', (SELECT count(*)::int FROM identity.device_sessions s WHERE s.user_id = '{ownerUserId}'::uuid AND s.revoked_on_utc IS NULL AND s.expires_on_utc > now()),
  'activeSessionsFixtureAdmin', (SELECT count(*)::int FROM identity.device_sessions s WHERE s.user_id = '{adminUserId}'::uuid AND s.revoked_on_utc IS NULL AND s.expires_on_utc > now()),
  'activeSessionsFamilyOwner', (SELECT count(*)::int FROM identity.device_sessions s JOIN identity.users u ON u.id = s.user_id WHERE u.email = 'family.owner@test.sanad.local' AND s.revoked_on_utc IS NULL AND s.expires_on_utc > now()),
  'activeSessionsMedicalCaregiver', (SELECT count(*)::int FROM identity.device_sessions s JOIN identity.users u ON u.id = s.user_id WHERE u.email = 'medical.caregiver@test.sanad.local' AND s.revoked_on_utc IS NULL AND s.expires_on_utc > now()),
  'activeSessionsContentAdmin', (SELECT count(*)::int FROM identity.device_sessions s JOIN identity.users u ON u.id = s.user_id WHERE u.email = '{credentialSourceEmail}' AND s.revoked_on_utc IS NULL AND s.expires_on_utc > now())
  )::jsonb
)::text;
'@

    foreach ($placeholder in @(
            '{ownerEmail}', '{adminEmail}', '{ownerAccountId}', '{adminAccountId}', '{facilityId}',
            '{roomTypeId}', '{awaitingBookingId}', '{baseBookingId}', '{extensionBookingId}',
            '{directConversationId}', '{bookingConversationId}', '{ownerBookingConversationId}',
            '{ownerFacilityBookingConversationId}',
            '{credentialSourceEmail}', '{ownerUserId}', '{adminUserId}')) {
        $value = switch ($placeholder) {
            '{ownerEmail}' { [string]$Definition.ownerEmail }
            '{adminEmail}' { [string]$Definition.adminEmail }
            '{ownerAccountId}' { [string]$Definition.ownerAccountId }
            '{adminAccountId}' { [string]$Definition.adminAccountId }
            '{facilityId}' { [string]$Definition.facilityId }
            '{roomTypeId}' { [string]$Definition.roomTypeId }
            '{awaitingBookingId}' { [string]$Definition.awaitingBookingId }
            '{baseBookingId}' { [string]$Definition.baseBookingId }
            '{extensionBookingId}' { [string]$Definition.extensionBookingId }
            '{directConversationId}' { [string]$Definition.directConversationId }
            '{bookingConversationId}' { [string]$Definition.bookingConversationId }
            '{ownerBookingConversationId}' { [string]$Definition.ownerBookingConversationId }
            '{ownerFacilityBookingConversationId}' { [string]$Definition.ownerFacilityBookingConversationId }
            '{credentialSourceEmail}' { [string]$Definition.credentialSourceEmail }
            '{ownerUserId}' { [string]$Definition.ownerUserId }
            '{adminUserId}' { [string]$Definition.adminUserId }
        }
        $sql = $sql.Replace($placeholder, [string]$value)
    }

    return $sql
}

# ---------------------------------------------------------------------------
# Runtime helpers
# ---------------------------------------------------------------------------

function Get-F2PsqlPath {
    <#
        Locates the local psql client. A missing client fails the run closed
        before anything is written.
    #>
    [CmdletBinding()]
    param()

    $command = Get-Command 'psql' -ErrorAction SilentlyContinue
    if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace([string]$command.Source)) {
        return [string]$command.Source
    }

    $candidates = @()
    foreach ($root in @("$env:ProgramFiles\PostgreSQL", "${env:ProgramFiles(x86)}\PostgreSQL")) {
        if ([string]::IsNullOrWhiteSpace($root) -or -not (Test-Path -LiteralPath $root)) { continue }
        $candidates += Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
            Sort-Object -Property Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'bin\psql.exe' }
    }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }

    return $null
}

function Get-F2DatabaseConnection {
    <#
        Resolves the local database connection the API itself uses, without ever
        surfacing it: the process environment first (the API reads the ambient
        connection string), then the API's Development settings file.
    #>
    [CmdletBinding()]
    param([string]$RepoRoot)

    $raw = ''
    $source = ''
    foreach ($name in @('ConnectionStrings__IdentityDatabase', 'ConnectionStrings:IdentityDatabase')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            $raw = $value
            $source = "environment:$name"
            break
        }
    }

    if ([string]::IsNullOrWhiteSpace($raw) -and -not [string]::IsNullOrWhiteSpace($RepoRoot)) {
        $settingsPath = Join-Path $RepoRoot 'src/API/Sanad.API/appsettings.Development.json'
        if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
            $settings = ConvertFrom-SafeJson ([System.IO.File]::ReadAllText($settingsPath))
            if ($null -ne $settings -and $null -ne $settings.ConnectionStrings) {
                $names = @($settings.ConnectionStrings.PSObject.Properties.Name)
                if ($names -contains 'IdentityDatabase') {
                    $candidate = [string]$settings.ConnectionStrings.IdentityDatabase
                    if (-not [string]::IsNullOrWhiteSpace($candidate)) {
                        $raw = $candidate
                        $source = 'appsettings.Development.json'
                    }
                }
            }
        }
    }

    $connection = ConvertFrom-F2PostgresConnectionString -ConnectionString $raw
    $connection['Source'] = $source
    return $connection
}

function Invoke-F2PsqlProcess {
    <#
        Runs one psql script file against the resolved local database with the
        connection supplied through the standard PG environment, restoring the
        caller environment and console encoding afterwards. Only the client's
        own output is returned, and it is redacted before it is surfaced.
    #>
    [CmdletBinding()]
    param(
        [string]$PsqlPath,
        [hashtable]$Connection,
        [string]$Sql,
        [string]$Tag
    )

    $tempFile = Join-Path ([System.IO.Path]::GetTempPath()) ("sanad-f2-" + $Tag + "-" + [Guid]::NewGuid().ToString('N') + ".sql")
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
            # Restore the caller environment exactly. SetEnvironmentVariable with a
            # null value leaves the variable present but empty, and a child process
            # that inherits an empty PGCLIENTENCODING makes PostgreSQL reject every
            # connection with 22023, so an unset variable must really be removed.
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

function Get-F2SafeOutputSummary {
    <#
        The last meaningful and redacted line of a client output, capped, so a
        failed statement can be reported without surfacing credentials.
    #>
    [CmdletBinding()]
    param([string[]]$Lines, [int]$MaxLength = 300)

    $all = @($Lines)
    for ($index = $all.Count - 1; $index -ge 0; $index--) {
        if ([string]::IsNullOrWhiteSpace($all[$index])) { continue }
        return (Get-TrimmedRedactedLine -Text $all[$index] -MaxLength $MaxLength)
    }
    return 'no client output'
}

function Invoke-F2PsqlQuery {
    <#
        Runs a read-only JSON query and returns the parsed object.
    #>
    [CmdletBinding()]
    param(
        [string]$PsqlPath,
        [hashtable]$Connection,
        [string]$Sql,
        [string]$Tag
    )

    $run = Invoke-F2PsqlProcess -PsqlPath $PsqlPath -Connection $Connection -Sql $Sql -Tag $Tag
    if ($run.ExitCode -ne 0) {
        return @{ Ok = $false; Value = $null; ErrorSummary = "psql exited with $($run.ExitCode): $(Get-F2SafeOutputSummary -Lines $run.Lines)" }
    }

    $text = (@($run.Lines) -join "`n").Trim()
    $value = ConvertFrom-SafeJson $text
    if ($null -eq $value) {
        return @{ Ok = $false; Value = $null; ErrorSummary = 'the read-only query did not return parseable JSON' }
    }

    return @{ Ok = $true; Value = $value; ErrorSummary = '' }
}

function Invoke-F2PsqlScript {
    <#
        Runs a provisioning script. A non-zero exit means the transaction was not
        committed (the guard blocks raise and roll everything back), so the caller
        reports the failure and stops before any HTTP proof.
    #>
    [CmdletBinding()]
    param(
        [string]$PsqlPath,
        [hashtable]$Connection,
        [string]$Sql,
        [string]$Tag
    )

    $run = Invoke-F2PsqlProcess -PsqlPath $PsqlPath -Connection $Connection -Sql $Sql -Tag $Tag
    $summary = ''
    if ($run.ExitCode -ne 0) {
        $summary = "psql exited with $($run.ExitCode): $(Get-F2SafeOutputSummary -Lines $run.Lines)"
    }
    return @{
        Ok           = ($run.ExitCode -eq 0)
        ExitCode     = $run.ExitCode
        ErrorSummary = $summary
    }
}

function Get-F2SessionLogoutPlan {
    <#
        The sessions this verifier created, named so it can release exactly its
        own. Each login adds one row to identity.device_sessions and the login
        policy allows five active sessions per identity, so a verifier that
        never logs out exhausts its own fixture identities' headroom after a
        few reruns. Only the tokens this run obtained are listed; unrelated
        sessions are never touched, and no new auth mechanism is involved: the
        existing logout-current route revokes the session the token belongs to.

        'fixtureOwner' is the same login as 'owner' (the provisioned owner
        fixture is reused under that conversation role), so it is reported under
        its canonical name and only once.
    #>
    [CmdletBinding()]
    param([System.Collections.IDictionary]$Tokens)

    $plan = @()
    if ($null -eq $Tokens) { return @($plan) }

    $canonicalNames = @{ fixtureOwner = 'owner' }
    $seen = @{}
    $seenTokens = @{}
    foreach ($role in @($Tokens.Keys | Sort-Object)) {
        $token = [string]$Tokens[$role]
        if ([string]::IsNullOrWhiteSpace($token)) { continue }
        $name = [string]$role
        if ($canonicalNames.ContainsKey($name)) { $name = [string]$canonicalNames[$name] }
        if ($seen.ContainsKey($name) -or $seenTokens.ContainsKey($token)) { continue }
        $seen[$name] = $true
        $seenTokens[$token] = $true
        $plan += [ordered]@{
            role           = $name
            path           = '/api/v1/auth/sessions/logout'
            expectedStatus = 204
        }
    }

    return @($plan)
}

function Invoke-F2HttpProbe {
    <#
        One HTTP request whose response headers are captured as well as the body,
        so the trace correlation can fall back to the correlation headers the API
        emits when a success body carries no trace id.
    #>
    [CmdletBinding()]
    param(
        [System.Net.Http.HttpClient]$Client,
        [string]$Method,
        [string]$Url,
        [AllowNull()][AllowEmptyString()][string]$Token
    )

    $request = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::new($Method)), $Url
    try {
        if (-not [string]::IsNullOrWhiteSpace($Token)) {
            $request.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue ('Bearer', $Token)
        }

        $response = $Client.SendAsync($request, [System.Net.Http.HttpCompletionOption]::ResponseContentRead).GetAwaiter().GetResult()
        try {
            $headers = [ordered]@{
                traceparent   = ''
                correlationId = ''
                requestId     = ''
            }
            $map = @{ 'traceparent' = 'traceparent'; 'x-correlation-id' = 'correlationId'; 'request-id' = 'requestId' }
            foreach ($name in $map.Keys) {
                $values = @()
                if ($response.Headers.Contains($name)) { $values = @($response.Headers.GetValues($name)) }
                elseif ($response.Content.Headers.Contains($name)) { $values = @($response.Content.Headers.GetValues($name)) }
                if ($values.Count -gt 0) { $headers[$map[$name]] = [string]$values[0] }
            }

            return @{
                Status  = [int]$response.StatusCode
                Body    = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                Headers = $headers
            }
        }
        finally {
            $response.Dispose()
        }
    }
    finally {
        $request.Dispose()
    }
}

function Invoke-F2PlannedChecks {
    <#
        Executes the planned authenticated loopback requests and evaluates each
        with the shared check evaluator, capturing the trace correlation from the
        response body or its correlation headers.
    #>
    [CmdletBinding()]
    param(
        [System.Net.Http.HttpClient]$Client,
        [string]$Url,
        [hashtable[]]$Plan,
        [string[]]$LogPath
    )

    $results = @()
    foreach ($check in $Plan) {
        $probe = Invoke-F2HttpProbe -Client $Client -Method $check.Method -Url "$Url$($check.Path)" -Token $check.Token
        $parsed = ConvertFrom-SafeJson $probe.Body

        $traceId = $null
        if ($null -ne $parsed -and @($parsed.PSObject.Properties.Name) -contains 'traceId') {
            $traceId = [string]$parsed.traceId
        }
        if ([string]::IsNullOrWhiteSpace($traceId)) {
            foreach ($candidate in @($probe.Headers.correlationId, $probe.Headers.traceparent, $probe.Headers.requestId)) {
                if (-not [string]::IsNullOrWhiteSpace([string]$candidate)) { $traceId = [string]$candidate; break }
            }
        }

        # The response exists, so any matching server log line is already on disk.
        $location = $null
        if (-not [string]::IsNullOrWhiteSpace($traceId)) {
            $location = Get-TraceLocation -TraceId $traceId -LogLines (Get-CapturedLogLines -Path $LogPath)
        }

        $outcome = Get-CheckOutcome -Check $check -Status $probe.Status -BodyText $probe.Body -TraceId $traceId -Location $location
        $outcome['responseHeaders'] = $probe.Headers
        $results += $outcome
    }

    return @($results)
}

function New-F2FailedCheck {
    <#
        A result entry for something that could not be checked at all, so the
        reported set keeps one shape and a missing prerequisite can never look
        like a pass.
    #>
    [CmdletBinding()]
    param([string]$Name, [string]$Actor, [string]$Expected, [string]$Problem)

    return [ordered]@{
        name             = $Name
        method           = '-'
        path             = '-'
        actor            = $Actor
        expected         = $Expected
        actual           = 'not run'
        passed           = $false
        problems         = @($Problem)
        traceId          = ''
        traceCorrelation = 'not-run'
        localLocation    = $null
        responseHeaders  = [ordered]@{ traceparent = ''; correlationId = ''; requestId = '' }
    }
}

function New-F2CheckPlan {
    <#
        Builds the planned authenticated loopback GET proofs: the owner route with
        its stored-field, deadline and scope assertions, the admin route with the
        stored tax and base/extension assertions plus its filter, paging and
        invalid-query behaviour, the chat route for every identity that could log
        in, and the anonymous and role-denied negative cases. A check whose token
        is unavailable is reported as not run instead of silently disappearing.
    #>
    [CmdletBinding()]
    param(
        [hashtable]$Definition,
        [object]$Snapshot,
        [hashtable]$Tokens,
        [string[]]$ChatRoles,
        [hashtable]$ExpectationsByRole
    )

    $ownerAssert = { param($status, $body) Test-F2OwnerListBody -Body $body -Definition $Definition -Snapshot $Snapshot }.GetNewClosure()
    $adminDefaultAssert = { param($status, $body) Test-F2AdminListBody -Body $body -Definition $Definition -Snapshot $Snapshot -Mode 'default' }.GetNewClosure()
    $adminFilterAssert = { param($status, $body) Test-F2AdminListBody -Body $body -Definition $Definition -Snapshot $Snapshot -Mode 'statusFilter' }.GetNewClosure()
    $adminPagingAssert = { param($status, $body) Test-F2AdminListBody -Body $body -Definition $Definition -Snapshot $Snapshot -Mode 'paging' }.GetNewClosure()
    $statusSummary = { param($status, $body) @{ Actual = "status=$status $(Get-SafeErrorSummary $body)"; Problems = @() } }.GetNewClosure()

    $ownerPath = '/api/v1/care-homes/booking-requests/mine'
    $adminPath = '/api/v1/admin/care-homes/bookings'
    $chatPath = '/api/v1/chat/conversations'

    $plan = @()

    if ($Tokens.ContainsKey('owner')) {
        $plan += @{
            Name = 'owner-list'
            Method = 'GET'
            Path = $ownerPath
            Actor = 'carehome-owner-fixture'
            Token = $Tokens['owner']
            ExpectedStatus = 200
            Expected = 'the owned paid and extension rows with the stored resident, contact, note and decision-deadline values and no medical snapshot'
            Assert = $ownerAssert
        }
    }
    else {
        $plan += @{}
    }

    if ($Tokens.ContainsKey('admin')) {
        $plan += @{
            Name = 'admin-list'
            Method = 'GET'
            Path = "$adminPath`?page=1&pageSize=25"
            Actor = 'support-admin-fixture'
            Token = $Tokens['admin']
            ExpectedStatus = 200
            Expected = 'the three fixture bookings with their stored tax amounts and the base booking / extension link distinction'
            Assert = $adminDefaultAssert
        }
        $plan += @{
            Name = 'admin-list-status-filter'
            Method = 'GET'
            Path = "$adminPath`?bookingStatus=3"
            Actor = 'support-admin-fixture'
            Token = $Tokens['admin']
            ExpectedStatus = 200
            Expected = 'only the accepted fixture bookings, still carrying their stored tax amounts'
            Assert = $adminFilterAssert
        }
        $plan += @{
            Name = 'admin-list-paging'
            Method = 'GET'
            Path = "$adminPath`?page=1&pageSize=1"
            Actor = 'support-admin-fixture'
            Token = $Tokens['admin']
            ExpectedStatus = 200
            Expected = 'one page item with the full fixture total count and page count'
            Assert = $adminPagingAssert
        }
        $plan += @{
            Name = 'admin-list-invalid-status'
            Method = 'GET'
            Path = "$adminPath`?bookingStatus=999"
            Actor = 'support-admin-fixture'
            Token = $Tokens['admin']
            ExpectedStatus = 400
            Expected = 'a validation problem for an out-of-range booking status filter'
            Assert = $statusSummary
        }
        $plan += @{
            Name = 'admin-list-role-denied'
            Method = 'GET'
            Path = $adminPath
            Actor = 'carehome-owner-fixture'
            Token = $(if ($Tokens.ContainsKey('owner')) { $Tokens['owner'] } else { '' })
            ExpectedStatus = 403
            Expected = 'the operational-admin policy refusing a care-home owner token'
            Assert = $statusSummary
        }
        $plan += @{
            Name = 'owner-list-role-denied'
            Method = 'GET'
            Path = $ownerPath
            Actor = 'support-admin-fixture'
            Token = $Tokens['admin']
            ExpectedStatus = 403
            Expected = 'the care-home owner policy refusing a support-admin token'
            Assert = $statusSummary
        }
    }
    else {
        $plan += @{}
    }

    foreach ($role in @($ChatRoles)) {
        # A role with no expected conversation returns an empty collection, which
        # PowerShell collapses to $null; drop that so the comparison sees a
        # genuinely empty set instead of one null item.
        $assertExpectations = @(@($ExpectationsByRole[$role]) | Where-Object { $null -ne $_ })
        $roleKeys = @($assertExpectations | ForEach-Object { $_.key })
        $chatAssert = { param($status, $body) Test-F2ChatListBody -Body $body -Definition $Definition -Expectations $assertExpectations }.GetNewClosure()
        $plan += @{
            Name = "chat-conversations-$role"
            Method = 'GET'
            Path = $chatPath
            Actor = $role
            Token = $Tokens[$role]
            ExpectedStatus = 200
            Expected = "only the conversations of this participant ($($roleKeys -join ', ')) with their persisted scope, booking reference and counterpart"
            Assert = $chatAssert
        }
    }

    $plan += @{
        Name = 'owner-list-anonymous'
        Method = 'GET'
        Path = $ownerPath
        Actor = 'anonymous'
        Token = ''
        ExpectedStatus = 401
        Expected = 'the owner route refusing an unauthenticated request'
        Assert = $statusSummary
    }
    $plan += @{
        Name = 'admin-list-anonymous'
        Method = 'GET'
        Path = $adminPath
        Actor = 'anonymous'
        Token = ''
        ExpectedStatus = 401
        Expected = 'the admin route refusing an unauthenticated request'
        Assert = $statusSummary
    }
    $plan += @{
        Name = 'chat-conversations-anonymous'
        Method = 'GET'
        Path = $chatPath
        Actor = 'anonymous'
        Token = ''
        ExpectedStatus = 401
        Expected = 'the chat route refusing an unauthenticated request'
        Assert = $statusSummary
    }

    return @($plan | Where-Object { $_.Count -gt 0 })
}

# ---------------------------------------------------------------------------
# Main flow
# ---------------------------------------------------------------------------

if ($f2LibraryOnly) { return }

$repoRoot = Get-RepositoryRoot

if ($f2ExpectedWorktreeRoot) {
    $expected = Resolve-FullPath $f2ExpectedWorktreeRoot
    if (-not $expected.Equals($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'ExpectedWorktreeRoot does not match this script''s repository root.'
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'AGENTS.md') -PathType Leaf)) {
    throw 'Run this script from the Sanad API repository checkout.'
}

$head = Get-PinnedHead $repoRoot
$uri = Assert-LoopbackBaseUrl $f2BaseUrl
$baseUrl = "$($uri.Scheme)://$($uri.Authority)"

$apiDirectory = Join-Path $repoRoot 'src/API/Sanad.API'
if (-not $f2ApiAssemblyPath) { $f2ApiAssemblyPath = Join-Path $apiDirectory 'bin/Release/net10.0/Sanad.API.dll' }
$assemblyPath = Resolve-FullPath $f2ApiAssemblyPath
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw "The API assembly was not found at $assemblyPath. Build the Release API before running this verification."
}

$definition = Get-F2FixtureDefinition
$checkResults = @()
$notes = @()
$summary = [ordered]@{
    workflow        = 'f2-local-http-verification'
    head            = $head
    baseUrl         = $baseUrl
    apiAssembly     = $assemblyPath.Replace($repoRoot, '<repo-root>').Replace('\', '/')
    fixtureProvisioningSkipped = [bool]$f2SkipFixtureProvisioning
    targetVerification = [ordered]@{}
    databaseTarget  = [ordered]@{}
    provisioning    = [ordered]@{}
    fixtureRows     = [ordered]@{}
    checks          = @()
    results         = [ordered]@{ total = 0; passed = 0; failed = 0 }
    chatCoverage    = [ordered]@{ directScopeProven = $false; bookingScopeProven = $false; bookingScopeProvenByDomainRow = $false; requesters = @() }
    chatLinkage     = [ordered]@{ conversations = @(); legacyNotDomainProducible = @() }
    sessionCleanup  = [ordered]@{ attempted = 0; revoked = 0; failed = 0; roles = @() }
    loginNotes      = @()
    api             = [ordered]@{ reused = $false; started = $false; logPaths = @() }
    limitations     = @()
    notes           = @()
}

function Write-F2Result {
    [CmdletBinding()]
    param([hashtable]$Value, [int]$ExitCode)

    $Value | ConvertTo-Json -Depth 12
    exit $ExitCode
}

$summary.limitations = @(
    'Fixture rows are written with INSERT-only SQL that mirrors the persisted shape of the CareHomes and Chat factories: no local HTTP or domain path can produce a paid care-home booking without a live payment provider call, and the local Paymob webhook secret is absent (the webhook answers 503 locally).'
    'The fixture facility is a domain-valid Draft with no profile revision, because approving it would need verified documents and an operational-admin review; the three read routes under proof do not depend on the facility status, and the in-transaction guard re-derives every booking-scoped participant pair from the booking it references.'
    'One pre-existing fixture conversation (the ownerBooking row) binds the provisioned care-home owner fixture to the reused caregiver booking. That pairing is not domain-producible - ApiChatBookingGateway maps a Families booking to its creator and the booked caregiver - so it is reported under chatLinkage as legacyNotDomainProducible and is never used as booking-scope evidence; the booking-scope proof comes from the ownerFacilityBooking row, which references a booking the fixture facility owns and pairs it with that booking''s family user. Removing the legacy row is a destructive local repair and is not performed by this verifier.'
    'Each successful login consumes one of the five active device-session slots the login policy allows per identity. This verifier logs out every session it created through the existing logout-current route after its checks, so repeated runs do not consume further headroom; identities already at the cap before the run are still skipped and reported as not run rather than revoked, and the checks that reuse pre-existing synthetic identities remain best effort.'
    'Two-owner isolation is not proven by this run. The local HTTP proof establishes facility-derived scoping (every returned owner row carries the fixture facility and room type, and the page holds exactly the three fixture bookings) and role denial (403 for the wrong role, 401 anonymous); the separate two-owner isolation evidence is the accepted in-process production-pipeline test Owner_list_stays_facility_scoped_and_returns_an_empty_list_for_an_owner_without_bookings plus the earlier controller-pipeline HTTP proof, and only one care-home owner identity is authorized for this local run.'
    'Trace correlation is unavailable in this configuration: the L1 library starts the API with Logging__LogLevel__Default=Warning, so successful requests emit no captured log line to match, and success bodies carry no trace id. The captured x-correlation-id/traceparent values and any ProblemDetails traceId stay in each check result so a failure can still be matched by hand.'
    'The verifier needs the local PostgreSQL client (psql) to provision and read the fixtures back; a missing client fails the run closed before any write.'
)

# 1. Read-only target verification before anything else.
$processEnvironment = Get-LocalFixtureEnvironment -Password $f2FixturePassword -EnableSeed
$preflight = Invoke-TargetVerification -RepoRoot $repoRoot -AssemblyPath $assemblyPath -Environment $processEnvironment -ApiDirectory $apiDirectory
$preflightReport = $preflight.report

$summary.targetVerification = [ordered]@{
    passed                   = [bool]$preflight.passed
    exitCode                 = $preflight.exitCode
    environmentName          = $(if ($null -ne $preflightReport) { [string]$preflightReport.environmentName } else { '' })
    targetMode               = $(if ($null -ne $preflightReport) { [string]$preflightReport.targetMode } else { '' })
    fixtureIdentityPresentCount = $(if ($null -ne $preflightReport) { [int]$preflightReport.fixtureIdentityPresentCount } else { 0 })
    activeFixtureSessionCount   = $(if ($null -ne $preflightReport) { [int]$preflightReport.activeFixtureSessionCount } else { 0 })
    modules                  = @()
}
if ($null -ne $preflightReport) {
    $summary.targetVerification.modules = @(@($preflightReport.modules) | ForEach-Object {
            [ordered]@{
                module             = [string]$_.module
                host               = [string]$_.host
                port               = [int]$_.port
                database           = [string]$_.database
                pendingMigrations  = [int]$_.pendingMigrations
                passed             = [bool]$_.passed
            }
        })
}

$psqlPath = Get-F2PsqlPath
$connection = Get-F2DatabaseConnection -RepoRoot $repoRoot
$summary.databaseTarget = [ordered]@{
    host               = [string]$connection.Host
    port               = [string]$connection.Port
    database           = [string]$connection.Database
    source             = [string]$connection.Source
    psqlClientLocated  = (-not [string]::IsNullOrWhiteSpace($psqlPath))
    passwordSupplied   = (-not [string]::IsNullOrWhiteSpace([string]$connection.Password))
}

$guard = Test-F2FixtureTarget -VerificationPassed ([bool]$preflight.passed) -Report $preflightReport -Connection $connection
if ([string]::IsNullOrWhiteSpace($psqlPath)) {
    $guard = @{ Ok = $false; Problems = @($guard.Problems) + @('the local PostgreSQL client (psql) was not found, so the fixtures cannot be provisioned or read back') }
}
if (-not $guard.Ok) {
    $summary.targetVerification.problems = @($guard.Problems)
    $summary.notes = @('the run stopped before any fixture write because the target guard failed')
    Write-F2Result -Value $summary -ExitCode 1
}

$stateQuery = Get-F2StateQuery -Definition $definition

# 2. Read-only state snapshot and the resolution of the existing rows reused by
#    the fixtures.
$before = Invoke-F2PsqlQuery -PsqlPath $psqlPath -Connection $connection -Sql $stateQuery -Tag 'state-before'
if (-not $before.Ok) {
    $summary.notes = @("the read-only before snapshot failed: $($before.ErrorSummary)")
    Write-F2Result -Value $summary -ExitCode 1
}

$resolved = @{
    familyOwnerUserId      = [string]$before.Value.familyOwnerUserId
    medicalCaregiverUserId = [string]$before.Value.medicalCaregiverUserId
    chatBookingId          = [string]$before.Value.chatBookingId
}
$resolutionProblems = @()
foreach ($key in @('familyOwnerUserId', 'medicalCaregiverUserId', 'chatBookingId')) {
    if ([string]::IsNullOrWhiteSpace([string]$resolved[$key])) {
        $resolutionProblems += "the existing row reused for '$key' was not found in the local database"
    }
}
if ($resolutionProblems.Count -gt 0) {
    $summary.notes = $resolutionProblems
    Write-F2Result -Value $summary -ExitCode 1
}

$summary.fixtureRows = [ordered]@{
    before = [ordered]@{
        careHomeFacilityCount  = [int]$before.Value.careHomeFacilityCount
        careHomeRoomTypeCount  = [int]$before.Value.careHomeRoomTypeCount
        careHomeBookingCount   = [int]$before.Value.careHomeBookingCount
        chatConversationCount  = [int]$before.Value.chatConversationCount
        fixtureIdentityCount   = [int]$before.Value.fixtureIdentityUserCount
    }
    after = [ordered]@{}
    added = [ordered]@{}
    ids = [ordered]@{
        careHomeOwnerUserId = [string]$definition.ownerUserId
        supportAdminUserId  = [string]$definition.adminUserId
        facilityId          = [string]$definition.facilityId
        roomTypeId          = [string]$definition.roomTypeId
        awaitingBookingId   = [string]$definition.awaitingBookingId
        baseBookingId       = [string]$definition.baseBookingId
        extensionBookingId  = [string]$definition.extensionBookingId
        directConversationId = [string]$definition.directConversationId
        bookingConversationId = [string]$definition.bookingConversationId
        ownerBookingConversationId = [string]$definition.ownerBookingConversationId
        reusedChatBookingId = [string]$resolved.chatBookingId
        reusedFamilyOwnerUserId = [string]$resolved.familyOwnerUserId
        reusedMedicalCaregiverUserId = [string]$resolved.medicalCaregiverUserId
    }
}

# 3. Provision with the INSERT-only script and read the state straight back.
$fixtureSql = Get-F2FixtureSql -Definition $definition -Resolved $resolved
$provisioningRuns = @()
$state = $null

if (-not $f2SkipFixtureProvisioning) {
    $firstRun = Invoke-F2PsqlScript -PsqlPath $psqlPath -Connection $connection -Sql $fixtureSql -Tag 'provision'
    $provisioningRuns += [ordered]@{ run = 1; ok = [bool]$firstRun.Ok; exitCode = [int]$firstRun.ExitCode; error = [string]$firstRun.ErrorSummary }
    if (-not $firstRun.Ok) {
        $summary.provisioning = [ordered]@{ runs = $provisioningRuns; idempotent = $false }
        $summary.notes = @('the fixture provisioning transaction did not commit; the guard blocks raise and roll the whole transaction back, so no fixture row was written')
        Write-F2Result -Value $summary -ExitCode 1
    }
}

$afterFirst = Invoke-F2PsqlQuery -PsqlPath $psqlPath -Connection $connection -Sql $stateQuery -Tag 'state-after-first'
if (-not $afterFirst.Ok) {
    $summary.provisioning = [ordered]@{ runs = $provisioningRuns; idempotent = $false }
    $summary.notes = @("the read-only read-back after provisioning failed: $($afterFirst.ErrorSummary)")
    Write-F2Result -Value $summary -ExitCode 1
}
$state = $afterFirst.Value

$idempotent = $null
if (-not $f2SkipFixtureProvisioning) {
    $secondRun = Invoke-F2PsqlScript -PsqlPath $psqlPath -Connection $connection -Sql $fixtureSql -Tag 'provision-repeat'
    $provisioningRuns += [ordered]@{ run = 2; ok = [bool]$secondRun.Ok; exitCode = [int]$secondRun.ExitCode; error = [string]$secondRun.ErrorSummary }
    if (-not $secondRun.Ok) {
        $summary.provisioning = [ordered]@{ runs = $provisioningRuns; idempotent = $false }
        $summary.notes = @('the repeated fixture provisioning did not commit, so idempotence could not be proven')
        Write-F2Result -Value $summary -ExitCode 1
    }

    $afterSecond = Invoke-F2PsqlQuery -PsqlPath $psqlPath -Connection $connection -Sql $stateQuery -Tag 'state-after-second'
    if (-not $afterSecond.Ok) {
        $summary.provisioning = [ordered]@{ runs = $provisioningRuns; idempotent = $false }
        $summary.notes = @("the read-only read-back after the repeated provisioning failed: $($afterSecond.ErrorSummary)")
        Write-F2Result -Value $summary -ExitCode 1
    }

    $firstJson = ($afterFirst.Value | ConvertTo-Json -Depth 8 -Compress)
    $secondJson = ($afterSecond.Value | ConvertTo-Json -Depth 8 -Compress)
    $idempotent = ($firstJson -eq $secondJson)
    $state = $afterSecond.Value
    if (-not $idempotent) {
        $summary.provisioning = [ordered]@{ runs = $provisioningRuns; idempotent = $false }
        $summary.notes = @('the repeated provisioning run changed the fixture state, so it was not idempotent')
        Write-F2Result -Value $summary -ExitCode 1
    }
}

$summary.provisioning = [ordered]@{
    runs       = $provisioningRuns
    idempotent = $idempotent
    guard      = 'prerequisite and read-back blocks inside the same transaction; a mismatch raises and rolls the transaction back'
}

$summary.fixtureRows.after = [ordered]@{
    careHomeFacilityCount  = [int]$state.careHomeFacilityCount
    careHomeRoomTypeCount  = [int]$state.careHomeRoomTypeCount
    careHomeBookingCount   = [int]$state.careHomeBookingCount
    chatConversationCount  = [int]$state.chatConversationCount
    fixtureIdentityCount   = [int]$state.fixtureIdentityUserCount
    fixtureIdentityAccountCount = [int]$state.fixtureIdentityAccountCount
    fixtureFacilityCount   = [int]$state.fixtureFacilityCount
    fixtureRoomTypeCount   = [int]$state.fixtureRoomTypeCount
    fixtureBookingCount    = [int]$state.fixtureBookingCount
    fixtureConversationCount = [int]$state.fixtureConversationCount
}
$summary.fixtureRows.added = [ordered]@{
    identityUsers        = [int]$state.fixtureIdentityUserCount - [int]$before.Value.fixtureIdentityUserCount
    careHomeFacilities   = [int]$state.careHomeFacilityCount - [int]$before.Value.careHomeFacilityCount
    careHomeRoomTypes    = [int]$state.careHomeRoomTypeCount - [int]$before.Value.careHomeRoomTypeCount
    careHomeBookings     = [int]$state.careHomeBookingCount - [int]$before.Value.careHomeBookingCount
    chatConversations    = [int]$state.chatConversationCount - [int]$before.Value.chatConversationCount
    featureTableRows     = ([int]$state.fixtureFacilityCount + [int]$state.fixtureRoomTypeCount + [int]$state.fixtureBookingCount + [int]$state.fixtureConversationCount)
}

# 4. Fixture read-back assertions before any HTTP request.
$readBackProblems = @()
if ([int]$state.fixtureIdentityUserCount -ne 2) { $readBackProblems += "expected 2 provisioned identity users, found $($state.fixtureIdentityUserCount)" }
if ([int]$state.fixtureIdentityAccountCount -ne 2) { $readBackProblems += "expected 2 provisioned user accounts, found $($state.fixtureIdentityAccountCount)" }
if ([string]$state.fixtureOwnerAccountType -ne [string]$definition.ownerAccountType) { $readBackProblems += 'the provisioned care-home owner account does not hold the care-home owner account type' }
if ([string]$state.fixtureAdminAccountType -ne [string]$definition.adminAccountType) { $readBackProblems += 'the provisioned support identity does not hold the support-admin account type' }
if ([int]$state.fixtureFacilityCount -ne 1) { $readBackProblems += 'the fixture facility row was not found after provisioning' }
if ([int]$state.fixtureRoomTypeCount -ne 1) { $readBackProblems += 'the fixture room type row was not found after provisioning' }
if ([int]$state.fixtureBookingCount -ne 3) { $readBackProblems += "expected 3 fixture bookings, found $($state.fixtureBookingCount)" }
if ([int]$state.fixtureConversationCount -ne 4) { $readBackProblems += "expected 4 fixture conversations, found $($state.fixtureConversationCount)" }
if ([string]::IsNullOrWhiteSpace([string]$state.awaitingDecisionHoldUntilUtc)) { $readBackProblems += 'the paid-awaiting-decision booking carries no stored decision deadline' }
elseif (-not [bool]((ConvertTo-F2Instant -Value $state.awaitingDecisionHoldUntilUtc) -gt [DateTimeOffset]::UtcNow)) { $readBackProblems += 'the stored decision deadline of the awaiting-decision booking is no longer in the future' }
if ([string]::IsNullOrWhiteSpace([string]$state.awaitingElderlyArabicNameHex)) { $readBackProblems += 'the awaiting-decision booking carries no stored resident Arabic name' }
if ([string]$state.awaitingMerchantReference -ne [string]$definition.awaitingMerchantReference) { $readBackProblems += 'the awaiting-decision booking merchant reference does not match the fixture definition' }
if ([string]$state.baseExtensionOfBookingId -ne '') { $readBackProblems += 'the accepted base booking is stored as an extension of another booking' }
if ([string]$state.extensionExtensionOfBookingId -ne [string]$definition.baseBookingId) { $readBackProblems += 'the extension booking is not stored against the accepted base booking' }
if ([string]$state.extensionExtensionRootBookingId -ne [string]$definition.baseBookingId) { $readBackProblems += 'the extension booking root reference does not match the base booking' }
if ([string]$state.extensionStartDate -ne [string]$state.baseEndDate) { $readBackProblems += 'the extension booking does not start when the base booking ends' }
if ([int]$state.directConversationScope -ne 0 -or [string]$state.directConversationBookingId -ne '') { $readBackProblems += 'the direct conversation is not stored with scope 0 and no booking reference' }
foreach ($bookingConversation in @(
        @{ name = 'bookingConversation'; bookingId = [string]$resolved.chatBookingId; label = 'the reused caregiver booking' },
        @{ name = 'ownerBookingConversation'; bookingId = [string]$resolved.chatBookingId; label = 'the reused caregiver booking' },
        @{ name = 'ownerFacilityBookingConversation'; bookingId = [string]$state.baseBookingId; label = 'the fixture facility booking' })) {
    if ([int]$state."$($bookingConversation.name)Scope" -ne 1) { $readBackProblems += "$($bookingConversation.name) is not stored with the booking scope" }
    if ([string]$state."$($bookingConversation.name)BookingId" -ne [string]$bookingConversation.bookingId) { $readBackProblems += "$($bookingConversation.name) is not stored against $($bookingConversation.label)" }
}
foreach ($conversation in @($definition.conversations)) {
    $one = [string]$state."$($conversation.key)ConversationParticipantOne"
    $two = [string]$state."$($conversation.key)ConversationParticipantTwo"
    if ([string]::IsNullOrWhiteSpace($one) -or [string]::IsNullOrWhiteSpace($two)) {
        $readBackProblems += "the '$($conversation.key)' conversation participants were not read back"
        continue
    }
    $pair = Get-F2NormalizedPair -First $one -Second $two
    if ([string]$pair.One -ne $one -or [string]$pair.Two -ne $two) {
        $readBackProblems += "the '$($conversation.key)' conversation participants are not stored in the canonical pair order"
    }
}

# 4b. Domain linkage of every booking-scoped conversation. The stored pair is
#     re-derived from the booking the row references, using the same party rules
#     the API applies (ApiChatBookingGateway), so a synthetic SQL-only pairing
#     can never be presented as authorization evidence. Such a row is reported
#     as legacy and excluded from the booking-scope proof, which needs a row the
#     running API could itself have produced.
$chatLinkage = @(Test-F2ConversationLinkage -Definition $definition -Snapshot $state)
$summary.chatLinkage.conversations = @($chatLinkage | ForEach-Object {
        [ordered]@{
            key               = [string]$_.key
            id                = [string]$_.id
            scope             = [int]$_.scope
            partyRule         = [string]$_.partyRule
            expectedBookingId = [string]$_.expectedBookingId
            storedBookingId   = [string]$_.storedBookingId
            domainConsistent  = [bool]$_.domainConsistent
        }
    })
$summary.chatLinkage.legacyNotDomainProducible = @($chatLinkage |
        Where-Object { [string]$_.partyRule -eq 'sqlOnlyLegacy' } |
        ForEach-Object { [string]$_.key })
foreach ($linkageEntry in $chatLinkage) {
    foreach ($linkageProblem in @($linkageEntry.problems)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$linkageProblem)) { $readBackProblems += [string]$linkageProblem }
    }
}
$summary.chatCoverage.bookingScopeProvenByDomainRow = (@($chatLinkage |
        Where-Object { [int]$_.scope -eq 1 -and [bool]$_.domainConsistent }).Count -gt 0)
if (-not $summary.chatCoverage.bookingScopeProvenByDomainRow) {
    $readBackProblems += 'no booking-scoped conversation is consistent with the booking it references, so the booking-scope proof would have no domain-grade row'
}

if ($readBackProblems.Count -gt 0) {
    $summary.notes = $readBackProblems
    Write-F2Result -Value $summary -ExitCode 1
}

$summary.notes += 'the fixture rows were read back from the local database after provisioning'
$summary.notes += "the stored decision deadline and resident Arabic name of the awaiting-decision booking were captured read-only for the HTTP comparison"
$summary.notes += "the stored participant pair of every conversation fixture was re-derived from the booking it references: $(@($chatLinkage).Count) row(s) checked, $(@($chatLinkage | Where-Object { [int]$_.scope -eq 1 -and [bool]$_.domainConsistent }).Count) domain-consistent booking-scoped row(s)"

# 5. Serve or reuse the local API and run the authenticated loopback proofs.
$apiProcess = $null
$apiStarted = $false
$apiReused = $false
$logPaths = @()
$client = $null
$chatRoles = @()
$expectationsByRole = @{}

try {
    if (Test-BaseUrlReachable -Url $baseUrl) {
        $apiReused = $true
    }
    elseif ($f2SkipApiStart) {
        $summary.notes += 'no local API answered the readiness probe and -SkipApiStart was requested'
        Write-F2Result -Value $summary -ExitCode 1
    }
    else {
        $serveEnvironment = Get-LocalFixtureEnvironment -Password $f2FixturePassword
        $logDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("sanad-f2-logs-" + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
        $stdoutLog = Join-Path $logDirectory 'api.stdout.log'
        $stderrLog = Join-Path $logDirectory 'api.stderr.log'
        $logPaths = @($stdoutLog, $stderrLog)

        # The started process inherits this environment, and an empty PG* value
        # (for example an empty PGCLIENTENCODING left behind by a client run)
        # makes PostgreSQL reject every connection the API opens, which surfaces
        # as HTTP 500 on every request. Remove empty ones before starting.
        foreach ($pgName in @('PGCLIENTENCODING', 'PGHOST', 'PGPORT', 'PGDATABASE', 'PGUSER', 'PGPASSWORD', 'PGSSLMODE', 'PGCONNECT_TIMEOUT', 'PGOPTIONS')) {
            if ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($pgName))) {
                Remove-Item -LiteralPath ("Env:" + $pgName) -ErrorAction SilentlyContinue
            }
        }

        $apiProcess = Start-FixtureApiProcess -AssemblyPath $assemblyPath -WorkingDirectory $apiDirectory `
            -Url $baseUrl -Environment $serveEnvironment -OutputLogPath $stdoutLog -ErrorLogPath $stderrLog
        $apiStarted = $true
        if (-not (Wait-ForEndpoint -Url $baseUrl -TimeoutSeconds 120 -Process $apiProcess)) {
            $summary.notes += 'the local API did not answer the readiness probe within the timeout'
            Write-F2Result -Value $summary -ExitCode 1
        }
    }

    $summary.api = [ordered]@{
        reused   = $apiReused
        started  = $apiStarted
        logPaths = @($logPaths | ForEach-Object { Get-TrimmedRedactedLine -Text ([string]$_) })
    }

    $client = New-SanadHttpClient
    $tokens = @{}

    # Required: the two provisioned fixture identities.
    foreach ($requiredLogin in @(
            @{ role = 'owner'; email = [string]$definition.ownerEmail; device = 'f2-verify-owner'; actor = 'carehome-owner-fixture' },
            @{ role = 'admin'; email = [string]$definition.adminEmail; device = 'f2-verify-admin'; actor = 'support-admin-fixture' })) {
        try {
            $tokens[$requiredLogin.role] = Get-FixtureToken -Client $client -Url $baseUrl `
                -Email $requiredLogin.email -Password $f2FixturePassword -DeviceName $requiredLogin.device
        }
        catch {
            $checkResults += New-F2FailedCheck -Name "login-$($requiredLogin.role)" -Actor $requiredLogin.actor `
                -Expected 'a session-bound access token for the provisioned fixture identity' `
                -Problem (Get-TrimmedRedactedLine -Text ([string]$_.Exception.Message))
        }
    }

    # Best effort: the pre-existing synthetic identities the checks reuse. A
    # rejected login changes no state, and its check is reported as not run
    # rather than as a pass. The login policy allows five active sessions per
    # identity, so an identity that is already at the cap is skipped without
    # attempting a login at all.
    if ($tokens.ContainsKey('owner')) {
        # The conversation fixtures name their participants by role, and the
        # provisioned care-home owner fixture is the 'fixtureOwner' role.
        $tokens['fixtureOwner'] = $tokens['owner']
        $chatRoles += 'fixtureOwner'
    }
    $sessionLimits = [ordered]@{
        familyOwner      = [int]$state.activeSessionsFamilyOwner
        medicalCaregiver = [int]$state.activeSessionsMedicalCaregiver
        contentAdmin     = [int]$state.activeSessionsContentAdmin
    }
    foreach ($optionalLogin in @(
            @{ role = 'familyOwner'; email = 'family.owner@test.sanad.local'; device = 'f2-verify-family-owner' },
            @{ role = 'medicalCaregiver'; email = 'medical.caregiver@test.sanad.local'; device = 'f2-verify-medical-caregiver' },
            @{ role = 'contentAdmin'; email = [string]$definition.credentialSourceEmail; device = 'f2-verify-content-admin' })) {
        $activeSessions = [int]$sessionLimits[$optionalLogin.role]
        if ($activeSessions -ge 5) {
            $summary.loginNotes += "the pre-existing synthetic identity '$($optionalLogin.role)' already holds $activeSessions active device sessions, the policy maximum, so no login was attempted and its visibility check was not run"
            continue
        }
        try {
            $tokens[$optionalLogin.role] = Get-FixtureToken -Client $client -Url $baseUrl `
                -Email $optionalLogin.email -Password $f2FixturePassword -DeviceName $optionalLogin.device
            $chatRoles += $optionalLogin.role
            $summary.loginNotes += "the pre-existing synthetic identity '$($optionalLogin.role)' accepted the shared fixture credential and was used for its own visibility check"
        }
        catch {
            $summary.loginNotes += "the pre-existing synthetic identity '$($optionalLogin.role)' did not accept the shared fixture credential, so its visibility check was not run: $(Get-TrimmedRedactedLine -Text ([string]$_.Exception.Message) -MaxLength 200)"
        }
    }
    $chatRoles = @($chatRoles | Select-Object -Unique)

    $roles = Get-F2RoleMap -Definition $definition -Snapshot $state
    foreach ($role in $chatRoles) {
        $expectationsByRole[$role] = @(Get-F2ConversationExpectations -Definition $definition -Roles $roles -Snapshot $state -RequesterRole $role)
    }

    $plan = New-F2CheckPlan -Definition $definition -Snapshot $state -Tokens $tokens -ChatRoles $chatRoles -ExpectationsByRole $expectationsByRole
    $checkResults += Invoke-F2PlannedChecks -Client $client -Url $baseUrl -Plan $plan -LogPath $logPaths

    # 5b. Release exactly the sessions this run created. Every login adds one row
    #     to identity.device_sessions and the login policy allows five active
    #     sessions per identity, so a verifier that never logs out exhausts its
    #     own fixture identities after a few reruns. Only tokens obtained above
    #     are listed; the existing logout-current route revokes the session of
    #     the presented token and unrelated sessions are never touched.
    $logoutPlan = @(Get-F2SessionLogoutPlan -Tokens $tokens)
    $summary.sessionCleanup.roles = @($logoutPlan | ForEach-Object { [string]$_.role })
    foreach ($logout in $logoutPlan) {
        $summary.sessionCleanup.attempted = [int]$summary.sessionCleanup.attempted + 1
        try {
            $logoutProbe = Invoke-F2HttpProbe -Client $client -Method 'POST' -Url "$baseUrl$([string]$logout.path)" -Token $tokens[[string]$logout.role]
            if ([int]$logoutProbe.Status -eq [int]$logout.expectedStatus) {
                $summary.sessionCleanup.revoked = [int]$summary.sessionCleanup.revoked + 1
            }
            else {
                $summary.sessionCleanup.failed = [int]$summary.sessionCleanup.failed + 1
                $summary.loginNotes += "logging out the session of '$([string]$logout.role)' answered status $($logoutProbe.Status), expected $([int]$logout.expectedStatus)"
            }
        }
        catch {
            $summary.sessionCleanup.failed = [int]$summary.sessionCleanup.failed + 1
            $summary.loginNotes += "logging out the session of '$([string]$logout.role)' failed: $(Get-TrimmedRedactedLine -Text ([string]$_.Exception.Message) -MaxLength 200)"
        }
    }

    # Observe the effect read-only instead of trusting the logout status: the
    # active-session counts must come back to the pre-run snapshot for the
    # identities this run logged in as.
    $summary.sessionCleanup.activeSessionsBeforeRun = [ordered]@{
        fixtureOwner     = [int]$state.activeSessionsFixtureOwner
        fixtureAdmin     = [int]$state.activeSessionsFixtureAdmin
        familyOwner      = [int]$state.activeSessionsFamilyOwner
        medicalCaregiver = [int]$state.activeSessionsMedicalCaregiver
        contentAdmin     = [int]$state.activeSessionsContentAdmin
    }
    $afterLogout = Invoke-F2PsqlQuery -PsqlPath $psqlPath -Connection $connection -Sql $stateQuery -Tag 'state-after-logout'
    if (-not $afterLogout.Ok) {
        $summary.sessionCleanup.failed = [int]$summary.sessionCleanup.failed + 1
        $summary.loginNotes += "the read-only active-session read-back after the logouts failed: $($afterLogout.ErrorSummary)"
    }
    else {
        $summary.sessionCleanup.activeSessionsAfterLogout = [ordered]@{
            fixtureOwner     = [int]$afterLogout.Value.activeSessionsFixtureOwner
            fixtureAdmin     = [int]$afterLogout.Value.activeSessionsFixtureAdmin
            familyOwner      = [int]$afterLogout.Value.activeSessionsFamilyOwner
            medicalCaregiver = [int]$afterLogout.Value.activeSessionsMedicalCaregiver
            contentAdmin     = [int]$afterLogout.Value.activeSessionsContentAdmin
        }
        $notRestored = @()
        foreach ($identity in @('fixtureOwner', 'fixtureAdmin', 'familyOwner', 'medicalCaregiver', 'contentAdmin')) {
            if ([int]$summary.sessionCleanup.activeSessionsAfterLogout[$identity] -gt [int]$summary.sessionCleanup.activeSessionsBeforeRun[$identity]) {
                $notRestored += $identity
            }
        }
        $cleanupProblems = @()
        if ([int]$summary.sessionCleanup.failed -gt 0) {
            $cleanupProblems += "$($summary.sessionCleanup.failed) of the $(@($logoutPlan).Count) session logout(s) this run requested did not answer 204"
        }
        if ($notRestored.Count -gt 0) {
            $cleanupProblems += "the active-session count is still above the pre-run value for: $($notRestored -join ', ')"
        }
        if ($cleanupProblems.Count -gt 0) {
            $checkResults += New-F2FailedCheck -Name 'session-cleanup' -Actor 'every identity this run logged in as' `
                -Expected 'the sessions this run created are revoked again, so the active-session counts return to their pre-run values' `
                -Problem ($cleanupProblems -join '; ')
        }
        else {
            $checkResults += [ordered]@{
                name             = 'session-cleanup'
                method           = 'POST'
                path             = '/api/v1/auth/sessions/logout'
                actor            = 'every identity this run logged in as'
                expected         = "the $(@($logoutPlan).Count) session(s) this run created are revoked and no identity holds more active sessions than before the run"
                actual           = "revoked=$($summary.sessionCleanup.revoked) of $(@($logoutPlan).Count); pre-run and post-logout active-session counts match for every fixture identity"
                passed           = $true
                problems         = @()
                traceId          = ''
                traceCorrelation = 'not-applicable'
                localLocation    = $null
                responseHeaders  = [ordered]@{ traceparent = ''; correlationId = ''; requestId = '' }
            }
        }
    }
}
finally {
    if ($null -ne $client) { $client.Dispose() }
    if ($apiStarted -and $null -ne $apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force -ErrorAction SilentlyContinue
    }
}

# 6. Prove which persisted conversation scopes the successful checks covered,
#    and that the booking-scope proof rests on a row whose stored pair is
#    domain-consistent with the booking it references (never on the SQL-only
#    legacy row).
$domainBookingKeys = @($chatLinkage |
        Where-Object { [int]$_.scope -eq 1 -and [bool]$_.domainConsistent } |
        ForEach-Object { [string]$_.key })
$summary.chatCoverage.bookingScopeProvenByDomainRow = $false
foreach ($role in $chatRoles) {
    $checkName = "chat-conversations-$role"
    $roleResult = @($checkResults | Where-Object { $_.name -eq $checkName })
    $rolePassed = ($roleResult.Count -gt 0 -and [bool]$roleResult[0].passed)
    $roleExpectations = @(@($expectationsByRole[$role]) | Where-Object { $null -ne $_ })
    $scopes = @($roleExpectations | ForEach-Object { [int]$_.scope })
    $keys = @($roleExpectations | ForEach-Object { [string]$_.key })
    $domainKeys = @($keys | Where-Object { $domainBookingKeys -contains $_ })
    if ($rolePassed -and ($scopes -contains 0)) { $summary.chatCoverage.directScopeProven = $true }
    if ($rolePassed -and ($scopes -contains 1)) { $summary.chatCoverage.bookingScopeProven = $true }
    if ($rolePassed -and $domainKeys.Count -gt 0) { $summary.chatCoverage.bookingScopeProvenByDomainRow = $true }
    $summary.chatCoverage.requesters += [ordered]@{
        role                     = $role
        conversationKeys         = $keys
        persistedScopes          = $scopes
        domainConsistentKeys     = $domainKeys
        checkPassed              = $rolePassed
    }
}

if (-not ($summary.chatCoverage.directScopeProven -and $summary.chatCoverage.bookingScopeProven -and $summary.chatCoverage.bookingScopeProvenByDomainRow)) {
    $checkResults += New-F2FailedCheck -Name 'chat-scope-coverage' -Actor 'chat-requesters' `
        -Expected 'a visible direct conversation and a visible booking-scoped conversation whose stored participant pair is domain-consistent with the booking it references, across every requester that could log in' `
        -Problem 'the chat visibility proofs did not cover both persisted conversation scopes on a domain-consistent booking row'
}

$summary.checks = @($checkResults)
$summary.results = [ordered]@{
    total  = @($checkResults).Count
    passed = @($checkResults | Where-Object { [bool]$_.passed }).Count
    failed = @($checkResults | Where-Object { -not [bool]$_.passed }).Count
}

$correlated = @($checkResults | Where-Object { [string]$_.traceCorrelation -eq 'matched-local-log' }).Count
$noTraceId = @($checkResults | Where-Object { [string]$_.traceCorrelation -eq 'no-trace-id' }).Count
$unavailable = @($checkResults | Where-Object { [string]$_.traceCorrelation -eq 'unavailable' }).Count
$summary.notes += "trace correlation: $correlated request(s) matched a captured local log line, $noTraceId carried no trace id, $unavailable carried a trace id that was not found in the captured logs"

$exitCode = 0
if ($summary.results.failed -gt 0) { $exitCode = 1 }
Write-F2Result -Value $summary -ExitCode $exitCode
