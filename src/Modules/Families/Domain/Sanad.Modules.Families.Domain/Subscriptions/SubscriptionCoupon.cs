using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Families.Domain.Subscriptions;

public sealed class SubscriptionCoupon : Entity<Guid>
{
    private SubscriptionCoupon()
    {
    }

    private SubscriptionCoupon(
        Guid id,
        string code,
        Guid planVersionId,
        decimal discountPercentage,
        DateTime expiresOnUtc,
        DateTime createdOnUtc)
        : base(id)
    {
        Code = code;
        PlanVersionId = planVersionId;
        DiscountPercentage = discountPercentage;
        ExpiresOnUtc = expiresOnUtc;
        CreatedOnUtc = createdOnUtc;
    }

    public string Code { get; private set; } = string.Empty;
    public Guid PlanVersionId { get; private set; }
    public decimal DiscountPercentage { get; private set; }
    public DateTime ExpiresOnUtc { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    public static SubscriptionCoupon Create(
        string code,
        Guid planVersionId,
        decimal discountPercentage,
        DateTime expiresOnUtc,
        DateTime? createdOnUtc = null)
    {
        string normalizedCode = code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalizedCode.Length is < 3 or > 50)
            throw new DomainException("Coupon code must contain between 3 and 50 characters.");
        if (planVersionId == Guid.Empty)
            throw new DomainException("A coupon requires a subscription plan version.");
        if (discountPercentage < 1m || discountPercentage > 100m)
            throw new DomainException("Coupon discount must be between 1 and 100 percent.");
        if (expiresOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Coupon expiry must be UTC.");

        DateTime created = createdOnUtc ?? DateTime.UtcNow;
        if (created.Kind != DateTimeKind.Utc || expiresOnUtc <= created)
            throw new DomainException("Coupon expiry must be after creation time.");

        return new SubscriptionCoupon(
            Guid.CreateVersion7(), normalizedCode, planVersionId,
            decimal.Round(discountPercentage, 2, MidpointRounding.ToEven),
            expiresOnUtc, created);
    }
}
