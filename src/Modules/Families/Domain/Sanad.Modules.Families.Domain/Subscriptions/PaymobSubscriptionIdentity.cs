using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Families.Domain.Subscriptions;

public sealed class PaymobSubscriptionIdentity : Entity<Guid>
{
    private PaymobSubscriptionIdentity()
    {
    }

    private PaymobSubscriptionIdentity(
        Guid id,
        string providerSubscriptionId,
        Guid paymentAttemptId,
        Guid? familySubscriptionId)
        : base(id)
    {
        ProviderSubscriptionId = providerSubscriptionId;
        PaymentAttemptId = paymentAttemptId;
        FamilySubscriptionId = familySubscriptionId;
    }

    public string ProviderSubscriptionId { get; private set; } = string.Empty;
    public Guid PaymentAttemptId { get; private set; }
    public Guid? FamilySubscriptionId { get; private set; }

    public static PaymobSubscriptionIdentity Create(
        string providerSubscriptionId,
        Guid paymentAttemptId,
        Guid? familySubscriptionId)
    {
        if (string.IsNullOrWhiteSpace(providerSubscriptionId)
            || paymentAttemptId == Guid.Empty
            || (familySubscriptionId is Guid subscriptionId && subscriptionId == Guid.Empty))
            throw new DomainException("Paymob subscription identity ownership is incomplete.");

        return new PaymobSubscriptionIdentity(
            Guid.CreateVersion7(),
            providerSubscriptionId.Trim(),
            paymentAttemptId,
            familySubscriptionId);
    }
}
