using System.Globalization;

namespace Ahlena.PortalProvisioner;

/// <summary>
/// The approved demo corpus (D1–D4): 22 inventory groups keyed under run tag
/// <c>demo1</c> and marker prefix <c>demo</c>.
/// <para>
/// This classification is deliberately SEPARATE from
/// <see cref="PortalProvisionGroups"/>: the S3a report-fixture groups keep
/// their own table, their own <c>spf</c> marker namespace and their existing
/// behaviour byte-for-byte. A demo run never expands the fixture tables and a
/// fixture run never sees a demo group.
/// </para>
/// <para>
/// Three groups are seam-gated because a row they must produce is
/// provider-call armed in the persisted state (a definitively Failed refund, or
/// an Approved / AwaitingExecution / Ambiguous duplicate-charge claim). They
/// are provisionable only behind the operator's <c>--seam-deployed</c>
/// declaration, exactly like the four S3a seam groups: the deployed revision's
/// <c>FixtureRefundSeam</c> allowlist intercepts the refund/execution call for
/// their allowlisted transaction ids.
/// </para>
/// </summary>
public static class DemoCorpusGroups
{
    // --- families / people -------------------------------------------------
    public const string IdentityAccounts = "identity-accounts";
    public const string FamiliesPeople = "families-people";
    public const string FamiliesSubscriptions = "families-subscriptions";

    // --- caregivers --------------------------------------------------------
    public const string CaregiversProfiles = "caregivers-profiles";
    public const string CaregiverRatings = "caregiver-ratings";

    // --- care homes --------------------------------------------------------
    public const string CareHomeFacilities = "care-home-facilities";
    public const string CareHomeInventory = "care-home-inventory";
    public const string CareHomeVisits = "care-home-visits";
    public const string CareHomeBookings = "care-home-bookings";

    // --- bookings / money --------------------------------------------------
    public const string CaregiverBookings = "caregiver-bookings";
    public const string FinancePlatform = "finance-platform";
    public const string FinanceClaims = "finance-claims";
    public const string FinanceComplaints = "finance-complaints";

    // --- engagement --------------------------------------------------------
    public const string Chat = "chat";
    public const string Community = "community";

    // --- cms ---------------------------------------------------------------
    public const string CmsLibrary = "cms-library";
    public const string CmsWellness = "cms-wellness";
    public const string CmsFaq = "cms-faq";
    public const string CmsLanding = "cms-landing";
    public const string CmsLegal = "cms-legal";
    public const string CmsSupport = "cms-support";

    // --- notifications -----------------------------------------------------
    public const string Notifications = "notifications";

    // --- elderly operations (opt-in; NOT part of the 22-group default) -----
    /// <summary>
    /// The opt-in elderly-operations group: the ONE existing demo elderly's own
    /// operational records — an open SOS, a pending help request, two answered
    /// daily check-ins and one completed medication course with two terminal
    /// dose logs. It is deliberately kept OUT of <see cref="All"/>,
    /// <see cref="ExecutionOrder"/> and <see cref="DataOnly"/>: the approved
    /// 22-group default, its plan, its counts and its tests stay unchanged, and
    /// these rows are written only when the operator names this group explicitly
    /// on <c>--groups</c>.
    /// </summary>
    public const string ElderlyOps = "elderly-ops";

    /// <summary>
    /// The 22 approved inventory groups in the order the brief lists them.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        IdentityAccounts,
        FamiliesPeople,
        FamiliesSubscriptions,
        CaregiversProfiles,
        CaregiverRatings,
        CareHomeFacilities,
        CareHomeInventory,
        CareHomeVisits,
        CareHomeBookings,
        CaregiverBookings,
        FinancePlatform,
        FinanceClaims,
        FinanceComplaints,
        Chat,
        Community,
        CmsLibrary,
        CmsWellness,
        CmsFaq,
        CmsLanding,
        CmsLegal,
        CmsSupport,
        Notifications
    ];

    /// <summary>
    /// Groups whose persisted end state is provider-call armed unless the
    /// deployed revision runs the <c>FixtureRefundSeam</c> allowlist for the
    /// exact transaction ids this plan prints. Provisionable only behind
    /// <c>--seam-deployed</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> SeamGated =
    [
        CareHomeBookings,
        CaregiverBookings,
        FinanceClaims
    ];

    /// <summary>Groups with no provider-armed row: provisionable without the seam.</summary>
    public static readonly IReadOnlyList<string> DataOnly =
        [.. All.Where(group => !SeamGated.Contains(group, StringComparer.Ordinal))];

    /// <summary>
    /// Demo groups provisionable ONLY when the operator names them explicitly.
    /// They are real, additive demo groups — <see cref="IsDemoGroup"/> accepts
    /// them, the parse-time CLI gate resolves them, the plan lists them in
    /// <c>PlannedGroups</c> (after the 22-group dependency order, in this
    /// declaration order) when they are requested, and the provisioner writes
    /// them — but they never join <see cref="All"/>, so the approved 22-group
    /// default, its counts lines and its plan are not expanded by them. They are
    /// absent from <see cref="ExecutionOrder"/> as well, which stays the 22-group
    /// dependency order.
    /// </summary>
    public static readonly IReadOnlyList<string> OptIn = [ElderlyOps];

    /// <summary>
    /// Dependency execution order. It intentionally differs from the inventory
    /// table order in three places:
    /// <list type="bullet">
    /// <item><c>caregiver-bookings</c> before <c>caregiver-ratings</c> — a rating
    /// needs a real completed booking id (its <c>BookingId</c> is unique);</item>
    /// <item><c>cms-faq</c> and <c>caregiver-ratings</c> before
    /// <c>cms-landing</c> — its contentJson refs resolve to real FAQ ids and
    /// caregiver rating ids;</item>
    /// <item><c>families-subscriptions</c> before <c>finance-platform</c>, whose
    /// invoices read back the seeded subscription and capture.</item>
    /// </list>
    /// </summary>
    public static readonly IReadOnlyList<string> ExecutionOrder =
    [
        IdentityAccounts,
        FamiliesPeople,
        FamiliesSubscriptions,
        CaregiversProfiles,
        CareHomeFacilities,
        CareHomeInventory,
        CaregiverBookings,
        CaregiverRatings,
        CareHomeBookings,
        CareHomeVisits,
        FinancePlatform,
        FinanceClaims,
        FinanceComplaints,
        Chat,
        Community,
        CmsLibrary,
        CmsWellness,
        CmsFaq,
        CmsLanding,
        CmsLegal,
        CmsSupport,
        Notifications
    ];

    /// <summary>
    /// The exact counts the brief approves per group, materialized so the plan,
    /// the manifest and the focused tests all read one table. <c>null</c> means
    /// "not a plain record count" (a composite or reuse group) and is described
    /// by <see cref="Description"/> instead.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ExpectedCounts =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [IdentityAccounts] = "16 accounts",
            [FamiliesPeople] = "2 families / 4 dependents / grants",
            [FamiliesSubscriptions] = "6",
            [CaregiversProfiles] = "6",
            [CaregiverRatings] = "8",
            [CareHomeFacilities] = "5",
            [CareHomeInventory] = "3 room types / 6 rooms / 12 beds",
            [CareHomeVisits] = "1 settings / 10 visits",
            [CareHomeBookings] = "14",
            [CaregiverBookings] = "14",
            [FinancePlatform] = "charge-rule reuse / 8 invoices",
            [FinanceClaims] = "4 claims / 2 shutdown refunds",
            [FinanceComplaints] = "4",
            [Chat] = "4 threads",
            [Community] = "6 posts / 12 comments",
            [CmsLibrary] = "6",
            [CmsWellness] = "6",
            [CmsFaq] = "8",
            [CmsLanding] = "6 sections (+ virtual stats)",
            [CmsLegal] = "2",
            [CmsSupport] = "1 contact / 3 messages",
            [Notifications] = "12 notifications (+ email outbox)",
            [ElderlyOps] = "9 rows: 1 SOS + 1 SOS history / 1 help request + 1 help-request history / "
                + "2 check-ins / 1 medication / 2 terminal dose logs"
        };

    /// <summary>One line per group, printed by plan mode.</summary>
    public static readonly IReadOnlyDictionary<string, string> Description =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [IdentityAccounts] = "one account per role plus verified / unverified / suspended variants",
            [FamiliesPeople] = "two families, four dependents and the medical access grants",
            [FamiliesSubscriptions] = "free, premium active, expired, cancelled and downgrade-pending subscriptions",
            [CaregiversProfiles] = "approved, pending review, rejected and suspended caregivers with specialties and availability",
            [CaregiverRatings] = "visible and hidden ratings with bilingual review text",
            [CareHomeFacilities] = "draft, pending review, corrections requested, approved and suspended facilities with documents and cover/logo media",
            [CareHomeInventory] = "shared, private and suite room types with rooms and beds, all carrying the demo media",
            [CareHomeVisits] = "visit settings with operating hours, visiting windows and closures, plus pending/approved/rejected/cancelled/rescheduled visits",
            [CareHomeBookings] = "every care-home booking status including refund Pending/Failed/Completed, an extension and a dispute",
            [CaregiverBookings] = "every caregiver booking status including confirmed, in progress, completed, cancelled, refunds, payout and extension",
            [FinancePlatform] = "platform charge rule readout, tax rule, invoices and transaction rows paired to the seeded bookings",
            [FinanceClaims] = "duplicate-charge claims Submitted / Approved-awaiting-execution / Ambiguous / Failed plus two service-shutdown refunds",
            [FinanceComplaints] = "Received, Investigating, Approved and Refused complaint reviews with award references",
            [Chat] = "four conversations (open, hidden, blocked, reported), 25-30 messages, two attachments and read marks",
            [Community] = "named and anonymous posts, deduplicated views, comments owned by the caller and by others, likes and one moderation removal with its deletion audit",
            [CmsLibrary] = "image, video and article library items with categories",
            [CmsWellness] = "wellness tips with free-text categories and the demo image",
            [CmsFaq] = "published, hidden and deactivated FAQs with bilingual answers",
            [CmsLanding] = "ordered landing sections with image refs, testimonial refs and FAQ refs (stats is virtual and never stored)",
            [CmsLegal] = "privacy policy and terms, published for the Public audience, carrying the demo marker in title and body",
            [CmsSupport] = "the support contact singleton plus three partner-contact messages",
            [Notifications] = "every notification type, read and unread, both languages, plus email outbox rows",
            [ElderlyOps] = "one existing demo elderly's opt-in operations: an open SOS, a pending help request, two "
                + "answered daily check-ins and one completed medication course whose two dose logs are terminal"
        };

    /// <summary>
    /// The groups that are never attached to the local <c>AhlenaDb</c> override:
    /// the S3a report-fixture groups. <c>--target-kind local-demo</c> refuses
    /// them so the override can only ever write the approved demo corpus.
    /// </summary>
    public static bool IsReportFixtureGroup(string group) =>
        PortalProvisionGroups.DataOnlyGroups.Contains(group, StringComparer.Ordinal)
        || PortalProvisionGroups.SeamGroups.Contains(group, StringComparer.Ordinal);

    /// <summary>
    /// True when the requested group id is one of the 22 approved demo groups or
    /// one of the explicitly-requested opt-in demo groups.
    /// </summary>
    public static bool IsDemoGroup(string group) =>
        All.Contains(group, StringComparer.Ordinal)
        || OptIn.Contains(group, StringComparer.Ordinal);
}

/// <summary>
/// Deterministic demo-corpus identities. Kept out of
/// <see cref="PortalProvisionIdentity"/> on purpose: the <c>spf</c> namespace
/// stays byte-identical, and every demo address/phone is derived from the
/// <c>demo</c> prefix so the two corpora can never collide on one target.
/// </summary>
public static class DemoCorpusIdentity
{
    public const string MarkerPrefix = "demo";

    public static string SanitizeTag(string value) => PortalProvisionIdentity.SanitizeTag(value);

    public static string BaseMarker(string runTag) => $"{MarkerPrefix}:{SanitizeTag(runTag)}";

    public static string GroupMarker(string runTag, string group) => $"{BaseMarker(runTag)}:{group}";

    public static string OwnershipMarker(string runTag, string role) => $"[{SanitizeTag(runTag)}:{role}]";

    /// <summary>
    /// Deterministic demo email. The address SHAPE is the same as an S3a
    /// fixture identity (<c>&lt;tag&gt;.&lt;role&gt;@test.sanad.local</c>): what
    /// keeps the two corpora apart is the pinned run tag (the fixture corpus is
    /// provisioned under <c>r2</c>, the demo corpus under <c>demo1</c>) together
    /// with the <c>demo</c> marker and phone namespace below, so no demo
    /// identity can collide with a fixture identity on one target.
    /// </summary>
    public static string Email(string runTag, string role) =>
        $"{SanitizeTag(runTag)}.{role}@test.sanad.local";

    /// <summary>
    /// Deterministic demo E.164 phone. The role ordinals start at 51 so a demo
    /// identity can never share a number with an S3a fixture role (1-11).
    /// </summary>
    public static string Phone(string runTag, string role)
    {
        string tag = SanitizeTag(runTag);
        ulong hash = PortalProvisionIdentity.StableHash($"{MarkerPrefix}:{tag}:{role}");
        long baseValue = 1_000_000 + (long)(hash % 9_000_000UL);
        int ordinal = RoleOrdinal(role);
        long digits = (baseValue * 10) + ordinal;
        return "+2010" + digits.ToString("D8", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Stable per-role demo ordinal. <c>01</c>-<c>16</c> are the 16 accounts;
    /// <c>17</c>+ are the extra dependents and the seam capture slots.
    /// </summary>
    public static int RoleOrdinal(string role) => role switch
    {
        "family.owner" => 51,
        "family.member" => 52,
        "family.owner2" => 53,
        "elderly.1" => 54,
        "elderly.2" => 55,
        "caregiver.1" => 56,
        "caregiver.2" => 57,
        "caregiver.3" => 58,
        "caregiver.4" => 59,
        "carehome.owner1" => 60,
        "carehome.owner2" => 61,
        "support" => 62,
        "superadmin" => 63,
        "content.admin" => 64,
        "finance.admin" => 65,
        "suspended.family" => 66,
        "dependent.3" => 67,
        "dependent.4" => 68,
        "seam.carehome.failed" => 69,
        "seam.caregiver.failed" => 70,
        "seam.claim.approved" => 71,
        "seam.claim.ambiguous" => 72,
        "shutdown.owner" => 73,
        "shutdown.owner2" => 74,
        "seam.claim.submitted" => 75,
        "seam.claim.failed" => 76,
        "caregiver.5" => 77,
        "caregiver.6" => 78,
        "carehome.owner3" => 79,
        "carehome.owner4" => 80,
        "carehome.owner5" => 81,
        _ => 82
    };

    /// <summary>A deterministic, marker-safe text key for a planned record.</summary>
    public static string RecordKey(string runTag, string group, string slot) =>
        $"{GroupMarker(runTag, group)}:{slot}";

    /// <summary>
    /// The exact numeric provider transaction id a demo <c>care-home-bookings</c>
    /// slot persists, so the plan prints the precise
    /// <c>FixtureRefundSeam</c> allowlist entry for a send-armed refund retry.
    /// </summary>
    public static string CareHomeRefundTransactionId(string runTag, string slot) =>
        PortalProvisionIdentity.StableNumber(RecordKey(runTag, DemoCorpusGroups.CareHomeBookings, slot))
            .ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A provider-facing id: the durable marker key with <c>:</c> replaced by
    /// <c>-</c>. .NET configuration splits keys on <c>:</c> and the
    /// <c>FixtureRefundSeam</c> allowlist IS configuration, so a provider id
    /// that must be allowlisted can never contain <c>:</c> — the binder silently
    /// DROPS such an entry, leaving a <c>--seam-deployed</c> declaration a lie.
    /// Only the id that leaves for a provider is slugged; the durable
    /// marker/<see cref="RecordKey"/> keeps its <c>:</c> (it is only ever a
    /// row-text key). This mirrors the S3a fixture id shape (for example
    /// <c>spf-r2-seam-claim-approved-txn</c>).
    /// </summary>
    private static string ProviderSlug(string recordKey) => recordKey.Replace(':', '-');

    /// <summary>The exact string order id a demo caregiver booking persists.</summary>
    public static string CaregiverOrderId(string runTag, string slot) =>
        $"{ProviderSlug(RecordKey(runTag, DemoCorpusGroups.CaregiverBookings, slot))}-order";

    /// <summary>
    /// The exact string provider transaction id a demo <c>caregiver-bookings</c>
    /// slot persists, so the plan prints the precise FixtureRefundSeam allowlist
    /// entry. Colon-free so it is configurable.
    /// </summary>
    public static string CaregiverRefundTransactionId(string runTag, string slot) =>
        $"{CaregiverOrderId(runTag, slot)}-txn";

    /// <summary>
    /// Offline check of the seam-armed caregiver <c>seam-failed-refund</c>
    /// slot, informed by the real refund-retry selection in
    /// <c>AdminBookingCommands</c>: a retry picks the booking's FIRST
    /// succeeded transaction with a stored <c>PaymobTransactionId</c> —
    /// payment SUCCESS is the retry's eligibility state, not refund
    /// terminality — and sends that id, falling back to the booking's own
    /// stored id when no such transaction exists at all. The seam intercepts
    /// a refund only when the id it is sent is EXACTLY the configured
    /// allowlist entry; anything else is passed through to the real gateway.
    /// This check is DELIBERATELY STRICTER than the handler: the handler could
    /// still act on the booking-fallback id when no succeeded transaction
    /// exists, while this check requires the full configured
    /// transaction/order/booking-fallback triple to match — for this one
    /// seeded provider-call-armed slot, an incomplete or mismatched id set is
    /// refused rather than reported as seam-covered.
    /// </summary>
    /// <returns>
    /// <c>null</c> when the persisted armed ids match the configured ids
    /// exactly (the slot is seam-protected); otherwise the mismatch
    /// explanation — which always states the slot is NOT seam-protected — for
    /// the run to refuse with, instead of silently reporting the slot as
    /// covered by an allowlist entry the data never sends.
    /// </returns>
    public static string? SeamArmedIdMismatch(
        string slot,
        string configuredOrderId,
        string configuredTransactionId,
        string? persistedOrderId,
        string? persistedTransactionId,
        string? persistedBookingTransactionId)
    {
        if (string.IsNullOrWhiteSpace(persistedTransactionId))
        {
            return $"no succeeded payment transaction with a stored PaymobTransactionId is persisted for slot "
                + $"'{slot}', so a refund retry can never match the configured allowlist entry "
                + $"'{configuredTransactionId}'; the armed slot is NOT seam-protected";
        }

        if (!string.Equals(persistedTransactionId, configuredTransactionId, StringComparison.Ordinal))
        {
            return $"the persisted succeeded transaction id '{persistedTransactionId}' does not equal the "
                + $"configured allowlist entry '{configuredTransactionId}'; a refund retry sends the persisted id, "
                + $"which the seam does not intercept, so the slot '{slot}' is NOT seam-protected";
        }

        if (!string.Equals(persistedOrderId, configuredOrderId, StringComparison.Ordinal))
        {
            return $"the persisted order id '{persistedOrderId}' does not equal the configured "
                + $"'{configuredOrderId}' for slot '{slot}'; the transaction id matches but the row's provider "
                + $"locators are inconsistent, so the slot is NOT seam-protected as provisioned";
        }

        if (!string.Equals(persistedBookingTransactionId, configuredTransactionId, StringComparison.Ordinal))
        {
            return $"the booking's own stored transaction id '{persistedBookingTransactionId}' (the retry's "
                + $"fallback locator) does not equal the configured allowlist entry '{configuredTransactionId}' "
                + $"for slot '{slot}'; the slot is NOT seam-protected";
        }

        return null;
    }

    /// <summary>The exact succeeded-capture transaction id a demo claim slot persists.</summary>
    public static string ClaimCaptureTransactionId(string runTag, string slot) =>
        $"{ProviderSlug(RecordKey(runTag, DemoCorpusGroups.FinanceClaims, slot))}-capture";

    /// <summary>The exact order id a demo claim capture slot persists.</summary>
    public static string ClaimCaptureOrderId(string runTag, string slot) =>
        $"{ClaimCaptureTransactionId(runTag, slot)}-order";

    /// <summary>
    /// Historical pre-fix capture id (the bare marker with <c>:</c>), retained
    /// as a documented derivation for evidence reads only — nothing in the
    /// tool references it any more. It describes the id shape a target would
    /// carry if seeded before the colon-free bug was fixed; both recorded
    /// targets were verified (read-only, 2026-10-26) to persist the
    /// colon-free <see cref="ClaimCaptureTransactionId"/> form, so no reuse
    /// check needs to accept the legacy form today. Do not introduce new
    /// colon-bearing provider ids: .NET configuration splits keys on
    /// <c>:</c>, so an id that must be seam-allowlisted can never contain one.
    /// </summary>
    public static string LegacyClaimCaptureTransactionId(string runTag, string slot) =>
        $"{RecordKey(runTag, DemoCorpusGroups.FinanceClaims, slot)}:capture";

    /// <summary>The deterministic idempotency key of a demo duplicate-charge claim.</summary>
    public static string ClaimIdempotencyKey(string runTag, string slot) =>
        $"{RecordKey(runTag, DemoCorpusGroups.FinanceClaims, slot)}:claim";

    /// <summary>
    /// The deterministic refund case a demo complaint owns. Derived only from the
    /// demo marker, so it can never collide with a capture-derived case
    /// (<c>SubscriptionRefundCaseIds</c>) and never with another complaint slot.
    /// </summary>
    public static Guid ComplaintRefundCaseId(string runTag, string slot)
    {
        string basis = $"{MarkerPrefix}:{SanitizeTag(runTag)}:{DemoCorpusGroups.FinanceComplaints}:{slot}";
        ulong high = PortalProvisionIdentity.StableHash($"{basis}:a");
        ulong low = PortalProvisionIdentity.StableHash($"{basis}:b");
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes[..8], high);
        BitConverter.TryWriteBytes(bytes[8..], low);
        return new Guid(bytes);
    }
}

/// <summary>
/// The fixed, replay-stable shape of the opt-in
/// <see cref="DemoCorpusGroups.ElderlyOps"/> group. The provisioner and the
/// focused facts both read THESE values, so the approved fixture is described
/// once instead of twice.
/// <para>
/// Every date is an absolute, already-past fixture date rather than an offset
/// from the run instant: a replay — even one carrying a later <c>--instant</c>
/// — therefore re-reads the same rows instead of materializing a second set of
/// check-ins or dose logs on shifted dates. The provisioner additionally
/// refuses (fail closed) when the run's declared instant precedes these dates,
/// so a fixture date can never be seeded into the future.
/// </para>
/// <para>
/// Nothing here is provider-armed and nothing here is live state: the SOS and
/// the help request are open/pending rows, the two check-ins are answers for
/// past days, and the medication's finite course ended in the past with the
/// medication left <c>Completed</c>, which is exactly what excludes it from the
/// medication-lateness read path.
/// </para>
/// </summary>
public static class ElderlyOpsFixture
{
    /// <summary>Idempotency slot of the one SOS row.</summary>
    public const string SosSlot = "sos";

    /// <summary>Idempotency slot of the one help request row.</summary>
    public const string HelpRequestSlot = "help-request";

    /// <summary>The marker-bearing name slot of the one medication row.</summary>
    public const string MedicationSlot = "medication";

    /// <summary>The SOS's mandatory <c>Created</c> history row (never expanded).</summary>
    public const int SosHistoryRows = 1;

    /// <summary>The help request's mandatory <c>Created</c> history row (never expanded).</summary>
    public const int HelpRequestHistoryRows = 1;

    /// <summary>First check-in: answered yes on <see cref="FirstCheckInLocalDate"/>.</summary>
    public static readonly DateOnly FirstCheckInLocalDate = new(2026, 10, 20);

    public static readonly TimeOnly FirstCheckInLocalTime = new(9, 30);
    public const bool FirstCheckInAnswer = true;

    /// <summary>Second check-in: a distinct local date, answered no.</summary>
    public static readonly DateOnly SecondCheckInLocalDate = new(2026, 10, 19);

    public static readonly TimeOnly SecondCheckInLocalTime = new(10, 0);
    public const bool SecondCheckInAnswer = false;

    /// <summary>Finite medication course, entirely in the past.</summary>
    public static readonly DateOnly MedicationStartDate = new(2026, 9, 6);
    public static readonly DateOnly MedicationEndDate = new(2026, 9, 26);

    /// <summary>The two terminal dose logs, inside the course on distinct days.</summary>
    public static readonly DateOnly TakenDoseDate = new(2026, 9, 11);
    public static readonly DateOnly SkippedDoseDate = new(2026, 9, 12);
    public static readonly TimeOnly MedicationDoseTime = new(8, 0);

    /// <summary>The medication's display values; the name additionally carries the group marker.</summary>
    public const string MedicationArabicLabel = "مسكن تجريبي";
    public const string MedicationDosage = "1 tablet";
    public const string MedicationDoseUnit = "tablet";
    public const int MedicationDoseQuantity = 1;

    /// <summary>
    /// The sentence-builder vocabulary the help request stores. It mirrors
    /// <c>TestUserDataSeeder.EnsureHelpRequestCatalogAsync</c> — the
    /// application's own catalog fixture (<c>self</c>/<c>need</c>/<c>water</c>/
    /// <c>now</c>) — and the labels are stored ON the help-request row itself
    /// (the read path projects the stored labels, it does not join the catalog),
    /// so the row stays readable whether or not the CMS catalog is seeded on the
    /// target.
    /// </summary>
    public const string HelpRequestActorKey = "self";
    public const string HelpRequestActorArabicLabel = "أنا";
    public const string HelpRequestActorEnglishLabel = "I";
    public const string HelpRequestActionKey = "need";
    public const string HelpRequestActionArabicLabel = "أحتاج";
    public const string HelpRequestActionEnglishLabel = "need";
    public const string HelpRequestNeedKey = "water";
    public const string HelpRequestNeedArabicLabel = "ماء";
    public const string HelpRequestNeedEnglishLabel = "water";
    public const string HelpRequestQualifierKey = "now";
    public const string HelpRequestQualifierArabicLabel = "الآن";
    public const string HelpRequestQualifierEnglishLabel = "now";

    /// <summary>
    /// The deterministic idempotency key of the one SOS row (unique per elderly
    /// under <c>ux</c>-style <c>(elderly_identity_user_id, idempotency_key)</c>).
    /// </summary>
    public static string SosKey(string runTag) =>
        DemoCorpusIdentity.RecordKey(runTag, DemoCorpusGroups.ElderlyOps, SosSlot);

    /// <summary>The deterministic idempotency key of the one help request row.</summary>
    public static string HelpRequestKey(string runTag) =>
        DemoCorpusIdentity.RecordKey(runTag, DemoCorpusGroups.ElderlyOps, HelpRequestSlot);

    /// <summary>
    /// The medication's deterministic, marker-bearing name. The
    /// <c>families.medications</c> table has no marker column, so this name IS
    /// the ownership key a re-run reads back before writing: it carries the run
    /// tag AND the group marker, and a name that does not match is refused
    /// rather than adopted.
    /// </summary>
    public static string MedicationName(string runTag) =>
        $"AHLENA DEMO {MedicationArabicLabel} [{DemoCorpusIdentity.GroupMarker(runTag, DemoCorpusGroups.ElderlyOps)}]";

    /// <summary>The medication's marker-bearing free-text instructions.</summary>
    public static string MedicationInstructions(string runTag) =>
        $"AHLENA DEMO demo-corpus medication instructions "
            + $"[{DemoCorpusIdentity.GroupMarker(runTag, DemoCorpusGroups.ElderlyOps)}]: one tablet after breakfast; "
            + "demo data only, not medical advice.";
}
