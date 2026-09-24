using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Families.Domain.Subscriptions;

public sealed class PaymobSubscriptionCallback : Entity<Guid>
{
    private PaymobSubscriptionCallback()
    {
    }

    private PaymobSubscriptionCallback(
        Guid id,
        string callbackKey,
        string? paymobRequestId,
        string providerSubscriptionId,
        string triggerType,
        DateTime receivedOnUtc)
        : base(id)
    {
        CallbackKey = callbackKey;
        PaymobRequestId = paymobRequestId;
        ProviderSubscriptionId = providerSubscriptionId;
        TriggerType = triggerType;
        ReceivedOnUtc = receivedOnUtc;
    }

    public string CallbackKey { get; private set; } = string.Empty;
    public string? PaymobRequestId { get; private set; }
    public string ProviderSubscriptionId { get; private set; } = string.Empty;
    public string TriggerType { get; private set; } = string.Empty;
    public DateTime ReceivedOnUtc { get; private set; }

    public static PaymobSubscriptionCallback Create(
        string callbackKey,
        string? paymobRequestId,
        string providerSubscriptionId,
        string triggerType,
        DateTime receivedOnUtc)
    {
        if (string.IsNullOrWhiteSpace(callbackKey)
            || string.IsNullOrWhiteSpace(providerSubscriptionId)
            || string.IsNullOrWhiteSpace(triggerType)
            || receivedOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Paymob subscription callback identity is incomplete.");

        return new PaymobSubscriptionCallback(
            Guid.CreateVersion7(),
            callbackKey.Trim(),
            string.IsNullOrWhiteSpace(paymobRequestId) ? null : paymobRequestId.Trim(),
            providerSubscriptionId.Trim(),
            triggerType.Trim(),
            receivedOnUtc);
    }
}
