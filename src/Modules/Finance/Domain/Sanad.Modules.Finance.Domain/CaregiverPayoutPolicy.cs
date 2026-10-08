using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Finance.Domain;

public sealed class CaregiverPayoutPolicy : Entity<Guid>
{
    private CaregiverPayoutPolicy() { }

    private CaregiverPayoutPolicy(Guid id, int payoutDelayHours, int version, DateTime effectiveOnUtc, DateTime createdOnUtc, bool active) : base(id)
    {
        PayoutDelayHours = payoutDelayHours;
        Version = version;
        EffectiveOnUtc = effectiveOnUtc;
        CreatedOnUtc = createdOnUtc;
        IsActive = active;
    }

    public int PayoutDelayHours { get; private set; }
    public int Version { get; private set; }
    public DateTime EffectiveOnUtc { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public bool IsActive { get; private set; }

    public static CaregiverPayoutPolicy Create(int payoutDelayHours, int version, DateTime effectiveOnUtc, DateTime? createdOnUtc = null, bool isActive = true)
    {
        if (payoutDelayHours < 0)
            throw new DomainException("Payout delay must be zero or more hours.");
        if (version <= 0) throw new DomainException("Payout policy version must be positive.");
        if (effectiveOnUtc.Kind != DateTimeKind.Utc) throw new DomainException("Payout policy effective timestamp must be UTC.");
        var created = createdOnUtc ?? DateTime.UtcNow;
        if (created.Kind != DateTimeKind.Utc) throw new DomainException("Payout policy creation timestamp must be UTC.");
        return new(Guid.CreateVersion7(), payoutDelayHours, version, effectiveOnUtc, created, isActive);
    }

    public void Deactivate() => IsActive = false;
}
