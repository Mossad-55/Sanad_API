using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Families.Domain.Subscriptions;

public enum SubscriptionCycle
{
    Monthly = 1,
    Annual = 2
}

public static class SubscriptionCycles
{
    public static SubscriptionCycle Parse(int value)
    {
        if (!Enum.IsDefined((SubscriptionCycle)value))
            throw new DomainException("Subscription billing cycle is invalid.");

        return (SubscriptionCycle)value;
    }
}
