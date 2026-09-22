using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Families.Domain.Subscriptions;

public sealed class SubscriptionTaxRule : Entity<Guid>
{
    private SubscriptionTaxRule()
    {
    }

    private SubscriptionTaxRule(
        Guid id,
        decimal ratePercentage,
        int version,
        DateTime effectiveOnUtc,
        DateTime createdOnUtc,
        bool isActive)
        : base(id)
    {
        RatePercentage = ratePercentage;
        Version = version;
        EffectiveOnUtc = effectiveOnUtc;
        CreatedOnUtc = createdOnUtc;
        IsActive = isActive;
    }

    public decimal RatePercentage { get; private set; }
    public int Version { get; private set; }
    public DateTime EffectiveOnUtc { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public bool IsActive { get; private set; }

    public static SubscriptionTaxRule Create(
        decimal ratePercentage,
        int version,
        DateTime effectiveOnUtc,
        DateTime? createdOnUtc = null,
        bool isActive = true)
    {
        if (ratePercentage < 0m || ratePercentage > 100m)
            throw new DomainException("Subscription tax rate must be between 0 and 100 percent.");

        if (version <= 0)
            throw new DomainException("Subscription tax rule version must be positive.");

        if (effectiveOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Subscription tax effective timestamp must be UTC.");

        DateTime created = createdOnUtc ?? DateTime.UtcNow;
        if (created.Kind != DateTimeKind.Utc)
            throw new DomainException("Subscription tax creation timestamp must be UTC.");

        return new SubscriptionTaxRule(
            Guid.CreateVersion7(),
            decimal.Round(ratePercentage, 2, MidpointRounding.ToEven),
            version,
            effectiveOnUtc,
            created,
            isActive);
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}
