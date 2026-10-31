using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;

namespace Ahlena.Modules.CareHomes.Domain.Bookings;

/// <summary>
/// The refund-policy revision a care-home booking is governed by, preserved at
/// booking time (Owner decision #7: "no automatic retrospective change —
/// preserve existing bookings' previous rules"; #8: separately purchased
/// extensions snapshot the policy/terms applicable at purchase; clause 12:
/// bookings predating a change remain under the policy applicable when they
/// were made; clause 6: "Preserve booked terms").
/// <para>
/// The version is stored as a small explicit value object rather than inferred
/// from a date at read time: a booking's entitlement must never be recomputed
/// against whatever policy happens to be current when a later actor looks at
/// it. The paired <see cref="DocumentEffectiveOnUtc"/> records the approved
/// document date the revision came from (decision #7 asks for the document
/// date to be recorded separately) and is display/attribution only — it never
/// participates in money arithmetic.
/// </para>
/// <para>
/// <see cref="LegacyVersion"/> is the pre-existing care-home behavior (full
/// refund before check-in, half the total after it): every booking created
/// before the new policy was activated keeps deciding under it, byte-for-byte.
/// There is deliberately no "unknown" value, because an ungoverned booking is
/// an integrity problem, not a policy choice.
/// </para>
/// </summary>
public sealed class CareHomeRefundPolicyVersion : ValueObject
{
    /// <summary>
    /// Revision 1: the original care-home cancellation behavior (full refund
    /// before check-in, half the total after it). Existing and historical
    /// bookings remain on this revision.
    /// </summary>
    public const int LegacyVersion = 1;

    /// <summary>
    /// Revision 2: the Owner-approved care-home refund behavior — the
    /// facility's own structured cancellation terms as displayed at booking
    /// time (clause 6), falling back to the clause 2 scheduled-start schedule
    /// when the facility published no structured terms.
    /// </summary>
    public const int ScheduledStartVersion = 2;

    /// <summary>The date the approved refund document took effect (2026-10-22).</summary>
    public static readonly DateOnly PolicyDocumentEffectiveOn = new(2026, 10, 22);

    private CareHomeRefundPolicyVersion(int version, DateOnly documentEffectiveOnUtc)
    {
        Version = version;
        DocumentEffectiveOnUtc = documentEffectiveOnUtc;
    }

    /// <summary>Monotonic policy revision; the whole entitlement dispatch key.</summary>
    public int Version { get; }

    /// <summary>
    /// Approved document date for this revision. Attribution only: recorded
    /// beside the booking so a reviewer can see which approved text applied,
    /// and never read by money arithmetic.
    /// </summary>
    public DateOnly DocumentEffectiveOnUtc { get; }

    /// <summary>True for every booking created before the new policy was activated.</summary>
    public bool IsLegacy => Version == LegacyVersion;

    /// <summary>True for the Owner-approved care-home terms/default-schedule policy (revision 2).</summary>
    public bool IsScheduledStart => Version == ScheduledStartVersion;

    /// <summary>
    /// The revision to snapshot onto a newly created booking. The new
    /// care-home terms/default-schedule policy applies to bookings created
    /// after activation (decision #7); bookings made before activation are
    /// written with <see cref="LegacyVersion"/> so no historical booking is
    /// silently repriced.
    /// </summary>
    public static CareHomeRefundPolicyVersion ForNewBooking(bool scheduledStartPolicyActivated) =>
        scheduledStartPolicyActivated
            ? new CareHomeRefundPolicyVersion(ScheduledStartVersion, PolicyDocumentEffectiveOn)
            : new CareHomeRefundPolicyVersion(LegacyVersion, default);

    /// <summary>
    /// Rehydrates a persisted revision. Rejects an unknown or empty revision
    /// (a booking must always name the policy it was sold under) instead of
    /// guessing a policy for it.
    /// </summary>
    public static CareHomeRefundPolicyVersion FromPersisted(int version, DateOnly documentEffectiveOnUtc)
    {
        if (version is not (LegacyVersion or ScheduledStartVersion))
        {
            throw new DomainException(
                $"Care-home booking refund policy version '{version}' is not a known policy revision.");
        }

        if (version == ScheduledStartVersion && documentEffectiveOnUtc == default)
        {
            throw new DomainException(
                "A scheduled-start care-home booking must record the approved policy document date it was sold under.");
        }

        return new CareHomeRefundPolicyVersion(version, documentEffectiveOnUtc);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Version;
        yield return DocumentEffectiveOnUtc;
    }

    public override string ToString() => $"care-home policy v{Version}";
}
