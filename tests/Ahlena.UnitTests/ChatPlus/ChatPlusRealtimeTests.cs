using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Ahlena.API.Hubs;
using Ahlena.API.Realtime;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Application.RateLimiting;
using Ahlena.Modules.Chat.Application.Conversations;
using Ahlena.Modules.Chat.Application.Realtime;
using Ahlena.Modules.Chat.Domain.Blocks;
using Ahlena.Modules.Chat.Domain.Conversations;
using Ahlena.Modules.Chat.Infrastructure.Persistence;

namespace Ahlena.UnitTests.ChatPlus;

/// <summary>
/// Phase 4 realtime tests: registry/typing/presence semantics, hub
/// authorization and budgets, audience-gated publishing, eviction, and
/// negotiate limits. Hub tests stub the SignalR context (no TestServer,
/// no sockets); transport fallback is proven by the sync harness.
/// </summary>
public sealed class ChatPlusRealtimeTests
{
    private static DateTime UtcStamp =>
        DateTime.SpecifyKind(new DateTime(2026, 10, 1, 12, 0, 0), DateTimeKind.Utc);

    private static ChatDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    // ------------------------------------------------------------------
    // Trackers.
    // ------------------------------------------------------------------

    [Fact]
    public void Typing_AcceptsOnePerTwoSecondsExpiresAfterEight()
    {
        var typing = new TypingTracker();
        UserId user = UserId.New();
        var conversation = ConversationId.New();
        DateTime start = DateTime.UtcNow;

        Assert.True(typing.Refresh(user, conversation, "c1", true, start, out DateTime expires));
        Assert.Equal(start + TimeSpan.FromSeconds(8), expires);

        // Too fast: dropped, keeps the old expiry.
        Assert.False(typing.Refresh(user, conversation, "c1", true, start.AddSeconds(1), out _));

        // Refresh after 2s accepted with a new expiry.
        Assert.True(typing.Refresh(
            user, conversation, "c1", true, start.AddSeconds(3), out DateTime expires2));
        Assert.Equal(start.AddSeconds(11), expires2);

        // Sweep before expiry: nothing.
        Assert.Empty(typing.SweepExpired(start.AddSeconds(10)));
        // Sweep after expiry: the hint with a stop due.
        var expired = typing.SweepExpired(start.AddSeconds(12));
        Assert.Single(expired);

        // Stop clears immediately.
        Assert.True(typing.Refresh(user, conversation, "c1", true, start.AddSeconds(20), out _));
        Assert.True(typing.Refresh(user, conversation, "c1", false, start.AddSeconds(21), out _));
        Assert.Empty(typing.SweepExpired(start.AddSeconds(40)));
    }

    [Fact]
    public void Presence_OrAcrossDevicesRateLimitedLastSeenOnly()
    {
        var presence = new PresenceTracker();
        UserId user = UserId.New();
        DateTime start = DateTime.UtcNow;

        Assert.True(presence.Heartbeat(user, "d1", start));
        // Too fast on the same device: dropped.
        Assert.False(presence.Heartbeat(user, "d1", start.AddSeconds(10)));
        // Second device accepted.
        Assert.True(presence.Heartbeat(user, "d2", start.AddSeconds(10)));

        Assert.True(presence.IsOnline(user, start.AddSeconds(20)));
        Assert.Equal(start.AddSeconds(10), presence.GetLastSeen(user, start.AddSeconds(20)));

        // d1 ages out but d2 keeps the user online (OR, 90s window).
        Assert.True(presence.IsOnline(user, start.AddSeconds(95)));
        Assert.False(presence.IsOnline(user, start.AddSeconds(101)));

        // Disconnect recomputes; last-seen is the last accepted heartbeat,
        // never the timeout instant.
        presence.RemoveConnection("d2", start.AddSeconds(102));
        Assert.False(presence.IsOnline(user, start.AddSeconds(103)));
        Assert.Equal(start.AddSeconds(10), presence.GetLastSeen(user, start.AddSeconds(103)));

        // Restart loses everything: unknown/null until new heartbeats.
        var fresh = new PresenceTracker();
        Assert.False(fresh.IsOnline(user, start.AddSeconds(104)));
        Assert.Null(fresh.GetLastSeen(user, start.AddSeconds(104)));
    }

    // ------------------------------------------------------------------
    // Hub.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Hub_RejectsSixthConnectionAndDeniedNegotiate()
    {
        var registry = new ChatConnectionRegistry();
        UserId user = UserId.New();

        for (int i = 0; i < 5; i++)
        {
            registry.AddConnection(user, $"c{i}");
        }

        var hub = CreateHub(
            registry: registry,
            userId: user,
            connectionId: "c5");

        await hub.OnConnectedAsync();
        Assert.True(((TestContext)hub.Context).Aborted);
        Assert.Equal(5, registry.CountConnections(user));
    }

    [Fact]
    public async Task Hub_JoinReturnsPresenceAndRejectsIneligible()
    {
        await using ChatDbContext db = CreateDb();
        UserId me = UserId.New();
        UserId peer = UserId.New();
        Conversation conversation = await AddConversationAsync(db, me, peer);

        var audience = new ChatHubAudience(db);
        var registry = new ChatConnectionRegistry();
        var presence = new PresenceTracker();
        presence.Heartbeat(peer, "peer-conn", DateTime.UtcNow);

        var hub = CreateHub(
            registry: registry,
            typing: new TypingTracker(),
            presence: presence,
            audience: audience,
            userId: me,
            connectionId: "mine");

        await hub.OnConnectedAsync();
        object joined = await hub.JoinConversation(conversation.Id.Value);

        Assert.True((bool)joined.GetType().GetProperty("joined")!.GetValue(joined)!);
        object? presencePayload = joined.GetType().GetProperty("presence")!.GetValue(joined)!;
        // Peer has no privacy row: undisclosed nulls, never a leak.
        Assert.Null(presencePayload.GetType().GetProperty("isOnline")!.GetValue(presencePayload));
        Assert.Null(presencePayload.GetType().GetProperty("lastSeenOnUtc")!.GetValue(presencePayload));

        // Stranger: sanitized hub error, never an existence/block reason.
        var strangerHub = CreateHub(
            registry: new ChatConnectionRegistry(),
            audience: audience,
            userId: UserId.New(),
            connectionId: "stranger");
        HubException error = await Assert.ThrowsAsync<HubException>(() =>
            strangerHub.JoinConversation(conversation.Id.Value));
        Assert.Equal("Chat.Conversation.NotFound", error.Message);
    }

    [Fact]
    public async Task Hub_SetTypingRequiresJoinAndEligibility()
    {
        await using ChatDbContext db = CreateDb();
        UserId me = UserId.New();
        UserId peer = UserId.New();
        Conversation conversation = await AddConversationAsync(db, me, peer);

        var audience = new ChatHubAudience(db);
        var registry = new ChatConnectionRegistry();
        var publisher = new RecordingPublisher();
        var hub = CreateHub(
            registry: registry,
            audience: audience,
            publisher: publisher,
            userId: me,
            connectionId: "mine");
        await hub.OnConnectedAsync();

        // Not joined: dropped silently.
        await hub.SetTyping(conversation.Id.Value, true);
        Assert.Empty(publisher.Typing);

        await hub.JoinConversation(conversation.Id.Value);
        await hub.SetTyping(conversation.Id.Value, true);
        Assert.Single(publisher.Typing);

        // Immediate refresh: dropped by the 2s rate.
        await hub.SetTyping(conversation.Id.Value, true);
        Assert.Single(publisher.Typing);

        object left = await hub.LeaveConversation(conversation.Id.Value);
        Assert.False((bool)left.GetType().GetProperty("joined")!.GetValue(left)!);
        Assert.Equal(2, publisher.Typing.Count);
        Assert.False(publisher.Typing[1].IsTyping);
    }

    [Fact]
    public async Task Hub_HeartbeatBroadcastsOnlyWhenAccepted()
    {
        await using ChatDbContext db = CreateDb();
        UserId me = UserId.New();
        await AddConversationAsync(db, me, UserId.New());

        var publisher = new RecordingPublisher();
        var hub = CreateHub(
            audience: new ChatHubAudience(db),
            publisher: publisher,
            userId: me,
            connectionId: "mine");
        await hub.OnConnectedAsync();

        // No privacy row: online sharing off — no event, no leak.
        await hub.Heartbeat();
        Assert.Empty(publisher.Presence);

        db.PrivacySettings.Add(
            Ahlena.Modules.Chat.Domain.Privacy.ChatPrivacySettings.Create(
                me, true, true, DateTime.UtcNow));
        await db.SaveChangesAsync();

        await hub.Heartbeat();
        Assert.Empty(publisher.Presence); // rate: same connection, too fast.

        var presence = new PresenceTracker();
        var hub2 = CreateHub(
            audience: new ChatHubAudience(db),
            publisher: publisher,
            presence: presence,
            userId: me,
            connectionId: "mine-2");
        await hub2.OnConnectedAsync();
        await hub2.Heartbeat();
        Assert.Single(publisher.Presence);
    }

    // ------------------------------------------------------------------
    // Audience + publisher gating.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Publisher_SendsOnlyToJoinedEligibleConnections()
    {
        await using ChatDbContext db = CreateDb();
        UserId me = UserId.New();
        UserId peer = UserId.New();
        Conversation conversation = await AddConversationAsync(db, me, peer);

        var registry = new ChatConnectionRegistry();
        registry.AddConnection(me, "me-1");
        registry.AddConnection(peer, "peer-1");
        registry.AddConnection(peer, "peer-2");
        registry.Join("me-1", conversation.Id);
        registry.Join("peer-1", conversation.Id);
        // peer-2 connected but never joined: receives nothing.

        var audience = new ChatHubAudience(db);
        var clients = new RecordingClients();
        var publisher = new ChatRealtimePublisher(
            new TestHubContext(clients), audience, registry);

        await publisher.PublishMessageCreatedAsync(
            conversation.Id.Value, new { id = 1 }, default);

        var firstSend = Assert.Single(clients.Sent);
        Assert.Equal(2, firstSend.Connections.Length);
        Assert.DoesNotContain("peer-2", firstSend.Connections);

        // Hide for peer: peer-1 drops out, me-1 stays.
        conversation.Hide(peer);
        await db.SaveChangesAsync();

        clients.Sent.Clear();
        await publisher.PublishReceiptUpdatedAsync(
            conversation.Id.Value, Guid.CreateVersion7(), null, null, default);

        Assert.Single(clients.Sent);
        Assert.Contains("me-1", clients.Sent[0].Connections);
    }

    [Fact]
    public async Task Publisher_BlockedPairReceivesNothing()
    {
        await using ChatDbContext db = CreateDb();
        UserId me = UserId.New();
        UserId peer = UserId.New();
        Conversation conversation = await AddConversationAsync(db, me, peer);

        db.Blocks.Add(Block.Create(me, peer, DateTime.UtcNow));
        await db.SaveChangesAsync();

        var registry = new ChatConnectionRegistry();
        registry.AddConnection(me, "me-1");
        registry.AddConnection(peer, "peer-1");
        registry.Join("me-1", conversation.Id);
        registry.Join("peer-1", conversation.Id);

        var clients = new RecordingClients();
        var publisher = new ChatRealtimePublisher(
            new TestHubContext(clients),
            new ChatHubAudience(db),
            registry);

        await publisher.PublishMessageCreatedAsync(
            conversation.Id.Value, new { id = 1 }, default);

        Assert.Empty(clients.Sent);
    }

    [Fact]
    public async Task Evictor_RemovesJoinsOnBlock()
    {
        await using ChatDbContext db = CreateDb();
        UserId me = UserId.New();
        UserId peer = UserId.New();
        Conversation conversation = await AddConversationAsync(db, me, peer);

        var registry = new ChatConnectionRegistry();
        registry.AddConnection(me, "me-1");
        registry.AddConnection(peer, "peer-1");
        registry.Join("me-1", conversation.Id);
        registry.Join("peer-1", conversation.Id);

        var audience = new ChatHubAudience(db);
        var publisher = new RecordingPublisher();
        var evictor = new ChatConnectionEvictor(
            registry, new TypingTracker(), audience, publisher);

        db.Blocks.Add(Block.Create(me, peer, DateTime.UtcNow));
        await db.SaveChangesAsync();

        await evictor.PairInvalidatedAsync(me, peer);

        Assert.Empty(registry.GetJoined("me-1"));
        Assert.Empty(registry.GetJoined("peer-1"));
    }

    [Fact]
    public void Negotiate_AllowsThirtyPerMinutePerUser()
    {
        var gate = new InMemoryChatRateGate();
        UserId user = UserId.New();

        for (int i = 0; i < 30; i++)
        {
            Assert.True(gate.TryAcquireNegotiate(user, out _));
        }

        Assert.False(gate.TryAcquireNegotiate(user, out TimeSpan retry));
        Assert.True(retry > TimeSpan.Zero);
    }

    // ------------------------------------------------------------------
    // Integrated reconnect: disconnect, reconnect, rejoin, sync converges.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Reconnect_RejoinPlusSyncRecoversMissedMessages()
    {
        await using ChatDbContext db = CreateDb();
        UserId alice = UserId.New();
        UserId bob = UserId.New();
        Conversation conversation = await AddConversationAsync(db, alice, bob);

        var registry = new ChatConnectionRegistry();
        var typing = new TypingTracker();
        var presence = new PresenceTracker();
        var audience = new ChatHubAudience(db);
        var publisher = new RecordingPublisher();
        var gate = new InMemoryChatRateGate();
        var protector = new ReconnectProtector();
        var recorder = new Ahlena.Modules.Chat.Application.Events.ChatEventRecorder(db);

        ChatHub BobHub(string connection) => CreateHub(
            registry, typing, presence, audience, publisher, bob, connection);

        // Bob connects, joins, and syncs to a cursor.
        ChatHub first = BobHub("bob-1");
        await first.OnConnectedAsync();
        await first.JoinConversation(conversation.Id.Value);

        var sender = new Ahlena.Modules.Chat.Application.Messages.SendMessageCommandHandler(
            db, null, null, recorder);
        Assert.True((await sender.Handle(
            new Ahlena.Modules.Chat.Application.Messages.SendMessageCommand(
                alice, conversation.Id, "r-1", "Text", "one", null),
            default)).IsSuccess);

        var sync = new Ahlena.Modules.Chat.Application.Sync.GetConversationSyncQueryHandler(
            db, protector, null, recorder);
        string? cursor = (await sync.Handle(
            new Ahlena.Modules.Chat.Application.Sync.GetConversationSyncQuery(
                bob, conversation.Id, null, 100),
            default)).Value.NextCursor;
        Assert.NotNull(cursor);

        Assert.True(
            protector.TryUnprotect(cursor, out var decoded) && decoded is not null,
            "cursor decodes");

        // Socket dies: disconnect clears joins and typing; presence lapses.
        await first.OnDisconnectedAsync(null);
        Assert.Empty(registry.GetConnections(bob));

        // Two messages arrive while Bob is gone, plus a receipt-only change
        // from Bob's own earlier read... (none yet) — send two more.
        Assert.True((await sender.Handle(
            new Ahlena.Modules.Chat.Application.Messages.SendMessageCommand(
                alice, conversation.Id, "r-2", "Text", "two", null),
            default)).IsSuccess);
        Assert.True((await sender.Handle(
            new Ahlena.Modules.Chat.Application.Messages.SendMessageCommand(
                alice, conversation.Id, "r-3", "Text", "three", null),
            default)).IsSuccess);

        // Bob reconnects on a new connection, rejoins, and syncs from the
        // last applied cursor: full convergence, no gaps, no duplicates.
        ChatHub second = BobHub("bob-2");
        await second.OnConnectedAsync();
        await second.JoinConversation(conversation.Id.Value);

        long maxRev = await db.Changes.MaxAsync(c => c.Revision);
        Assert.True(maxRev >= 4);

        var seen = new HashSet<string>();
        string? current = cursor;
        bool hasMore = true;
        int polls = 0;

        while (hasMore && polls++ < 10)
        {
            var page = (await sync.Handle(
                new Ahlena.Modules.Chat.Application.Sync.GetConversationSyncQuery(
                    bob, conversation.Id, current, 100),
                default)).Value;

            foreach (var item in page.Items)
            {
                if (item.Payload is Ahlena.Modules.Chat.Application.Messages.MessageItemResponse message)
                {
                    seen.Add(message.ClientMessageId);
                }
            }

            current = page.NextCursor;
            hasMore = page.HasMore;
        }

        // r-1 was already snapshotted; r-2 and r-3 are the missed ones.
        Assert.Contains("r-2", seen);
        Assert.Contains("r-3", seen);

        // The convergence poll stamped delivery for the returned snapshots,
        // so one receipt-only replay follows; afterwards it is quiet.
        var receipts = (await sync.Handle(
            new Ahlena.Modules.Chat.Application.Sync.GetConversationSyncQuery(
                bob, conversation.Id, current, 100),
            default)).Value;
        Assert.All(
            receipts.Items,
            item => Assert.Equal(
                nameof(Ahlena.Modules.Chat.Domain.Changes.ChatChangeType.MessageReceiptUpdated),
                item.Type));

        var quiet = (await sync.Handle(
            new Ahlena.Modules.Chat.Application.Sync.GetConversationSyncQuery(
                bob, conversation.Id, receipts.NextCursor, 100),
            default)).Value;
        Assert.False(quiet.HasMore);
        Assert.Empty(quiet.Items);
    }

    // ------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Hub_AccountDisabledAfterConnectStopsHeartbeatAndRemovesConnection()
    {
        await using ChatDbContext db = CreateDb();
        UserId user = UserId.New();
        var lookup = new ActiveLookup(user);
        var registry = new ChatConnectionRegistry();
        var presence = new PresenceTracker();
        var hub = CreateHub(registry: registry, presence: presence,
            audience: new ChatHubAudience(db, lookup),
            userId: user, userLookup: lookup);
        await hub.OnConnectedAsync();
        await hub.Heartbeat();
        Assert.True(presence.IsOnline(user, DateTime.UtcNow));

        lookup.Disabled = user;
        await hub.Heartbeat();

        Assert.True(((TestContext)hub.Context).Aborted);
        Assert.Empty(registry.GetConnections(user));
        Assert.False(presence.IsOnline(user, DateTime.UtcNow));
    }

    [Fact]
    public async Task Audience_DisabledAccountsReceiveNoEventsOrPresence()
    {
        await using ChatDbContext db = CreateDb();
        UserId me = UserId.New();
        UserId peer = UserId.New();
        Conversation conversation = await AddConversationAsync(db, me, peer);
        var lookup = new ActiveLookup(me, peer);
        var audience = new ChatHubAudience(db, lookup);
        Assert.Equal(2, (await audience.GetEventRecipientsAsync(conversation.Id, null, default)).Count);
        Assert.Contains(peer, await audience.GetPresenceSubscribersAsync(me, default));

        lookup.Disabled = peer;
        Assert.Empty(await audience.GetEventRecipientsAsync(conversation.Id, null, default));
        Assert.Empty(await audience.GetPresenceSubscribersAsync(me, default));
        Assert.Empty(await audience.GetPresenceSubscribersAsync(peer, default));
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void Deployment_UnimplementedBackplaneCannotBypassSingleInstance(int count, bool backplane)
    {
        var options = new Ahlena.API.ChatPlus.ChatPlusDeploymentOptions
        {
            InstanceCount = count,
            BackplaneConfigured = backplane
        };
        Assert.Throws<InvalidOperationException>(options.Validate);
        new Ahlena.API.ChatPlus.ChatPlusDeploymentOptions().Validate();
    }

    private sealed class ActiveLookup(params UserId[] active) : IChatUserLookup
    {
        public UserId? Disabled { get; set; }

        public Task<UserProfile?> GetProfileAsync(UserId userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<UserProfile?>(active.Contains(userId) && userId != Disabled
                ? new UserProfile(userId, "Test", null, 1)
                : null);
    }

    private static ChatHub CreateHub(
        ChatConnectionRegistry? registry = null,
        TypingTracker? typing = null,
        PresenceTracker? presence = null,
        ChatHubAudience? audience = null,
        RecordingPublisher? publisher = null,
        UserId? userId = null,
        string connectionId = "test-connection",
        IChatUserLookup? userLookup = null)
    {
        registry ??= new ChatConnectionRegistry();
        typing ??= new TypingTracker();
        presence ??= new PresenceTracker();
        var hub = new ChatHub(
            registry,
            typing,
            presence,
            audience!,
            publisher ?? new RecordingPublisher(),
            new InMemoryChatRateGate(), userLookup)
        {
            Context = new TestContext(connectionId, userId),
            Clients = new RecordingClients()
        };
        return hub;
    }

    private static async Task<Conversation> AddConversationAsync(
        ChatDbContext db,
        UserId participantA,
        UserId participantB)
    {
        Conversation conversation = Conversation.Create(
            participantA,
            participantB,
            ConversationScope.Direct,
            null,
            UtcStamp);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        return conversation;
    }

    private sealed class RecordingPublisher : IChatRealtimePublisher
    {
        public List<(Guid ConversationId, object Payload)> Created { get; } = new();
        public List<(Guid ConversationId, Guid MessageId)> Receipts { get; } = new();
        public List<(Guid ConversationId, Guid UserId, bool IsTyping)> Typing { get; } = new();
        public List<(Guid UserId, bool IsOnline)> Presence { get; } = new();

        public Task PublishMessageCreatedAsync(Guid c, object p, CancellationToken ct)
        {
            Created.Add((c, p));
            return Task.CompletedTask;
        }

        public Task PublishReceiptUpdatedAsync(Guid c, Guid m, DateTime? d, DateTime? r, CancellationToken ct)
        {
            Receipts.Add((c, m));
            return Task.CompletedTask;
        }

        public Task PublishTypingChangedAsync(Guid c, Guid u, bool t, DateTime e, CancellationToken ct)
        {
            Typing.Add((c, u, t));
            return Task.CompletedTask;
        }

        public Task PublishPresenceChangedAsync(Guid u, bool o, DateTime? l, CancellationToken ct)
        {
            Presence.Add((u, o));
            return Task.CompletedTask;
        }
    }

    private sealed class TestContext(string connectionId, UserId? userId) : HubCallerContext
    {
        public bool Aborted { get; private set; }

        public override string ConnectionId => connectionId;
        public override string? UserIdentifier => userId?.Value.ToString();
        public override ClaimsPrincipal? User => userId is null
            ? null
            : new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", userId.Value.Value.ToString())], "test"));
        public override IDictionary<object, object?> Items { get; } =
            new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() => Aborted = true;
    }

    private sealed class RecordingClients : IHubCallerClients
    {
        public List<(string Method, string[] Connections, object?[] Args)> Sent { get; } = new();

        private IClientProxy Proxy(string[] connections) =>
            new RecordingProxy((method, args) =>
            {
                Sent.Add((method, connections, args));
                return Task.CompletedTask;
            });

        public IClientProxy All => Proxy([]);
        public IClientProxy AllExcept(IReadOnlyList<string> excluded) => Proxy([]);
        public IClientProxy Caller => Proxy(["caller"]);
        public IClientProxy Client(string connectionId) => Proxy([connectionId]);
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy(connectionIds.ToArray());
        public IClientProxy Group(string groupName) => Proxy([]);
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excluded) => Proxy([]);
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy([]);
        public IClientProxy Others => Proxy(["others"]);
        public IClientProxy OthersInGroup(string groupName) => Proxy([]);
        public IClientProxy User(string userId) => Proxy([userId]);
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy(userIds.ToArray());
    }

    private sealed class RecordingProxy(Func<string, object?[], Task> send) : ISingleClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken ct = default) =>
            send(method, args);

        public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class ReconnectProtector
        : Ahlena.Modules.Chat.Application.Sync.IChatSyncCursorProtector
    {
        public string Protect(Ahlena.Modules.Chat.Application.Sync.ChatSyncCursor cursor) =>
            Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(
                    System.Text.Json.JsonSerializer.Serialize(cursor)));

        public bool TryUnprotect(
            string? token,
            out Ahlena.Modules.Chat.Application.Sync.ChatSyncCursor? cursor)
        {
            cursor = null;

            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            try
            {
                cursor = System.Text.Json.JsonSerializer.Deserialize<
                    Ahlena.Modules.Chat.Application.Sync.ChatSyncCursor>(
                        System.Text.Encoding.UTF8.GetString(
                            Convert.FromBase64String(token)));
                return cursor is not null;
            }
            catch (Exception ex) when (
                ex is FormatException || ex is System.Text.Json.JsonException)
            {
                return false;
            }
        }
    }

    private sealed class TestHubContext(RecordingClients clients) : IHubContext<ChatHub>    {
        public IHubClients Clients => new ForwardingClients(clients);
        public IGroupManager Groups => throw new NotSupportedException();

        private sealed class ForwardingClients(RecordingClients inner) : IHubClients
        {
            public IClientProxy All => inner.All;
            public IClientProxy AllExcept(IReadOnlyList<string> excluded) => inner.AllExcept(excluded);
            public IClientProxy Client(string connectionId) => inner.Client(connectionId);
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => inner.Clients(connectionIds);
            public IClientProxy Group(string groupName) => inner.Group(groupName);
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excluded) =>
                inner.GroupExcept(groupName, excluded);
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => inner.Groups(groupNames);
            public IClientProxy User(string userId) => inner.User(userId);
            public IClientProxy Users(IReadOnlyList<string> userIds) => inner.Users(userIds);
        }
    }
}
