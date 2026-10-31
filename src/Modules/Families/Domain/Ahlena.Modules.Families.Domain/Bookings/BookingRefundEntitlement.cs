namespace Ahlena.Modules.Families.Domain.Bookings;

/// <summary>
/// What the cancellation policy says is owed for a booking. This is policy eligibility only:
/// it is not a provider call, not a settlement result and it carries no amount, so nothing here
/// can spend or derive a payout figure.
/// <see cref="FullCapturedRefund"/> covers the whole captured amount, platform fee included.
/// <see cref="PartialRefund"/> covers a positive slice of it — the 50% clause-2 share or the
/// undelivered sessions of a package — so a partial award is never published as a full one.
/// The exact amount travels on <see cref="BookingRefundDecision"/> and on the recorded fact.
/// </summary>
public enum BookingRefundEntitlement
{
    NoRefundDue = 1,
    FullCapturedRefund = 2,
    PartialRefund = 3
}
