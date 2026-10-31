namespace Ahlena.Modules.Families.Domain.Bookings;

/// <summary>
/// Route a booking's cancellation to the refund policy revision it was sold under (Owner decision
/// #7: "no automatic retrospective change — preserve existing bookings' previous rules").
/// <para>
/// The version is read from the booking's own <see cref="Booking.RefundPolicyVersion"/>, never from
/// "what is current right now". That is the whole point of snapshotting it at purchase: a booking
/// created before the new policy was activated keeps dispatching to
/// <see cref="BookingCancellationPolicy"/> (revision 1, acceptance-relative 60-minute rule) even
/// after the new policy is live, and a reschedule keeps the original revision's rules.
/// </para>
/// <para>
/// Revision 2 bookings go to <see cref="BookingRefundPolicy"/>, which returns the richer
/// <see cref="BookingRefundDecision"/> carrying the award. Revision 1 bookings keep the exact
/// revision-1 behavior and its amount-free <see cref="BookingCancellationDecision"/>.
/// </para>
/// </summary>
public static class BookingRefundPolicyDispatch
{
    /// <summary>True when the booking must be decided by the legacy acceptance-relative policy.</summary>
    public static bool UsesLegacyPolicy(Booking booking)
    {
        ArgumentNullException.ThrowIfNull(booking);
        return booking.RefundPolicyVersion == BookingRefundPolicyVersion.LegacyVersion;
    }

    /// <summary>True when the booking is governed by the approved scheduled-start policy (revision 2).</summary>
    public static bool UsesScheduledStartPolicy(Booking booking)
    {
        ArgumentNullException.ThrowIfNull(booking);
        return booking.RefundPolicyVersion == BookingRefundPolicyVersion.ScheduledStartVersion;
    }

    /// <summary>
    /// The policy revision that governs an existing booking, rehydrated from its snapshot. Fails
    /// loudly on an unknown revision instead of defaulting a booking to either policy.
    /// </summary>
    public static BookingRefundPolicyVersion PolicyVersionOf(Booking booking)
    {
        ArgumentNullException.ThrowIfNull(booking);

        return BookingRefundPolicyVersion.FromPersisted(
            booking.RefundPolicyVersion,
            booking.RefundPolicyDocumentEffectiveOn);
    }

    /// <summary>
    /// Decides an ordinary (non-provider-outcome) cancellation of a booking, dispatching on the
    /// booking's sold revision. The amount-bearing revision-2 decision is returned as null for a
    /// revision-1 booking, whose amount-free decision is returned instead, so a caller must handle
    /// both shapes explicitly rather than silently treating the new decision as always present.
    /// </summary>
    public static (BookingRefundDecision? ScheduledStartDecision, BookingCancellationDecision? LegacyDecision) Decide(
        Booking booking,
        BookingCancellationPolicyInput legacyInput,
        BookingRefundPolicyInput scheduledStartInput)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(legacyInput);
        ArgumentNullException.ThrowIfNull(scheduledStartInput);

        return UsesLegacyPolicy(booking)
            ? (null, BookingCancellationPolicy.Decide(legacyInput))
            : (BookingRefundPolicy.Decide(scheduledStartInput), null);
    }
}
