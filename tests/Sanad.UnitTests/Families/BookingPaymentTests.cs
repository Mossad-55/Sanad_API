using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Domain.Bookings;
using Xunit;

namespace Sanad.UnitTests.Families;

public sealed class BookingPaymentTests
{
    private static Booking CreatePendingPaymentBooking()
    {
        DateTime now = DateTime.UtcNow;

        return Booking.Create(
            FamilyId.New(),
            UserId.New(),
            ElderlyId.New(),
            CaregiverId.New(),
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(now).AddDays(2),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "123 Nile St, Cairo",
            null,
            BookingPriceSnapshot.Calculate(500m, 15m),
            now.AddHours(24),
            DateOnly.FromDateTime(now),
            now);
    }

    [Fact]
    public void RecordPaymentIntent_ShouldStorePendingTransaction_WithSnapshotAmount()
    {
        var booking = CreatePendingPaymentBooking();

        booking.RecordPaymentIntent("PM-ORDER-1", PaymentMethod.Wallet, DateTime.UtcNow);

        var transaction = Assert.Single(booking.PaymentTransactions);
        Assert.Equal(PaymentTransactionStatus.Pending, transaction.Status);
        Assert.Equal(PaymentMethod.Wallet, transaction.Method);
        Assert.Equal(575m, transaction.Amount);
        Assert.Equal("EGP", transaction.Currency);
    }

    [Fact]
    public void MarkAsPaid_ShouldSettleMatchingPendingTransaction_AndRecordRefundGuards()
    {
        var booking = CreatePendingPaymentBooking();
        booking.RecordPaymentIntent("PM-ORDER-1", PaymentMethod.Card, DateTime.UtcNow);

        booking.MarkAsPaid("PM-ORDER-1", "PM-TXN-1", DateTime.UtcNow);

        Assert.Equal(BookingStatus.PendingCaregiverApproval, booking.Status);
        Assert.Equal(PaymentTransactionStatus.Succeeded, booking.PaymentTransactions.Single().Status);

        Assert.Throws<DomainException>(() => booking.MarkRefunded(null, DateTime.UtcNow));
    }

    [Fact]
    public void Decline_ThenRefund_ShouldReachRefunded_AndRefundTransactions()
    {
        var booking = CreatePendingPaymentBooking();
        booking.RecordPaymentIntent("PM-ORDER-1", PaymentMethod.Card, DateTime.UtcNow);
        booking.MarkAsPaid("PM-ORDER-1", "PM-TXN-1", DateTime.UtcNow);

        booking.DeclineByCaregiver("عدم توفر الموعد", DateTime.UtcNow);
        booking.MarkRefunded("PM-REFUND-1", DateTime.UtcNow);

        Assert.Equal(BookingStatus.Refunded, booking.Status);
        Assert.Equal("PM-REFUND-1", booking.PaymobRefundTransactionId);
        Assert.NotNull(booking.RefundedOnUtc);
        Assert.Equal(PaymentTransactionStatus.Refunded, booking.PaymentTransactions.Single().Status);

        Assert.Throws<DomainException>(() => booking.MarkRefunded("PM-REFUND-2", DateTime.UtcNow));
    }

    [Fact]
    public void RecordPaymentFailure_ShouldMarkTransactionFailed_WithoutChangingBookingStatus()
    {
        var booking = CreatePendingPaymentBooking();
        booking.RecordPaymentIntent("PM-ORDER-1", PaymentMethod.Card, DateTime.UtcNow);

        booking.RecordPaymentFailure("PM-ORDER-1", "PM-TXN-F1", DateTime.UtcNow);

        Assert.Equal(BookingStatus.PendingPayment, booking.Status);
        Assert.Equal(PaymentTransactionStatus.Failed, booking.PaymentTransactions.Single().Status);
    }
}