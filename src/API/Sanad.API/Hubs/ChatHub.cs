using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Sanad.API.Authorization;
using Sanad.API.Realtime;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.RateLimiting;
using Sanad.Modules.Chat.Application.Conversations;
using Sanad.Modules.Chat.Application.Realtime;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.API.Hubs;

/// <summary>
/// Chat Plus realtime hub (`/hubs/chat`). Thin and authenticated: every
/// method derives identity from the JWT `sub` (never a client-supplied user
/// id), revalidates current authorization per call, and drops ineligible or
/// rate-limited hints silently with counters. Send/history/upload/read/
/// report stay REST; the hub carries only ephemeral hints plus committed
/// fan-out published by the dispatcher through
/// <see cref="IChatRealtimePublisher"/>.
/// Single-instance only. Browser WebSocket/SSE clients authenticate with
/// `?access_token=` on this hub path only (see JWT configuration);
/// tokens, connection tokens, and payloads are redacted from logs.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.NormalAccess)]
public sealed class ChatHub(
    IChatConnectionRegistry registry,
    ITypingTracker typing,
    IPresenceTracker presence,
    IChatHubAudience audience,
    IChatRealtimePublisher publisher,
    IChatRateGate rateGate,
    IChatUserLookup? userLookup = null) : Hub
{
    /// <summary>Hub connection budget per user (multi-device OR).</summary>
    public const int MaxConnectionsPerUser = 5;

    /// <summary>Joined-conversation budget per connection.</summary>
    public const int MaxJoinedPerConnection = 20;

    public override async Task OnConnectedAsync()
    {
        UserId? userId = await CurrentActiveUserIdAsync();

        if (userId is null ||
            !rateGate.TryAcquireNegotiate(userId.Value, out _) ||
            registry.CountConnections(userId.Value) >= MaxConnectionsPerUser)
        {
            Context.Abort();
            return;
        }

        registry.AddConnection(userId.Value, Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        registry.RemoveConnection(Context.ConnectionId, out UserId? userId);
        registry.LeaveAll(Context.ConnectionId);

        foreach (var cleared in typing.ClearConnection(Context.ConnectionId))
        {
            await publisher.PublishTypingChangedAsync(
                cleared.ConversationId.Value,
                cleared.UserId.Value,
                false,
                DateTime.UtcNow,
                CancellationToken.None);
        }

        if (userId is not null)
        {
            presence.RemoveConnection(Context.ConnectionId, DateTime.UtcNow);
            await BroadcastPresenceAsync(userId.Value);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Idempotent join for this connection. Rejoin after every reconnect.
    /// Returns `{ joined, presence }` where presence is the current
    /// permitted counterpart snapshot (nulls when undisclosed).
    /// </summary>
    public async Task<object> JoinConversation(Guid conversationId)
    {
        UserId? userId = await CurrentActiveUserIdAsync();
        var conversation = new ConversationId(conversationId);

        if (userId is null ||
            !await IsEligibleAsync(conversation, userId.Value))
        {
            throw new HubException("Chat.Conversation.NotFound");
        }

        if (registry.CountJoined(Context.ConnectionId) >= MaxJoinedPerConnection &&
            !registry.GetJoined(Context.ConnectionId).Contains(conversation))
        {
            throw new HubException("Chat.Hub.JoinRejected");
        }

        registry.Join(Context.ConnectionId, conversation);

        ConversationAudience? audienceState = await audience.GetAudienceAsync(
            conversation, CancellationToken.None);

        UserId counterpart = audienceState is not null &&
            audienceState.ParticipantOne == userId.Value
            ? audienceState.ParticipantTwo
            : audienceState?.ParticipantOne ?? UserId.Empty;

        var gate = await audience.GetPresenceGateAsync(
            counterpart, CancellationToken.None);

        bool? isOnline = gate.ShowOnlineStatus
            ? presence.IsOnline(counterpart, DateTime.UtcNow)
            : null;
        DateTime? lastSeen = gate.ShareLastSeen
            ? presence.GetLastSeen(counterpart, DateTime.UtcNow)
            : null;

        return new
        {
            joined = true,
            presence = new { userId = counterpart.Value, isOnline, lastSeenOnUtc = lastSeen }
        };
    }

    /// <summary>Idempotent leave; stops this connection's typing state.</summary>
    public async Task<object> LeaveConversation(Guid conversationId)
    {
        UserId? userId = await CurrentActiveUserIdAsync();
        var conversation = new ConversationId(conversationId);

        registry.Leave(Context.ConnectionId, conversation);

        if (userId is not null)
        {
            typing.ClearUser(userId.Value, conversation);
            await publisher.PublishTypingChangedAsync(
                conversationId,
                userId.Value.Value,
                false,
                DateTime.UtcNow,
                CancellationToken.None);
        }

        return new { joined = false };
    }

    /// <summary>
    /// Ephemeral typing hint for a joined eligible conversation. Unjoined,
    /// ineligible, or rate-limited hints are dropped silently; expiry is
    /// server-set (8s) and broadcast by the sweep when still eligible.
    /// </summary>
    public async Task SetTyping(Guid conversationId, bool isTyping)
    {
        UserId? userId = await CurrentActiveUserIdAsync();
        var conversation = new ConversationId(conversationId);

        if (userId is null ||
            !registry.GetJoined(Context.ConnectionId).Contains(conversation) ||
            !await IsEligibleAsync(conversation, userId.Value))
        {
            return;
        }

        if (!typing.Refresh(
                userId.Value, conversation, Context.ConnectionId,
                isTyping, DateTime.UtcNow, out DateTime expiresOnUtc))
        {
            return;
        }

        await publisher.PublishTypingChangedAsync(
            conversationId,
            userId.Value.Value,
            isTyping,
            expiresOnUtc,
            CancellationToken.None);
    }

    /// <summary>
    /// Liveness refresh for the caller's own device. Never grants
    /// permission or discloses arbitrary users; at most one accepted
    /// refresh per 30s per connection.
    /// </summary>
    public async Task Heartbeat()
    {
        UserId? userId = await CurrentActiveUserIdAsync();

        if (userId is null)
        {
            Context.Abort();
            return;
        }

        if (!presence.Heartbeat(userId.Value, Context.ConnectionId, DateTime.UtcNow))
        {
            return;
        }

        await BroadcastPresenceAsync(userId.Value);
    }

    private async Task<bool> IsEligibleAsync(ConversationId conversation, UserId userId)
    {
        ConversationAudience? state = await audience.GetAudienceAsync(
            conversation, CancellationToken.None);

        if (state is null || state.BlockedPair)
        {
            return false;
        }

        bool isParticipant =
            state.ParticipantOne == userId || state.ParticipantTwo == userId;

        if (!isParticipant)
        {
            return false;
        }

        bool hidden =
            (userId == state.ParticipantOne && state.HiddenForParticipantOne) ||
            (userId == state.ParticipantTwo && state.HiddenForParticipantTwo);

        return !hidden;
    }

    /// <summary>
    /// PresenceChanged is a hint, not a durable audit: broadcast only when
    /// the subject explicitly enables online sharing; either-direction
    /// blocks already exclude subscribers at the audience layer.
    /// </summary>
    private async Task BroadcastPresenceAsync(UserId subject)
    {
        var gate = await audience.GetPresenceGateAsync(
            subject, CancellationToken.None);

        if (!gate.ShowOnlineStatus)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;

        await publisher.PublishPresenceChangedAsync(
            subject.Value,
            presence.IsOnline(subject, now),
            gate.ShareLastSeen ? presence.GetLastSeen(subject, now) : null,
            CancellationToken.None);
    }

    private async Task<UserId?> CurrentActiveUserIdAsync()
    {
        string? sub = Context.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(sub, out Guid id) || id == Guid.Empty)
        {
            return null;
        }

        var userId = new UserId(id);
        // Production DI always supplies the active-user lookup. Optional
        // only for the existing isolated hub tests.
        if (userLookup is not null &&
            await userLookup.GetProfileAsync(userId, Context.ConnectionAborted) is null)
        {
            registry.RemoveConnection(Context.ConnectionId, out _);
            typing.ClearConnection(Context.ConnectionId);
            presence.RemoveConnection(Context.ConnectionId, DateTime.UtcNow);
            Context.Abort();
            return null;
        }

        return userId;
    }
}
