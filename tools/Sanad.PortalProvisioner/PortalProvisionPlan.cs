using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sanad.PortalProvisioner;

// ---------------------------------------------------------------------------
// Deterministic identity derivation
// ---------------------------------------------------------------------------

/// <summary>
/// Pure, DB-free derivation of every stable identity the provisioner can
/// create: emails, phones, ownership markers, narrative-field markers. The
/// FNV-1a hash is seed-stable (no runtime hash randomization), so the same
/// (tag, role) always derives the same identity — this is what makes a rerun
/// an upsert rather than a duplicate. Shared by plan mode and the focused
/// tests.
/// </summary>
public static class PortalProvisionIdentity
{
    public static string SanitizeTag(string value)
    {
        var buffer = new System.Text.StringBuilder();
        foreach (char character in value ?? string.Empty)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer.Append(char.ToLowerInvariant(character));
            }
        }

        string tag = buffer.Length == 0 ? "pf" : buffer.ToString();
        return tag.Length > 24 ? tag[..24] : tag;
    }

    public static string BaseMarker(string runTag) => $"spf:{SanitizeTag(runTag)}";

    public static string GroupMarker(string runTag, string group) =>
        $"{BaseMarker(runTag)}:{group}";

    public static string OwnershipMarker(string runTag, string role) =>
        $"[{SanitizeTag(runTag)}:{role}]";

    public static string FixtureEmail(string runTag, string role) =>
        $"{SanitizeTag(runTag)}.{role}@test.sanad.local";

    /// <summary>
    /// Deterministic per-(tag, role) E.164 phone — mirrors the proven
    /// PortalFixtureHost derivation (FNV-1a on tag+role, offset away from the
    /// production-test-seed range, per-role ordinal).
    /// </summary>
    public static string DeterministicPhone(string runTag, string role)
    {
        string tag = SanitizeTag(runTag);
        ulong hash = StableHash($"{tag}:{role}");
        long baseValue = 1_000_000 + (long)(hash % 9_000_000UL);
        int ordinal = RoleOrdinal(role);
        long digits = (baseValue * 10) + ordinal;
        return "+2010" + digits.ToString("D8", CultureInfo.InvariantCulture);
    }

    public static int RoleOrdinal(string role) => role switch
    {
        "family.owner" => 1,
        "caregiver" => 2,
        "carehome.owner" => 3,
        "support" => 4,
        "superadmin" => 5,
        "elderly" => 6,
        "shutdown.owner" => 7,
        "carehome.owner2" => 9,
        "seam.claim.approved" => 10,
        "seam.claim.ambiguous" => 11,
        _ => 8
    };

    /// <summary>
    /// The numeric provider transaction id the care-home seam Failed group's
    /// booking persists (derived exactly like the shared booking helper:
    /// FNV-1a of the group marker masked positive), i.e. the exact refund
    /// call id the FixtureRefundSeam must allowlist as 'Rejected'.
    /// </summary>
    public static string SeamCareHomeFailedTransactionId(string runTag)
    {
        string tag = SanitizeTag(runTag);
        return StableNumber(GroupMarker(tag, PortalProvisionGroups.CareHomeRefundFailed))
            .ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The caregiver seam Failed group's deterministic refund-call id.</summary>
    public static string SeamCaregiverFailedTransactionId(string runTag) =>
        $"spf-{SanitizeTag(runTag)}-seam-caregiver-failed-txn";

    /// <summary>The approved (awaiting-execution) seam claim's charge transaction id.</summary>
    public static string SeamClaimApprovedTransactionId(string runTag) =>
        $"spf-{SanitizeTag(runTag)}-seam-claim-approved-txn";

    /// <summary>The ambiguous seam claim's charge transaction id.</summary>
    public static string SeamClaimAmbiguousTransactionId(string runTag) =>
        $"spf-{SanitizeTag(runTag)}-seam-claim-ambiguous-txn";

    /// <summary>The approved seam claim's idempotency key (its invariant exemption key).</summary>
    public static string SeamClaimApprovedIdempotencyKey(string runTag) =>
        $"spf-{SanitizeTag(runTag)}-seam-duplicate-claim-approved";

    /// <summary>The ambiguous seam claim's idempotency key (its invariant exemption key).</summary>
    public static string SeamClaimAmbiguousIdempotencyKey(string runTag) =>
        $"spf-{SanitizeTag(runTag)}-seam-duplicate-claim-ambiguous";

    /// <summary>Positive mask of the FNV-1a hash — the shared numeric-id derivation.</summary>
    public static long StableNumber(string value) =>
        (long)(StableHash(value) & 0x7FFFFFFFFFFFFFFFUL);

    /// <summary>FNV-1a 64-bit: stable across runs and machines.</summary>
    public static ulong StableHash(string value)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            foreach (char character in value)
            {
                hash ^= character;
                hash *= 1099511628211UL;
            }

            return hash;
        }
    }
}

// ---------------------------------------------------------------------------
// Plan records
// ---------------------------------------------------------------------------

/// <summary>One login identity the run will ensure (upsert by natural key).</summary>
public sealed record PortalProvisionPlannedLogin(
    string Role,
    string Email,
    string Phone,
    string OwnershipMarker);

/// <summary>One group's planned writes with its deterministic natural keys.</summary>
public sealed record PortalProvisionPlannedGroup(
    string Group,
    string Marker,
    IReadOnlyList<PortalProvisionPlannedRecord> PlannedRecords,
    string Detail);

/// <summary>One planned fixture-owned record and the key a rerun matches on.</summary>
public sealed record PortalProvisionPlannedRecord(
    string Kind,
    string IdempotencyBasis,
    string Detail);

/// <summary>The complete plan: everything a run would write, and nothing else.</summary>
public sealed record PortalProvisionPlanReport(
    string Mode,
    string RunTag,
    DateTime InstantUtc,
    string RevisionCorrelation,
    string TargetKind,
    string MarkerPrefix,
    bool SeamDeployed,
    IReadOnlyList<PortalProvisionPlannedLogin> PlannedLogins,
    IReadOnlyList<PortalProvisionPlannedGroup> PlannedGroups,
    IReadOnlyList<string> ApprovedDataOnlyGroups,
    IReadOnlyList<string> SeamGatedGroupsNotProvisioned);

public static class PortalProvisionPlan
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Build the complete write manifest WITHOUT any database connection: plan
    /// mode is pure derivation. The same inputs always produce byte-identical
    /// JSON (asserted by a focused test). Seam groups are planned ONLY when
    /// the operator passes <paramref name="seamDeployed"/>; the plan prints
    /// their allowlisted transaction ids and scripted outcomes so the deployed
    /// revision's <c>FixtureRefundSeam</c> configuration can be copied from it.
    /// </summary>
    public static PortalProvisionPlanReport Build(
        string runTag,
        DateTime instantUtc,
        string revision,
        string targetKind,
        IReadOnlyList<string> groups,
        bool seamDeployed = false)
    {
        string tag = PortalProvisionIdentity.SanitizeTag(runTag);
        DateTime utc = instantUtc.Kind == DateTimeKind.Utc
            ? instantUtc
            : DateTime.SpecifyKind(instantUtc, DateTimeKind.Utc);

        // Fail-closed classification: data-only groups always; seam groups
        // only behind the explicit --seam-deployed declaration.
        List<string> refusedGroups = [];
        foreach (string id in groups)
        {
            if (PortalProvisionGroups.DataOnlyGroups.Contains(id))
            {
                continue;
            }

            if (PortalProvisionGroups.SeamGroups.Contains(id))
            {
                if (!seamDeployed)
                {
                    refusedGroups.Add($"{id} (requires --seam-deployed)");
                }

                continue;
            }

            refusedGroups.Add(id);
        }

        if (refusedGroups.Count > 0)
        {
            throw new InvalidOperationException(
                "plan refused: requested groups outside the plannable set: "
                    + string.Join(", ", refusedGroups));
        }

        var logins = new List<PortalProvisionPlannedLogin>();
        foreach (string role in (string[])[
                    "family.owner", "caregiver", "carehome.owner", "support", "superadmin", "shutdown.owner"])
        {
            logins.Add(new PortalProvisionPlannedLogin(
                role,
                PortalProvisionIdentity.FixtureEmail(tag, role),
                PortalProvisionIdentity.DeterministicPhone(tag, role),
                PortalProvisionIdentity.OwnershipMarker(tag, role)));
        }

        logins.Add(new PortalProvisionPlannedLogin(
            "elderly (phone-keyed)",
            "<none: phone-keyed identity>",
            PortalProvisionIdentity.DeterministicPhone(tag, "elderly"),
            PortalProvisionIdentity.OwnershipMarker(tag, "elderly")));

        string marker(string group) => PortalProvisionIdentity.GroupMarker(tag, group);

        var planned = new List<PortalProvisionPlannedGroup>
        {
            new(
                PortalProvisionGroups.CareHomeFacilityDraft,
                marker(PortalProvisionGroups.CareHomeFacilityDraft),
                [new("CareHomeFacility", "ownerUserId = draft-owner login's user id", "Draft status, never submitted")],
                "separate owner's care-home facility left in Draft"),
            new(
                PortalProvisionGroups.CareHomePaidAwaitingDecision,
                $"{marker(PortalProvisionGroups.CareHomePaidAwaitingDecision)}:accept|:reject|:rejected (CareNeedsNotes)",
                [
                    new("CareHomeBookingAcceptSlot", "CareNeedsNotes = accept marker", "PaidAwaitingDecision/Paid, awaiting decision"),
                    new("CareHomeBookingRejectSlot", "CareNeedsNotes = reject marker", "PaidAwaitingDecision/Paid, awaiting decision"),
                    new("CareHomeBookingRejectedExample", "CareNeedsNotes = rejected marker", "RefundPending with refund Pending carrying a live claim (inert)")
                ],
                "two paid decision slots plus a persisted rejected example"),
            new(
                PortalProvisionGroups.CareHomeRefundAmbiguity,
                marker(PortalProvisionGroups.CareHomeRefundAmbiguity),
                [new("CareHomeBooking", "CareNeedsNotes = group marker", "Refund claim held in flight; Ambiguous outcome recorded; NOT retryable")],
                "care-home refund reconcile flow (Pending with claim)"),
            new(
                PortalProvisionGroups.CareHomeEvidencedExternalRefund,
                marker(PortalProvisionGroups.CareHomeEvidencedExternalRefund),
                [new("CareHomeBooking", "CareNeedsNotes = group marker", "Refund Initiated then Completed from recorded external evidence; terminal")],
                "externally recorded completed refund"),
            new(
                PortalProvisionGroups.CareHomeBookingExtension,
                marker(PortalProvisionGroups.CareHomeBookingExtension),
                [
                    new("CareHomeBookingCurrent", "CareNeedsNotes = group marker", "Accepted paid segment"),
                    new("CareHomeBookingExtension", "ExtensionOfBookingId = current booking id", "Extension segment")
                ],
                "booking extension off an accepted segment"),
            new(
                PortalProvisionGroups.CareHomeCheckoutDispute,
                marker(PortalProvisionGroups.CareHomeCheckoutDispute),
                [
                    new("CareHomeBooking", "CareNeedsNotes = group marker", "Checked in/out, NO family confirmation"),
                    new("CareHomeCheckInDispute", "BookingId = disputed booking id", "Open dispute")
                ],
                "checkout without family confirmation plus an open dispute"),
            new(
                PortalProvisionGroups.CaregiverConfirmedBooking,
                marker(PortalProvisionGroups.CaregiverConfirmedBooking),
                [new("FamiliesBooking", "SpecialInstructions = group marker", "Paid + Confirmed, awaiting visit")],
                "paid caregiver booking parked in Confirmed"),
            new(
                PortalProvisionGroups.CaregiverCompletedBooking,
                marker(PortalProvisionGroups.CaregiverCompletedBooking),
                [
                    new("FamiliesBooking", "SpecialInstructions = group marker", "Started -> Completed chronology"),
                    new("CaregiverPayout", "TransferReference = payout marker", "Recorded synthetic payout (no real transfer)")
                ],
                "paid completed caregiver booking with payout record"),
            new(
                PortalProvisionGroups.CaregiverRefundAmbiguity,
                marker(PortalProvisionGroups.CaregiverRefundAmbiguity),
                [new("FamiliesBooking", "SpecialInstructions = group marker", "Refund attempt CLAIMED and left in flight; Pending state; retry refused pre-gateway")],
                "caregiver refund reconcile flow (Pending with claim)"),
            new(
                PortalProvisionGroups.SubscriptionAndDuplicateClaim,
                $"{marker(PortalProvisionGroups.SubscriptionAndDuplicateClaim)} (+ derived case ids)",
                [
                    new("FamilySubscription", "familyId = family owner; current plan slot", "Premium current paid period"),
                    new("SubscriptionPaymentAttempt", "subscriptionId + succeeded", "Succeeded capture (Paymob order/txn ids are fixture markers, never provider calls)"),
                    new("DuplicateChargeClaim", "PaymentAttemptId + idempotency key", "Submitted; awaiting Support review; no budget claimed"),
                    new("ShutdownFamilySlot", "shutdown owner family", "Dedicated capture for the shutdown refund"),
                    new("SubscriptionRefundRequest", "IdempotencyKey = shutdown-refund marker", "Service-shutdown refund recorded (recorder path only)"),
                    new("RefundLedgerEntry", "RefundCaseId = derived case", "Ledger movement entry; budget claimed atomically")
                ],
                "dedicated paid subscription, submitted duplicate-charge claim, executed shutdown refund"),
            new(
                PortalProvisionGroups.FamiliesCareRecords,
                $"{marker(PortalProvisionGroups.FamiliesCareRecords)}:family|:caregiver|:carehome (CareRecord.Details)",
                [
                    new("CareRecord.Family", "Details = family-source marker", "Family-authored care record"),
                    new("CareRecord.Caregiver", "Details = caregiver-source marker", "Caregiver-authored care record"),
                    new("CareRecord.CareHome", "Details = care-home-source marker", "Care-home-authored care record")
                ],
                "one dependent, three locked authoring sources"),
            new(
                PortalProvisionGroups.FamilyPrivateAccess,
                marker(PortalProvisionGroups.FamilyPrivateAccess),
                [new("LoginIdentity", "read-only listing of the run's reviewer logins", "No writes; verifies current subscription present")],
                "reviewer login identities and private family test access"),
            new(
                PortalProvisionGroups.ComplaintReceived,
                marker(PortalProvisionGroups.ComplaintReceived),
                [new("ComplaintReview", "RefundCaseId = duplicate-claim case (unique per case)", "Received")],
                "complaint review in Received state"),
            new(
                PortalProvisionGroups.ComplaintInvestigating,
                marker(PortalProvisionGroups.ComplaintInvestigating),
                [new("ComplaintReview", "RefundCaseId = shutdown-refund case", "Investigating")],
                "complaint review in Investigating state")
        };

        planned.Add(new(
            PortalProvisionGroups.CareHomeRefundFailed,
            marker(PortalProvisionGroups.CareHomeRefundFailed),
            [
                new("CareHomeBooking", "CareNeedsNotes = group marker",
                    "Paid -> Rejected -> refund Failed (claim released, refund-send-armed); NEVER send-armed live: "
                        + $"refund retry intercepted by the seam. FixtureRefundSeam:RefundOutcomes['"
                        + $"{PortalProvisionIdentity.SeamCareHomeFailedTransactionId(tag)}']='Rejected'")
            ],
            "seam: care-home refund Failed, retry intercepted by the deployed FixtureRefundSeam"));
        planned.Add(new(
            PortalProvisionGroups.CaregiverRefundFailed,
            marker(PortalProvisionGroups.CaregiverRefundFailed),
            [
                new("FamiliesBooking", "SpecialInstructions = group marker",
                    "Paid + closed (CancelledByFamily) -> refund state Failed; NEVER send-armed live: admin retry "
                        + "intercepted by the seam. FixtureRefundSeam:RefundOutcomes['"
                        + $"{PortalProvisionIdentity.SeamCaregiverFailedTransactionId(tag)}']='Rejected'")
            ],
            "seam: caregiver refund Failed, retry intercepted by the deployed FixtureRefundSeam"));
        planned.Add(new(
            PortalProvisionGroups.DuplicateClaimApprovedAwaitingExecution,
            $"{marker(PortalProvisionGroups.DuplicateClaimApprovedAwaitingExecution)} (+ dedicated capture slot)",
            [
                new("FamilySubscription", "familyId = seam.claim.approved owner", "Dedicated capture slot"),
                new("SubscriptionPaymentAttempt", "subscriptionId + succeeded",
                    $"Succeeded charge txn '{PortalProvisionIdentity.SeamClaimApprovedTransactionId(tag)}'"),
                new("DuplicateChargeClaim",
                    $"IdempotencyKey = {PortalProvisionIdentity.SeamClaimApprovedIdempotencyKey(tag)}",
                    "Approved + AwaitingExecution, never executed; accidental Execute intercepted by the seam: "
                        + $"FixtureRefundSeam:RefundOutcomes['"
                        + $"{PortalProvisionIdentity.SeamClaimApprovedTransactionId(tag)}']='Rejected'")
            ],
            "seam: duplicate-charge claim Approved awaiting execution, Execute intercepted"));
        planned.Add(new(
            PortalProvisionGroups.DuplicateClaimAmbiguous,
            $"{marker(PortalProvisionGroups.DuplicateClaimAmbiguous)} (+ dedicated capture slot)",
            [
                new("FamilySubscription", "familyId = seam.claim.ambiguous owner", "Dedicated capture slot"),
                new("SubscriptionPaymentAttempt", "subscriptionId + succeeded",
                    $"Succeeded charge txn '{PortalProvisionIdentity.SeamClaimAmbiguousTransactionId(tag)}'"),
                new("DuplicateChargeClaim",
                    $"IdempotencyKey = {PortalProvisionIdentity.SeamClaimAmbiguousIdempotencyKey(tag)}",
                    "Executed IN-PROCESS via a scripted unconfirmable gateway stub -> State=Ambiguous, "
                        + "reconciliation-only; reconciliation calls intercepted by the seam: "
                        + $"FixtureRefundSeam:RefundOutcomes['"
                        + $"{PortalProvisionIdentity.SeamClaimAmbiguousTransactionId(tag)}']='Unknown'")
            ],
            "seam: duplicate-charge claim Ambiguous, reconciliation intercepted"));

        return new PortalProvisionPlanReport(
            Mode: "plan",
            RunTag: tag,
            InstantUtc: utc,
            RevisionCorrelation: revision,
            TargetKind: targetKind,
            MarkerPrefix: "spf",
            SeamDeployed: seamDeployed,
            PlannedLogins: logins,
            PlannedGroups: [.. planned.Where(item => groups.Count == 0
                ? PortalProvisionGroups.DataOnlyGroups.Contains(item.Group)
                : groups.Contains(item.Group))],
            ApprovedDataOnlyGroups: PortalProvisionGroups.DataOnlyGroups,
            SeamGatedGroupsNotProvisioned: PortalProvisionGroups.SeamGroups);
    }

    public static string Serialize(PortalProvisionPlanReport report) =>
        JsonSerializer.Serialize(report, JsonOptions);
}
