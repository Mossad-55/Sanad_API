namespace Sanad.Modules.Families.Domain.Bookings;

/// <summary>
/// What the cancellation policy says is owed for a booking. This is policy eligibility only:
/// it is not a provider call, not a settlement result and it carries no amount, so nothing here
/// can spend or derive a payout figure.
/// <see cref="FullCapturedRefund"/> covers the whole captured amount, platform fee included.
/// </summary>
public enum BookingRefundEntitlement
{
    NoRefundDue = 1,
    FullCapturedRefund = 2
}
