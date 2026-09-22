using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Subscriptions;

public enum SubscriptionPaymentMethod
{
    Card = 1,
    Wallet = 2
}

public enum SubscriptionPaymentAttemptStatus
{
    Pending = 1,
    Succeeded = 2,
    Failed = 3
}

public sealed class SubscriptionPaymentAttempt : Entity<Guid>
{
    private SubscriptionPaymentAttempt()
    {
    }

    private SubscriptionPaymentAttempt(
        Guid id,
        FamilyId familyId,
        Guid planVersionId,
        string planKey,
        int planVersion,
        decimal basePrice,
        decimal discountPercentage,
        decimal discountAmount,
        decimal taxableAmount,
        decimal taxRatePercentage,
        decimal taxAmount,
        decimal totalPayable,
        string currency,
        SubscriptionPaymentMethod method,
        string? couponCode,
        DateTime createdOnUtc)
        : base(id)
    {
        FamilyId = familyId;
        PlanVersionId = planVersionId;
        PlanKey = planKey;
        PlanVersion = planVersion;
        BasePrice = basePrice;
        DiscountPercentage = discountPercentage;
        DiscountAmount = discountAmount;
        TaxableAmount = taxableAmount;
        TaxRatePercentage = taxRatePercentage;
        TaxAmount = taxAmount;
        TotalPayable = totalPayable;
        Currency = currency;
        Method = method;
        CouponCode = couponCode;
        Status = SubscriptionPaymentAttemptStatus.Pending;
        CreatedOnUtc = createdOnUtc;
    }

    public FamilyId FamilyId { get; private set; }
    public Guid PlanVersionId { get; private set; }
    public string PlanKey { get; private set; } = string.Empty;
    public int PlanVersion { get; private set; }
    public decimal BasePrice { get; private set; }
    public decimal DiscountPercentage { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal TaxableAmount { get; private set; }
    public decimal TaxRatePercentage { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal TotalPayable { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public SubscriptionPaymentMethod Method { get; private set; }
    public string? CouponCode { get; private set; }
    public string MerchantReference => $"sub_{Id:N}";
    public string? PaymobOrderId { get; private set; }
    public string? PaymobTransactionId { get; private set; }
    public SubscriptionPaymentAttemptStatus Status { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? SettledOnUtc { get; private set; }
    public DateTime? FailedOnUtc { get; private set; }

    public static SubscriptionPaymentAttempt Create(
        FamilyId familyId,
        SubscriptionPlanVersion plan,
        decimal basePrice,
        decimal discountPercentage,
        decimal discountAmount,
        decimal taxableAmount,
        decimal taxRatePercentage,
        decimal taxAmount,
        decimal totalPayable,
        string? couponCode,
        SubscriptionPaymentMethod method,
        DateTime createdOnUtc)
    {
        if (familyId == FamilyId.Empty)
            throw new DomainException("A subscription payment requires a family.");
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Id == Guid.Empty)
            throw new DomainException("A subscription payment requires a plan version.");
        if (!Enum.IsDefined(method))
            throw new DomainException("Subscription payment method is invalid.");
        if (createdOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Subscription payment creation time must be UTC.");
        if (totalPayable < 0m || string.IsNullOrWhiteSpace(plan.Currency))
            throw new DomainException("Subscription payment amount is invalid.");

        return new SubscriptionPaymentAttempt(
            Guid.CreateVersion7(), familyId, plan.Id, plan.Key, plan.Version,
            Money(basePrice), Money(discountPercentage), Money(discountAmount),
            Money(taxableAmount), Money(taxRatePercentage), Money(taxAmount),
            Money(totalPayable), plan.Currency, method,
            string.IsNullOrWhiteSpace(couponCode) ? null : couponCode.Trim().ToUpperInvariant(),
            createdOnUtc);
    }

    public void RecordPaymobOrder(string paymobOrderId)
    {
        if (Status != SubscriptionPaymentAttemptStatus.Pending)
            throw new DomainException("Only pending subscription payments can record a provider order.");
        if (string.IsNullOrWhiteSpace(paymobOrderId))
            throw new DomainException("Paymob order id is required.");

        PaymobOrderId = paymobOrderId.Trim();
    }

    public bool TryMarkSucceeded(string paymobTransactionId, DateTime utcNow)
    {
        if (Status != SubscriptionPaymentAttemptStatus.Pending)
            return false;
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new DomainException("Subscription payment settlement time must be UTC.");

        Status = SubscriptionPaymentAttemptStatus.Succeeded;
        PaymobTransactionId = paymobTransactionId;
        SettledOnUtc = utcNow;
        return true;
    }

    public bool TryMarkFailed(string? paymobTransactionId, DateTime utcNow)
    {
        if (Status != SubscriptionPaymentAttemptStatus.Pending)
            return false;
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new DomainException("Subscription payment failure time must be UTC.");

        Status = SubscriptionPaymentAttemptStatus.Failed;
        PaymobTransactionId = paymobTransactionId;
        FailedOnUtc = utcNow;
        return true;
    }

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.ToEven);
}
