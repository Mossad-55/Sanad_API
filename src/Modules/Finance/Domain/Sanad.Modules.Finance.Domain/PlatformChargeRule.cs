using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Finance.Domain;

public sealed class PlatformChargeRule : Entity<Guid>
{
    private PlatformChargeRule() { }

    private PlatformChargeRule(Guid id, decimal fee, decimal tax, int version, DateTime effectiveOnUtc, DateTime createdOnUtc, bool active) : base(id)
    {
        PlatformFeeRatePercentage = fee;
        TaxRatePercentage = tax;
        Version = version;
        EffectiveOnUtc = effectiveOnUtc;
        CreatedOnUtc = createdOnUtc;
        IsActive = active;
    }

    public decimal PlatformFeeRatePercentage { get; private set; }
    public decimal TaxRatePercentage { get; private set; }
    public int Version { get; private set; }
    public DateTime EffectiveOnUtc { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public bool IsActive { get; private set; }

    public static PlatformChargeRule Create(decimal fee, decimal tax, int version, DateTime effectiveOnUtc, DateTime? createdOnUtc = null, bool isActive = true)
    {
        if (fee is < 0 or > 100 || tax is < 0 or > 100)
            throw new DomainException("Platform fee and tax rates must be between 0 and 100 percent.");
        if (version <= 0) throw new DomainException("Platform charge rule version must be positive.");
        if (effectiveOnUtc.Kind != DateTimeKind.Utc) throw new DomainException("Platform charge effective timestamp must be UTC.");
        var created = createdOnUtc ?? DateTime.UtcNow;
        if (created.Kind != DateTimeKind.Utc) throw new DomainException("Platform charge creation timestamp must be UTC.");
        return new(Guid.CreateVersion7(), Round(fee), Round(tax), version, effectiveOnUtc, created, isActive);
    }

    public void Deactivate() => IsActive = false;
    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.ToEven);
}
