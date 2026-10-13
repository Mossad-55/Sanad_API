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
        string connectionId,
        bool isTyping,
        DateTime now,
        out DateTime expiresOnUtc);

    /// <summary>Clears all hints held by a connection (disconnect).</summary>
    IReadOnlyList<(UserId UserId, ConversationId ConversationId)> ClearConnection(
        string connectionId);

    void ClearUser(UserId userId, ConversationId conversationId);

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

    Task PublishTypingChangedAsync(
        Guid conversationId,
        Guid userId,
        bool isTyping,
        DateTime expiresOnUtc,
        CancellationToken cancellationToken);

    Task PublishPresenceChangedAsync(
        Guid userId,
        bool isOnline,
        DateTime? lastSeenOnUtc,
        CancellationToken cancellationToken);
}

/// <summary>
/// Per-dispatch authorization resolution for realtime fan-out. Groups are
/// routing only: every dispatch resolves currently authorized recipients
/// (participant, visible, unblocked, privacy-gated) first — never an
/// unchecked broadcast on an earlier membership check.
/// </summary>
public sealed record ConversationAudience(
    UserId ParticipantOne,
    UserId ParticipantTwo,
    bool HiddenForParticipantOne,
    bool HiddenForParticipantTwo,
    bool BlockedPair);

public interface IChatHubAudience
{
    Task<ConversationAudience?> GetAudienceAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Users eligible to receive realtime events for a conversation,
    /// optionally excluding one user (e.g. the typing sender). Empty when
    /// the pair is blocked (either direction) — REST history is unaffected.
    /// </summary>
    Task<IReadOnlyList<UserId>> GetEventRecipientsAsync(
        ConversationId conversationId,
        UserId? excludeUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Users eligible to observe the subject's presence: participants of
    /// shared visible unblocked conversations. Bounded scan.
    /// </summary>
    Task<IReadOnlyList<UserId>> GetPresenceSubscribersAsync(
        UserId subjectUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The subject's durable presence-privacy gate (defaults false/false
    /// when no row exists — absence of a policy is never consent).
    /// </summary>
    Task<(bool ShowOnlineStatus, bool ShareLastSeen)> GetPresenceGateAsync(
        UserId subjectUserId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Authorization-invalidation seam: committed block/hide/privacy changes
/// evict affected realtime state (joins, typing) before further dispatch.
/// No-op default keeps pre-existing handler constructions compiling;
/// production wires the hub-backed evictor.
/// </summary>
public interface IChatAuthorizationChangedNotifier
{
    Task PairInvalidatedAsync(
        UserId firstUserId,
        UserId secondUserId,
        CancellationToken cancellationToken = default);

    Task ConversationHiddenAsync(
        ConversationId conversationId,
        UserId hiddenForUserId,
        CancellationToken cancellationToken = default);

    Task PresenceSettingsChangedAsync(
        UserId ownerUserId,
        CancellationToken cancellationToken = default);
}
