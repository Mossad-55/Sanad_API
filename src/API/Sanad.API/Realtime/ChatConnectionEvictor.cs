using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Realtime;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.API.Realtime;

/// <summary>
/// Hub-backed authorization invalidation: committed block/hide/privacy
/// changes evict affected realtime state (joins, typing) so no new dispatch
/// observes stale eligibility. Already-sent frames cannot be recalled;
/// clients rejoin/re-snapshot through the documented recovery flow.
/// </summary>
public sealed class ChatConnectionEvictor(
    IChatConnectionRegistry registry,
    ITypingTracker typing,
    IChatHubAudience audience,
    IChatRealtimePublisher publisher) : IChatAuthorizationChangedNotifier
{
    public async Task PairInvalidatedAsync(
        UserId firstUserId,
        UserId secondUserId,
        CancellationToken cancellationToken = default)
    {
        foreach (UserId user in new[] { firstUserId, secondUserId })
        {
            foreach (string connection in registry.GetConnections(user))
            {
                foreach (ConversationId joined in registry.GetJoined(connection))
                {
                    ConversationAudience? state = await audience.GetAudienceAsync(
                        joined, cancellationToken);

                    if (state is null || state.BlockedPair)
                    {
                        registry.Leave(connection, joined);
                    }
                }
            }

            foreach (string connection in registry.GetConnections(user))
            {
                foreach (var cleared in typing.ClearConnection(connection))
                {
                    await publisher.PublishTypingChangedAsync(
                        cleared.ConversationId.Value,
                        cleared.UserId.Value,
                        false,
                        DateTime.UtcNow,
                        cancellationToken);
                }
            }
        }
    }

    public Task ConversationHiddenAsync(
        ConversationId conversationId,
        UserId hiddenForUserId,
        CancellationToken cancellationToken = default)
    {
        foreach (string connection in registry.GetConnections(hiddenForUserId))
        {
            registry.Leave(connection, conversationId);
        }

        typing.ClearUser(hiddenForUserId, conversationId);
        return Task.CompletedTask;
    }

    public Task PresenceSettingsChangedAsync(
        UserId ownerUserId,
        CancellationToken cancellationToken = default)
    {
        // Presence is resolved per dispatch/snapshot from durable settings;
        // changing them withdraws previously shown state via the next
        // snapshot (documented client reset). Only ephemeral typing is
        // cleared here so a stale hint cannot outlive the change.
        foreach (string connection in registry.GetConnections(ownerUserId))
        {
            typing.ClearConnection(connection);
        }

        return Task.CompletedTask;
    }
}
