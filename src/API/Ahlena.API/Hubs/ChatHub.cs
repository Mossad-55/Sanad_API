using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Ahlena.API.Authorization;
using Ahlena.API.Realtime;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Application.RateLimiting;
using Ahlena.Modules.Chat.Application.Conversations;
using Ahlena.Modules.Chat.Application.Realtime;
using Ahlena.Modules.Chat.Domain.Conversations;

namespace Ahlena.API.Hubs;

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
[Authorize(Policy = AuthorizationPolicies.SessionBoundAccess)]
public sealed class ChatHub(
    IChatConnectionRegistry registry,
    ITypingTracker typing,
    IPresenceTracker presence,
    IChatHubAudience audience,
    IChatRealtimePublisher publisher,
    IChatRateGate rateGate,
    IChatUserLookup? userLookup = null,
    Security.IIdentitySessionValidator? sessionValidator = null) : Hub
{
    /// <summary>Hub connection budget per user (multi-device OR).</summary>
    public const int MaxConnectionsPerUser = 5;

    /// <summary>Joined-conversation budget per connection.</summary>
    public const int MaxJoinedPerConnection = 20;

    public override async Task OnConnectedAsync()
    {
        var session = await CurrentActiveSessionAsync();

        if (session is null ||
            !rateGate.TryAcquireNegotiate(session.Value.UserId, out _) ||
            registry.CountConnections(session.Value.UserId) >= MaxConnectionsPerUser)
        {
            Context.Abort();
            return;
        }

        // Verified session from the JWT sid claim, never a client-selected
        // session header. Multi-device isolation follows from distinct
        // session ids per device.
        registry.AddConnection(
            session.Value.UserId,
            Context.ConnectionId,
            session.Value.SessionId);
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
        var session = await CurrentActiveSessionAsync();
        var conversation = new ConversationId(conversationId);

        if (session is null ||
            !await IsEligibleAsync(conversation, session.Value.UserId))
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

        UserId userId = session.Value.UserId;
        UserId counterpart = audienceState is not null &&
            audienceState.ParticipantOne == userId
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
        var session = await CurrentActiveSessionAsync();
        var conversation = new ConversationId(conversationId);

        registry.Leave(Context.ConnectionId, conversation);

        if (session is not null)
        {
            typing.ClearUser(session.Value.UserId, conversation);
            await publisher.PublishTypingChangedAsync(
                conversationId,
                session.Value.UserId.Value,
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
        var session = await CurrentActiveSessionAsync();
        var conversation = new ConversationId(conversationId);

        if (session is null ||
            !registry.GetJoined(Context.ConnectionId).Contains(conversation) ||
            !await IsEligibleAsync(conversation, session.Value.UserId))
        {
            return;
        }

        if (!typing.Refresh(
                session.Value.UserId, conversation, Context.ConnectionId,
                isTyping, DateTime.UtcNow, out DateTime expiresOnUtc))
        {
            return;
        }

        await publisher.PublishTypingChangedAsync(
            conversationId,
            session.Value.UserId.Value,
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
        var session = await CurrentActiveSessionAsync();

        if (session is null)
        {
            Context.Abort();
            return;
        }

        if (!presence.Heartbeat(session.Value.UserId, Context.ConnectionId, DateTime.UtcNow))
        {
            return;
        }

        await BroadcastPresenceAsync(session.Value.UserId);
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

    private async Task<(UserId UserId, DeviceSessionId SessionId)?> CurrentActiveSessionAsync()
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

        // Verified session from the JWT sid claim. When the session
        // validator is wired (production), a missing/inactive session
        // evicts joins/hints and aborts: logout-current/revoke-one affect
        // only that device, while logout-all/reset/reuse/disable fail every
        // session. Isolated hub tests omit the validator and keep the
        // user-only behavior.
        string? sid = Context.User?.FindFirst(
            Ahlena.Modules.Identity.Application.Authentication.Tokens.AuthClaimNames.SessionId)?.Value;

        if (sessionValidator is null)
        {
            DeviceSessionId? stored = null;

            try
            {
                stored = registry.GetSession(Context.ConnectionId);
            }
            catch (Exception)
            {
                stored = null;
            }

            DeviceSessionId effective = stored ?? DeviceSessionId.Empty;

            if (Guid.TryParse(sid, out Guid claimSession) && claimSession != Guid.Empty)
            {
                effective = new DeviceSessionId(claimSession);
            }

            return (userId, effective);
        }

        if (!Guid.TryParse(sid, out Guid sessionGuid) || sessionGuid == Guid.Empty)
        {
            EvictConnection();
            return null;
        }

        var sessionId = new DeviceSessionId(sessionGuid);

        bool active;

        try
        {
            active = await sessionValidator.IsSessionActiveAsync(
                userId, sessionId, Context.ConnectionAborted);
        }
        catch (Exception)
        {
            EvictConnection();
            return null;
        }

        if (!active)
        {
            // Stop dispatch to revoked sockets: evict joins/hints now; the
            // socket aborts here and also closes on token expiry (CloseOn
            // AuthenticationExpiration stays enabled). Bounded invalidation:
            // hub calls fail immediately, realtime dispatch re-resolves per
            // 5s cycle and drops revoked connections.
            registry.EvictSession(sessionId);
            foreach (var cleared in typing.ClearConnection(Context.ConnectionId))
            {
                await publisher.PublishTypingChangedAsync(
                    cleared.ConversationId.Value,
                    cleared.UserId.Value,
                    false,
                    DateTime.UtcNow,
                    CancellationToken.None);
            }

            EvictConnection();
            return null;
        }

        return (userId, sessionId);
    }

    private async Task<UserId?> CurrentActiveUserIdAsync()
    {
        var session = await CurrentActiveSessionAsync();
        return session?.UserId;
    }

    private void EvictConnection()
    {
        registry.RemoveConnection(Context.ConnectionId, out _);
        registry.LeaveAll(Context.ConnectionId);
        typing.ClearConnection(Context.ConnectionId);
        presence.RemoveConnection(Context.ConnectionId, DateTime.UtcNow);
        Context.Abort();
    }
}
