using System.Text.Json;

namespace Sanad.PortalProvisioner;

/// <summary>One planned demo login. Never carries a password.</summary>
public sealed record DemoCorpusPlannedLogin(
    string Role,
    string Email,
    string Phone,
    string Marker);

/// <summary>One planned demo group with the approved depth.</summary>
public sealed record DemoCorpusPlannedGroup(
    string Group,
    string Marker,
    string ExpectedCounts,
    string Description,
    bool SeamGated,
    bool Planned);

/// <summary>The complete demo-corpus plan, derived without a database connection.</summary>
public sealed record DemoCorpusPlanReport(
    string RunTag,
    DateTime InstantUtc,
    string RevisionCorrelation,
    string TargetKind,
    string MarkerPrefix,
    string StorageRoot,
    bool LocalSanadDbOverride,
    bool SeamDeployed,
    IReadOnlyList<string> ExecutionOrder,
    IReadOnlyList<DemoCorpusPlannedLogin> PlannedLogins,
    IReadOnlyList<DemoCorpusPlannedGroup> PlannedGroups,
    IReadOnlyList<string> SeamAllowlist,
    IReadOnlyList<string> RefusedGroups);

/// <summary>
/// Pure derivation of the demo-corpus write manifest. Like the S3a plan it
/// opens no connection: <c>plan</c> must be reviewable before any target is
/// touched. The media section prints the folder each field will be written to
/// and the writer that owns it, so the reviewer can see that every media
/// reference is produced by the real storage implementation.
/// </summary>
public static class DemoCorpusPlan
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static readonly IReadOnlyList<string> LoginRoles =
    [
        "family.owner", "family.member", "family.owner2", "elderly.1", "elderly.2",
        "caregiver.1", "caregiver.2", "caregiver.3", "caregiver.4",
        "carehome.owner1", "carehome.owner2", "support", "superadmin",
        "content.admin", "finance.admin", "suspended.family"
    ];

    public static DemoCorpusPlanReport Build(
        string runTag,
        DateTime instantUtc,
        string revision,
        string targetKind,
        string storageRoot,
        IReadOnlyList<string> groups,
        bool seamDeployed,
        bool localSanadDbOverride)
    {
        string tag = DemoCorpusIdentity.SanitizeTag(runTag);
        DateTime utc = instantUtc.Kind == DateTimeKind.Utc
            ? instantUtc
            : DateTime.SpecifyKind(instantUtc, DateTimeKind.Utc);

        List<string> refused = [];
        List<string> planned = [];

        foreach (string group in groups)
        {
            if (!DemoCorpusGroups.IsDemoGroup(group))
            {
                refused.Add($"{group} (not a demo-corpus group; the S3a fixture groups are never planned here)");
                continue;
            }

            if (DemoCorpusGroups.SeamGated.Contains(group, StringComparer.Ordinal) && !seamDeployed)
            {
                refused.Add(
                    $"{group} (seam-gated: its persisted end state is provider-call armed unless the deployed "
                        + "revision runs the FixtureRefundSeam allowlist for the exact ids this plan prints)");
                continue;
            }

            planned.Add(group);
        }

        if (localSanadDbOverride
            && groups.Any(DemoCorpusGroups.IsReportFixtureGroup))
        {
            refused.Add(
                "the local SanadDb override can only ever write the demo corpus; a report-fixture group was requested");
        }

        string markerPrefix = DemoCorpusIdentity.MarkerPrefix;

        var logins = LoginRoles
            .Select(role => new DemoCorpusPlannedLogin(
                role,
                role is "elderly.1" or "elderly.2"
                    ? "<none: phone-keyed elderly identity>"
                    : DemoCorpusIdentity.Email(tag, role),
                DemoCorpusIdentity.Phone(tag, role),
                DemoCorpusIdentity.OwnershipMarker(tag, role)))
            .ToList();

        var plannedGroups = DemoCorpusGroups.ExecutionOrder
            .Where(group => planned.Contains(group, StringComparer.Ordinal))
            .Select(group => new DemoCorpusPlannedGroup(
                group,
                DemoCorpusIdentity.GroupMarker(tag, group),
                DemoCorpusGroups.ExpectedCounts[group],
                DemoCorpusGroups.Description[group],
                DemoCorpusGroups.SeamGated.Contains(group, StringComparer.Ordinal),
                Planned: true))
            .ToList();

        return new DemoCorpusPlanReport(
            tag,
            utc,
            revision,
            targetKind,
            markerPrefix,
            storageRoot,
            localSanadDbOverride,
            seamDeployed,
            DemoCorpusGroups.ExecutionOrder,
            logins,
            plannedGroups,
            DescribeSeamAllowlist(tag, groups),
            refused);
    }

    /// <summary>
    /// The exact <c>FixtureRefundSeam:RefundOutcomes</c> entries the deployed
    /// revision needs for every requested seam-gated group, printed BEFORE any
    /// write so the operator can copy them verbatim. Derived from the requested
    /// set (not the accepted set) so a plan that refuses a seam group without
    /// <c>--seam-deployed</c> still tells the operator exactly what to allowlist
    /// before re-running with the declaration.
    /// </summary>
    public static IReadOnlyList<string> DescribeSeamAllowlist(string runTag, IReadOnlyList<string> groups)
    {
        string tag = DemoCorpusIdentity.SanitizeTag(runTag);
        var lines = new List<string>();

        void Entry(string group, string slot, string transactionId, string outcome) =>
            lines.Add(
                $"{group} [{slot}]: FixtureRefundSeam:RefundOutcomes['{transactionId}']='{outcome}'");

        foreach (string group in DemoCorpusGroups.ExecutionOrder)
        {
            if (!groups.Contains(group, StringComparer.Ordinal)
                || !DemoCorpusGroups.SeamGated.Contains(group, StringComparer.Ordinal))
            {
                continue;
            }

            switch (group)
            {
                case DemoCorpusGroups.CareHomeBookings:
                    // The definitively-failed refund (claim released) and the
                    // rejected refund that stays Pending with NO persisted claim
                    // are both refund-send-armed; a retry hits the seam.
                    Entry(group, "rejected-refund-pending",
                        DemoCorpusIdentity.CareHomeRefundTransactionId(tag, "rejected-refund-pending"), "Rejected");
                    Entry(group, "seam-failed-refund",
                        DemoCorpusIdentity.CareHomeRefundTransactionId(tag, "seam-failed-refund"), "Rejected");
                    break;

                case DemoCorpusGroups.CaregiverBookings:
                    Entry(group, "seam-failed-refund",
                        DemoCorpusIdentity.CaregiverRefundTransactionId(tag, "seam-failed-refund"), "Rejected");
                    break;

                case DemoCorpusGroups.FinanceClaims:
                    // Submitted and Approved hold no execution intent yet: an
                    // accidental Execute must be refused (Rejected). Ambiguous is
                    // reconciliation-only (Unknown). Failed already recorded the
                    // definitive refusal (Rejected).
                    Entry(group, "submitted",
                        DemoCorpusIdentity.ClaimCaptureTransactionId(tag, "submitted"), "Rejected");
                    Entry(group, "approved",
                        DemoCorpusIdentity.ClaimCaptureTransactionId(tag, "approved"), "Rejected");
                    Entry(group, "ambiguous",
                        DemoCorpusIdentity.ClaimCaptureTransactionId(tag, "ambiguous"), "Unknown");
                    Entry(group, "failed",
                        DemoCorpusIdentity.ClaimCaptureTransactionId(tag, "failed"), "Rejected");
                    break;
            }
        }

        if (lines.Count == 0)
        {
            lines.Add(
                "no seam-gated group requested: the deployed revision needs no FixtureRefundSeam allowlist entry");
        }

        return lines;
    }

    /// <summary>
    /// The media contract, printed by plan mode so the operator can see that
    /// every reference is produced by the real writer rather than invented.
    /// </summary>
    public static IReadOnlyList<string> DescribeMedia() =>
    [
        "source image: one generated JPEG with a visible 'SANAD DEMO' mark (no stock asset)",
        "source document: one generated PDF whose every line is demo labelled ('DEMO - not legal text')",
        "source video: one generated MP4, reused wherever a video key is expected",
        "public image fields -> IFileStorage.SaveAsync(folder as the real uploader uses) -> key '<folder>/<guidv7>.jpg' -> GET /files/<key>",
        "care-home profile/room media -> IFileStorage.SavePrivateAsync('care-home-media') -> key 'private/care-home-media/<guidv7>.jpg' (the domain only accepts private keys there)",
        "video fields -> IFileStorage.SavePrivateAsync -> key 'private/<folder>/<guidv7>.mp4' (the public writer rejects video/mp4 by design)",
        "a seeded reference whose file cannot be opened through the same abstraction fails the run",
        "no invented keys, no database blobs, no direct file writes, no new route"
    ];
}
