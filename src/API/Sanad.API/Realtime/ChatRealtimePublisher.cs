using Microsoft.AspNetCore.SignalR;
using Sanad.API.Hubs;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Realtime;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.API.Realtime;

/// <summary>
/// Hub-context fan-out for committed chat state. Every method resolves the
/// currently authorized audience and the currently joined connections first;
/// stale connections receive nothing and already-sent frames are never
/// recalled (the boundary is no new dispatch after a committed invalidation
/// is observed). No socket event implies physical device delivery —
/// delivered remains the recipient's REST fetch acknowledgement.
/// </summary>
public sealed class ChatRealtimePublisher(
    IHubContext<ChatHub> hub,
    IChatHubAudience audience,
    IChatConnectionRegistry registry) : IChatRealtimePublisher
{
    public async Task PublishMessageCreatedAsync(
        Guid conversationId,
        object messagePayload,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> connections = await JoinedConnectionsAsync(
            new ConversationId(conversationId), null, cancellationToken);

        if (connections.Count == 0)
        {
            return;
        }

        await hub.Clients.Clients(connections).SendAsync(
            "MessageCreated",
            new { conversationId, message = messagePayload },
            cancellationToken);
    }

    public async Task PublishReceiptUpdatedAsync(
        Guid conversationId,
        Guid messageId,
        DateTime? deliveredOnUtc,
        DateTime? readOnUtc,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> connections = await JoinedConnectionsAsync(
            new ConversationId(conversationId), null, cancellationToken);

        if (connections.Count == 0)
        {
            return;
        }

        await hub.Clients.Clients(connections).SendAsync(
            "MessageReceiptUpdated",
            new { conversationId, messageId, deliveredOnUtc, readOnUtc },
            cancellationToken);
    }

    public async Task PublishTypingChangedAsync(
        Guid conversationId,
        Guid userId,
        bool isTyping,
        DateTime expiresOnUtc,
        CancellationToken cancellationToken)
    {
        var conversation = new ConversationId(conversationId);
        var sender = new UserId(userId);

        IReadOnlyList<string> connections = await JoinedConnectionsAsync(
            conversation, sender, cancellationToken);

        if (connections.Count == 0)
        {
            return;
        }

        await hub.Clients.Clients(connections).SendAsync(
            "TypingChanged",
            new { conversationId, userId, isTyping, expiresOnUtc },
            cancellationToken);
    }

    public async Task PublishPresenceChangedAsync(
        Guid userId,
        bool isOnline,
        DateTime? lastSeenOnUtc,
        CancellationToken cancellationToken)
    {
        var subject = new UserId(userId);

        IReadOnlyList<UserId> subscribers = await audience
            .GetPresenceSubscribersAsync(subject, cancellationToken);

        var connections = new HashSet<string>(StringComparer.Ordinal);

        foreach (UserId subscriber in subscribers)
        {
            foreach (string connection in registry.GetConnections(subscriber))
            {
                connections.Add(connection);
            }
        }

        if (connections.Count == 0)
        {
            return;
        }

        await hub.Clients.Clients(connections.ToArray()).SendAsync(
            "PresenceChanged",
            new { userId, isOnline, lastSeenOnUtc },
            cancellationToken);
    }

    private async Task<IReadOnlyList<string>> JoinedConnectionsAsync(
        ConversationId conversationId,
        UserId? excludeUserId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<UserId> recipients = await audience.GetEventRecipientsAsync(
            conversationId, excludeUserId, cancellationToken);

        var connections = new HashSet<string>(StringComparer.Ordinal);

        foreach (UserId recipient in recipients)
        {
            foreach (string connection in registry.GetConnections(recipient))
            {
                if (registry.GetJoined(connection).Contains(conversationId))
                {
                    connections.Add(connection);
                }
            }
        }

        return connections.ToArray();
    }
}
