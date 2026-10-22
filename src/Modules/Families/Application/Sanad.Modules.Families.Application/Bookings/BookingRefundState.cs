using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.Modules.Families.Application.Bookings;

/// <summary>
/// How a closed booking relates to Paymob refund.
/// Failed = paid, then cancelled/declined/expired, and the refund that is owed definitively did not
/// settle (no movement recorded, or only part of the recorded award); an unpaid skip is NotApplicable.
/// Pending = an attempt is in flight or its outcome is unconfirmed, so neither success nor failure is
/// known yet.
/// </summary>
public enum BookingRefundState
{
    NotApplicable = 1,
    Failed = 2,
    Succeeded = 3,
    /// <summary>Paid or unpaid, policy decided nothing is owed; never renders as Failed; never retryable.</summary>
    NoRefundDue = 4,
    /// <summary>
    /// A refund attempt is in flight, or ended in an outcome the gateway could not confirm. Nothing
    /// may be retried and the state is deliberately neither Succeeded nor Failed: the movement is
    /// unknown until an operator reconciles it.
    /// </summary>
    Pending = 5
}

public enum AdminBookingFinanceFilter
{
    All = 0,
    Cancelled = 1,
    FailedRefund = 2,
    Refunded = 3
}

public static class BookingRefundStates
{
    public static readonly BookingStatus[] ClosedWithoutRefundStatus =
    [
        BookingStatus.CancelledByFamily,
        BookingStatus.DeclinedByCaregiver,
        BookingStatus.CancelledByCaregiver,
        BookingStatus.Expired
    ];

    public static readonly BookingStatus[] AdminDefaultStatuses =
    [
        BookingStatus.CancelledByFamily,
        BookingStatus.DeclinedByCaregiver,
        BookingStatus.CancelledByCaregiver,
        BookingStatus.Refunded,
        BookingStatus.Expired
    ];

    public static readonly BookingStatus[] CancelledStatuses =
    [
        BookingStatus.CancelledByFamily,
        BookingStatus.DeclinedByCaregiver,
        BookingStatus.CancelledByCaregiver
    ];

    public static BookingRefundState Resolve(
        BookingStatus status,
        DateTime? paidOnUtc,
        DateTime? refundedOnUtc)
    {
        if (status == BookingStatus.Refunded || refundedOnUtc is not null)
        {
            return BookingRefundState.Succeeded;
        }

        if (paidOnUtc is not null
            && ClosedWithoutRefundStatus.Contains(status))
        {
            return BookingRefundState.Failed;
        }

        return BookingRefundState.NotApplicable;
    }

    /// <summary>
    /// The refund state of a booking read without loading its cancellation fact. An attempt the
    /// gateway could not confirm — or one still in flight because the process died before recording
    /// its outcome — is uncertain, so it resolves to <see cref="BookingRefundState.Pending"/> here
    /// too. Reads that have no fact (the admin, caregiver and family list projections) must not
    /// report such a booking as a plain <see cref="BookingRefundState.Failed"/> refund, which would
    /// invite a retry that the settlement guards refuse.
    /// </summary>
    public static BookingRefundState Resolve(Booking booking)
    {
        if (booking.Status != BookingStatus.Refunded
            && booking.RefundedOnUtc is null
            && (booking.IsRefundOutcomeAmbiguous || booking.IsRefundAttemptInFlight))
        {
            return BookingRefundState.Pending;
        }

        return Resolve(booking.Status, booking.PaidOnUtc, booking.RefundedOnUtc);
    }

    public static BookingRefundState ResolveWithFact(Booking booking, BookingCancellationFact? fact)
    {
        if (booking.Status == BookingStatus.Refunded || booking.RefundedOnUtc is not null)
        {
            return BookingRefundState.Succeeded;
        }

        if (fact is not null && fact.RefundEntitlement == BookingRefundEntitlement.NoRefundDue)
        {
            return BookingRefundState.NoRefundDue;
        }

        // Precedence: an attempt the gateway could not confirm, or one that is still in flight, is
        // uncertain. It is never reported as success (the money may not have moved) and never as a
        // plain failure (the money may have moved), and it is never retryable — an operator must
        // reconcile it first.
        if (booking.IsRefundOutcomeAmbiguous || booking.IsRefundAttemptInFlight)
        {
            return BookingRefundState.Pending;
        }

        // Success is derived from durable settled money, not from the entitlement: an award is only
        // "succeeded" once the amount owed has actually been recorded as refunded. A partial award
        // the provider refused reads Failed (the remainder is still owed and retryable), and a
        // partial award that settled reads Succeeded. Deriving it the other way round would report
        // a definitive provider refusal as a completed refund.
        if (fact?.AwardedMinorUnits is { } owed && owed > 0)
        {
            return booking.RefundedMinorUnits >= owed
                ? BookingRefundState.Succeeded
                : BookingRefundState.Failed;
        }

        // A revision-1 fact carries no award (it is amount-free by design), so its state keeps the
        // legacy reading of the booking itself.
        return Resolve(booking);
    }
}
