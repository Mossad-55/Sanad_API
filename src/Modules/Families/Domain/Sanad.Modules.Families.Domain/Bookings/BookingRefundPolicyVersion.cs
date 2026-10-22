using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Families.Domain.Bookings;

/// <summary>
/// The refund-policy revision a care booking is governed by, preserved at
/// booking time (Owner decision #7: "no automatic retrospective change —
/// preserve existing bookings' previous rules"; #8: reschedules retain the
/// original policy version, and separately purchased extensions snapshot the
/// terms applicable at purchase; clause 12: bookings predating a change remain
/// under the policy applicable when they were made).
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
/// <see cref="LegacyVersion"/> is the pre-existing behavior captured by
/// <see cref="BookingCancellationPolicy"/> (60 minutes measured from
/// acceptance): every booking created before the new policy was activated keeps
/// dispatching to it. There is deliberately no "unknown" value, because an
/// ungoverned booking is an integrity problem, not a policy choice.
/// </para>
/// </summary>
public sealed class BookingRefundPolicyVersion : ValueObject
{
    /// <summary>
    /// Revision 1: the original acceptance-relative 60-minute family grace
    /// window. Existing and historical bookings remain on this revision.
    /// </summary>
    public const int LegacyVersion = 1;

    /// <summary>
    /// Revision 2: the Owner-approved scheduled-start policy (24 hours / 50% /
    /// no refund at or after start) carried by <see cref="BookingRefundPolicy"/>.
    /// </summary>
    public const int ScheduledStartVersion = 2;

    /// <summary>The date the approved refund document took effect (2026-10-22).</summary>
    public static readonly DateOnly PolicyDocumentEffectiveOn = new(2026, 10, 22);

    private BookingRefundPolicyVersion(int version, DateOnly documentEffectiveOnUtc)
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

    /// <summary>True for the Owner-approved scheduled-start policy (revision 2).</summary>
    public bool IsScheduledStart => Version == ScheduledStartVersion;

    /// <summary>
    /// The revision to snapshot onto a newly created booking. The new
    /// scheduled-start policy applies to bookings created after activation
    /// (decision #7); bookings made before activation are written with
    /// <see cref="LegacyVersion"/> so no historical booking is silently
    /// repriced.
    /// </summary>
    public static BookingRefundPolicyVersion ForNewBooking(bool scheduledStartPolicyActivated) =>
        scheduledStartPolicyActivated
            ? new BookingRefundPolicyVersion(ScheduledStartVersion, PolicyDocumentEffectiveOn)
            : new BookingRefundPolicyVersion(LegacyVersion, default);

    /// <summary>
    /// Rehydrates a persisted revision. Rejects an unknown or empty revision
    /// (a booking must always name the policy it was sold under) instead of
    /// guessing a policy for it.
    /// </summary>
    public static BookingRefundPolicyVersion FromPersisted(int version, DateOnly documentEffectiveOnUtc)
    {
        if (version is not (LegacyVersion or ScheduledStartVersion))
        {
            throw new DomainException(
                $"Booking refund policy version '{version}' is not a known policy revision.");
        }

        if (version == ScheduledStartVersion && documentEffectiveOnUtc == default)
        {
            throw new DomainException(
                "A scheduled-start booking must record the approved policy document date it was sold under.");
        }

        return new BookingRefundPolicyVersion(version, documentEffectiveOnUtc);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Version;
        yield return DocumentEffectiveOnUtc;
    }

    public override string ToString() => $"policy v{Version}";
}
