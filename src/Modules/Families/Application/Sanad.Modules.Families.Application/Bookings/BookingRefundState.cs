using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.Modules.Families.Application.Bookings;

/// <summary>
/// How a closed booking relates to Paymob refund.
/// Failed = paid, then cancelled/declined/expired, but <c>MarkRefunded</c> never ran
/// (gateway error or unpaid skip is NotApplicable).
/// </summary>
public enum BookingRefundState
{
    NotApplicable = 1,
    Failed = 2,
    Succeeded = 3,
    /// <summary>Paid or unpaid, policy decided nothing is owed; never renders as Failed; never retryable.</summary>
    NoRefundDue = 4
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

    public static BookingRefundState Resolve(Booking booking) =>
        Resolve(booking.Status, booking.PaidOnUtc, booking.RefundedOnUtc);

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

        return Resolve(booking);
    }
}
