using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Bookings;

public sealed class PaymentTransaction : Entity<PaymentTransactionId>
{
    private PaymentTransaction()
    {
    }

    private PaymentTransaction(
        PaymentTransactionId id,
        string paymobOrderId,
        PaymentMethod method,
        decimal amount,
        string currency,
        DateTime createdOnUtc)
        : base(id)
    {
        PaymobOrderId = paymobOrderId;
        Method = method;
        Amount = amount;
        Currency = currency;
        Status = PaymentTransactionStatus.Pending;
        CreatedOnUtc = createdOnUtc;
    }

    public string PaymobOrderId { get; private set; } = string.Empty;

    public string? PaymobTransactionId { get; private set; }

    public PaymentMethod Method { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public PaymentTransactionStatus Status { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime? SettledOnUtc { get; private set; }

    public DateTime? FailedOnUtc { get; private set; }

    public DateTime? RefundedOnUtc { get; private set; }

    internal static PaymentTransaction Create(
        string paymobOrderId,
        PaymentMethod method,
        decimal amount,
        string currency,
        DateTime createdOnUtc)
    {
        return new PaymentTransaction(
            PaymentTransactionId.New(),
            paymobOrderId,
            method,
            amount,
            currency,
            createdOnUtc);
    }

    internal void MarkSucceeded(string paymobTransactionId, DateTime utcNow)
    {
        if (Status != PaymentTransactionStatus.Pending)
        {
            return;
        }

        PaymobTransactionId = paymobTransactionId;
        Status = PaymentTransactionStatus.Succeeded;
        SettledOnUtc = utcNow;
    }

    internal void MarkFailed(string? paymobTransactionId, DateTime utcNow)
    {
        if (Status != PaymentTransactionStatus.Pending)
        {
            return;
        }

        PaymobTransactionId = paymobTransactionId;
        Status = PaymentTransactionStatus.Failed;
        FailedOnUtc = utcNow;
    }

    internal void MarkRefunded(DateTime utcNow)
    {
        if (Status != PaymentTransactionStatus.Succeeded)
        {
            return;
        }

        Status = PaymentTransactionStatus.Refunded;
        RefundedOnUtc = utcNow;
    }
}