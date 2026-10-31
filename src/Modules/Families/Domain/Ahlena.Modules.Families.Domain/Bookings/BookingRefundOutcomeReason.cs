namespace Ahlena.Modules.Families.Domain.Bookings;

/// <summary>
/// Provider-side, access and force-majeure outcomes that can end a care booking.
/// <para>
/// These are the clause 4 / clause 5 reasons the approved policy names: a provider
/// cancellation, a no-show, lateness beyond 60 minutes without the customer's consent, an
/// inability to access the recipient at the appointed time, a safety cancellation, and a
/// force-majeure event. Accessibility outcomes are tracked separately from the provider ones
/// because the approved text reaches the same money result from opposite directions: the
/// customer could not access the recipient (clause 2, no refund), while a safety cancellation by
/// Ahlena refunds undelivered services (clause 4).
/// </para>
/// <para>
/// Every member is a concrete, recorded reason. Values start at 1 so that an unmapped or
/// client-fabricated value (including the numeric default 0) is rejected rather than being read
/// as a real outcome, and there is deliberately no "unknown" member: an outcome the system cannot
/// name must fail the request instead of silently choosing a refund path.
/// </para>
/// </summary>
public enum BookingRefundOutcomeReason
{
    /// <summary>Clause 4: the assigned provider cancelled. Full refund for the affected session.</summary>
    ProviderCancelled = 1,

    /// <summary>
    /// Clause 4: the provider did not appear. Full refund for the affected session, and the
    /// customer may choose a replacement at no extra cost.
    /// </summary>
    ProviderNoShow = 2,

    /// <summary>
    /// Clause 4: the provider arrived more than 60 minutes late without the customer's consent.
    /// Exactly 60 minutes is not late enough to trigger the clause, so the threshold is a strict
    /// "greater than" comparison against <c>BookingRefundPolicy.ProviderLatenessThreshold</c>.
    /// </summary>
    ProviderLateBeyondThreshold = 3,

    /// <summary>
    /// Clause 2: the recipient could not be accessed at the appointed time, so no refund is due.
    /// This is the one member that denies rather than refunds.
    /// </summary>
    RecipientInaccessible = 4,

    /// <summary>
    /// Clause 4: Ahlena cancelled the undelivered service on safety grounds (or the customer
    /// reported an immediate safety concern). Delivered services stay payable; undelivered ones
    /// are refunded.
    /// </summary>
    SafetyCancellation = 5,

    /// <summary>
    /// Clause 4, Owner amendment: force majeure refunds undelivered services and unused sessions.
    /// Delivered services remain payable, and any rescheduling or credit arrangement requires the
    /// customer's choice — it is never applied automatically.
    /// </summary>
    ForceMajeure = 6
}
