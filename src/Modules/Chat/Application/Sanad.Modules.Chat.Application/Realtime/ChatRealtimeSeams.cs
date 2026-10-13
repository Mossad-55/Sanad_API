using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.Modules.Chat.Application.Realtime;

/// <summary>
/// Single-instance in-memory connection registry seam (realtime topology).
/// Connection ids are SignalR connection ids; joined conversations are
/// re-established by the client after every reconnect.
/// </summary>
public interface IChatConnectionRegistry
{
    void AddConnection(UserId userId, string connectionId);

    void RemoveConnection(string connectionId, out UserId? userId);

    IReadOnlyList<string> GetConnections(UserId userId);

    int CountConnections(UserId userId);

    void Join(string connectionId, ConversationId conversationId);

    void Leave(string connectionId, ConversationId conversationId);

    void LeaveAll(string connectionId);

    IReadOnlyList<ConversationId> GetJoined(string connectionId);

    int CountJoined(string connectionId);
}

/// <summary>
/// Typing-hint tracker seam. At most one accepted hint per 2s per
/// user/conversation; a start expires 8s after the last refresh without a
/// new one; stop/disconnect clears.
/// </summary>
public interface ITypingTracker
{
    /// <returns>True when the hint was accepted (rate check passed).</returns>
    bool Refresh(
        UserId userId,
        ConversationId conversationId,
        bool isTyping,
        DateTime now,
        out DateTime expiresOnUtc);

    void ClearUser(UserId userId, ConversationId conversationId);

    void ClearConnection(
        string connectionId,
        Func<string, UserId?> resolveUser,
        Action<UserId, ConversationId> onCleared);

    IReadOnlyList<(UserId UserId, ConversationId ConversationId)> SweepExpired(
        DateTime now);
}

/// <summary>
/// Presence tracker seam. Online = any eligible live device with heartbeat
/// age under 90s (OR across devices). Last-seen is the last accepted
/// heartbeat instant only (never the timeout instant); a restart loses all
/// presence/last-seen (unknown/null until new heartbeats).
/// </summary>
public interface IPresenceTracker
{
    /// <returns>True when the heartbeat was accepted (rate check passed).</returns>
    bool Heartbeat(UserId userId, string connectionId, DateTime now);

    void RemoveConnection(string connectionId, DateTime now);

    bool IsOnline(UserId userId, DateTime now);

    DateTime? GetLastSeen(UserId userId, DateTime now);
}

/// <summary>
/// After-commit realtime fan-out seam (API implements it over
/// `IHubContext&lt;ChatHub&gt;`). The dispatcher resolves currently
/// authorized connections per dispatch; already-sent frames cannot be
/// recalled, so the boundary is: no new dispatch after a committed
/// invalidation is observed.
/// </summary>
public interface IChatRealtimePublisher
{
    Task PublishMessageCreatedAsync(
        Guid conversationId,
        object messagePayload,
        CancellationToken cancellationToken);

    Task PublishReceiptUpdatedAsync(
        Guid conversationId,
        Guid messageId,
        DateTime? deliveredOnUtc,
        DateTime? readOnUtc,
        CancellationToken cancellationToken);
}
