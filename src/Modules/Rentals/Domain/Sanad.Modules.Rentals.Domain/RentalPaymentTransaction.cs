using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Rentals.Domain;

/// <summary>The payment rails a rental booking can be captured with. Products/other rails are out of scope.</summary>
public enum RentalPaymentMethod
{
    Card = 1,
    Wallet = 2
}

public enum RentalPaymentTransactionStatus
{
    /// <summary>The intent was created; the gateway has not settled.</summary>
    Pending = 1,

    /// <summary>The gateway confirmed the capture of the rent plus deposit total.</summary>
    Succeeded = 2,

    /// <summary>The gateway reported the attempt failed; a fresh intent may be created.</summary>
    Failed = 3
}

/// <summary>
/// One gateway payment attempt for a rental booking (the "booking/payment"
/// part of the clause 8 lifecycle). Mirrors the Families booking payment
/// transaction shape: intent first, a later confirmation settles the row.
/// The recorded amount is the whole captured total (rent + deposit) so the
/// refund caps (#4) can be checked against real captured funds.
/// </summary>
public sealed class RentalPaymentTransaction : Entity<Guid>
{
    public const int MaximumPaymobOrderIdLength = 200;
    public const int MaximumPaymobTransactionIdLength = 100;

    private RentalPaymentTransaction() { }

    private RentalPaymentTransaction(
        Guid id,
        string paymobOrderId,
        RentalPaymentMethod method,
        long amountMinorUnits,
        DateTime createdOnUtc) : base(id)
    {
        PaymobOrderId = paymobOrderId;
        Method = method;
        AmountMinorUnits = amountMinorUnits;
        Currency = "EGP";
        Status = RentalPaymentTransactionStatus.Pending;
        CreatedOnUtc = createdOnUtc;
    }

    public Guid RentalBookingId { get; private set; }

    /// <summary>The gateway merchant order id; idempotent lookup key for the confirmation.</summary>
    public string PaymobOrderId { get; private set; } = string.Empty;

    public string? PaymobTransactionId { get; private set; }

    public RentalPaymentMethod Method { get; private set; }

    /// <summary>Captured intent total: prepaid rent plus deposit, EGP piastres.</summary>
    public long AmountMinorUnits { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public RentalPaymentTransactionStatus Status { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime? SettledOnUtc { get; private set; }

    public DateTime? FailedOnUtc { get; private set; }

    public static RentalPaymentTransaction Create(
        Guid rentalBookingId,
        string paymobOrderId,
        RentalPaymentMethod method,
        long amountMinorUnits,
        DateTime createdOnUtc)
    {
        if (rentalBookingId == Guid.Empty)
            throw new DomainException("A rental payment requires its booking.");
        string order = paymobOrderId?.Trim() ?? string.Empty;
        if (order.Length == 0 || order.Length > MaximumPaymobOrderIdLength)
            throw new DomainException($"The payment order id must be between 1 and {MaximumPaymobOrderIdLength} characters.");
        if (!Enum.IsDefined(method))
            throw new DomainException("A rental payment requires a known payment method.");
        if (amountMinorUnits <= 0)
            throw new DomainException("A rental payment must carry a positive amount.");
        if (createdOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("A rental payment timestamp must be UTC.");

        RentalPaymentTransaction transaction = new(Guid.CreateVersion7(), order, method, amountMinorUnits, createdOnUtc);
        transaction.RentalBookingId = rentalBookingId;
        return transaction;
    }

    public void MarkSucceeded(string paymobTransactionId, DateTime utcNow)
    {
        string reference = paymobTransactionId?.Trim() ?? string.Empty;
        if (reference.Length == 0 || reference.Length > MaximumPaymobTransactionIdLength)
            throw new DomainException("The settled payment requires a gateway transaction reference.");

        if (Status != RentalPaymentTransactionStatus.Pending)
        {
            // Already settled/failed: a late duplicate confirmation is an
            // idempotent no-op, never a state rewrite.
            return;
        }

        if (utcNow.Kind != DateTimeKind.Utc)
            throw new DomainException("A rental payment timestamp must be UTC.");

        PaymobTransactionId = reference;
        Status = RentalPaymentTransactionStatus.Succeeded;
        SettledOnUtc = utcNow;
    }

    public void MarkFailed(string? paymobTransactionId, DateTime utcNow)
    {
        if (Status != RentalPaymentTransactionStatus.Pending)
        {
            return;
        }

        if (utcNow.Kind != DateTimeKind.Utc)
            throw new DomainException("A rental payment timestamp must be UTC.");

        string? reference = string.IsNullOrWhiteSpace(paymobTransactionId) ? null : paymobTransactionId!.Trim();
        PaymobTransactionId = reference;
        Status = RentalPaymentTransactionStatus.Failed;
        FailedOnUtc = utcNow;
    }
}
