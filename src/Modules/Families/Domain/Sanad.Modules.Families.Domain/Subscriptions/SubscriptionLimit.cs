using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Families.Domain.Subscriptions;

public enum SubscriptionLimitKind
{
    Finite = 1,
    Unlimited = 2
}

public sealed class SubscriptionLimit : ValueObject
{
    private SubscriptionLimit(SubscriptionLimitKind kind, int? value)
    {
        Kind = kind;
        Value = value;
    }

    public SubscriptionLimitKind Kind { get; }
    public int? Value { get; }
    public bool IsUnlimited => Kind == SubscriptionLimitKind.Unlimited;

    public static SubscriptionLimit Finite(int value)
    {
        if (value <= 0)
            throw new DomainException("A finite subscription limit must be positive.");

        return new SubscriptionLimit(SubscriptionLimitKind.Finite, value);
    }

    public static SubscriptionLimit Unlimited { get; } =
        new(SubscriptionLimitKind.Unlimited, null);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Kind;
        yield return Value;
    }
}
