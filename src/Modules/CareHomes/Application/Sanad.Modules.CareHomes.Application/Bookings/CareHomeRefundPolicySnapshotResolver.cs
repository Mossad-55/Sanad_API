using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Application.Bookings;

/// <summary>
/// Resolves the refund-policy facts a care-home booking snapshots at creation
/// time: the revision it is sold under and, when the facility's approved
/// profile revision carries structured cancellation terms, the exact terms
/// displayed at that moment with their provenance.
/// <para>
/// The booking itself never reads "whatever the facility displays today" —
/// decision #10 (facility changes apply only to future bookings) and clause 6
/// ("Preserve booked terms") are enforced here, at the single resolution point
/// every booking purchase flows through.
/// </para>
/// </summary>
public static class CareHomeRefundPolicySnapshotResolver
{
    /// <summary>
    /// Whether the Owner-approved care-home refund policy is live for NEW
    /// bookings. It ships dormant (Owner decision #7: bookings created after
    /// <i>actual activation</i> use the new policy; everything made before it
    /// keeps its original rules), so flipping this to <c>true</c> is a
    /// deliberate, separately reviewed change — the same convention the
    /// Families care-booking lane uses.
    /// </summary>
    public const bool ScheduledStartPolicyActivated = false;

    /// <summary>
    /// Resolves the policy snapshot for a booking created at
    /// <paramref name="utcNow"/>. The facility's approved profile revision is
    /// the displayed page; its structured cancellation terms — when published —
    /// become the booking's snapshot. Legacy snapshots never carry terms.
    /// </summary>
    public static CareHomePolicySnapshot Resolve(CareHomeFacility facility, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(facility);
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The booking creation timestamp must be UTC.", nameof(utcNow));

        CareHomeRefundPolicyVersion version = CareHomeRefundPolicyVersion.ForNewBooking(ScheduledStartPolicyActivated);
        CareHomeProfileRevision? displayed = facility.Revisions.SingleOrDefault(r => r.Id == facility.ApprovedRevisionId);

        if (version.IsLegacy)
        {
            // Legacy: the historical rule governs and no terms snapshot exists
            // to preserve (terms do not drive legacy money).
            return CareHomePolicySnapshot.From(version, displayed?.Id, null);
        }

        string? termsJson = CareHomeCancellationTerms.Parse(displayed?.CancellationTermsJson)?.ToJson();
        return CareHomePolicySnapshot.From(version, displayed?.Id, termsJson);
    }
}
