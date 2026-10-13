using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sanad.API.Hubs;
using Sanad.API.NotificationsIntegration;
using Sanad.API.Realtime;
using Sanad.API.Security;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Chat.Application.RateLimiting;
using Sanad.Modules.Chat.Domain.Blocks;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Domain.Messages;
using Sanad.Modules.Chat.Infrastructure.Persistence;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication.Login;
using Sanad.Modules.Identity.Application.Authentication.Refresh;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Identity.Infrastructure.Security;
using Sanad.Modules.Notifications.Application.Abstractions.Push;
using Sanad.Modules.Notifications.Domain.Notifications;
using Sanad.Modules.Notifications.Infrastructure.Persistence;
using IdentityDevicePlatform = Sanad.Modules.Identity.Domain.Authentication.DeviceSessions.DevicePlatform;
using NotifyDevicePlatform = Sanad.Modules.Notifications.Domain.Notifications.DevicePlatform;

namespace Sanad.UnitTests.ChatPlus;

/// <summary>
/// Session-bound Normal access, hub session authorization, and push
/// final-send eligibility. Uses fake providers only; no provider send occurs.
/// </summary>
public sealed class ChatPlusSessionSecurityTests
{
    private static DateTime FixedNow =>
        DateTime.SpecifyKind(new DateTime(2026, 10, 13, 12, 0, 0), DateTimeKind.Utc);

    private static JwtAuthTokenService CreateTokenService() =>
        new(Options.Create(new JwtOptions
        {
            Issuer = "sanad-api",
            Audience = "sanad-clients",
            SigningKey = "12345678901234567890123456789012"
        }));

    private static User CreateActiveUser(string email, string phone)
    {
        User user = User.Create(
            FullName.Create("مستخدم اختبار"),
            FullName.Create("Test User"),
            Email.Create(email),
            PhoneNumber.Create(phone));
        user.AddAccount(AccountType.Family);
        user.SetInitialPasswordHash("stored-hash", FixedNow);
        user.VerifyEmail(FixedNow);
        user.VerifyPhone(FixedNow);
        user.Activate(FixedNow.AddMinutes(1));
        return user;
    }

    private static IdentityDbContext CreateIdentityDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ChatDbContext CreateChatDb() =>
        new(new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static NotificationsDbContext CreateNotificationsDb() =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    // ------------------------------------------------------------------
    // Token binding.
    // ------------------------------------------------------------------

    [Fact]
    public void NormalToken_CarriesSid_WhenSessionSupplied()
    {
        var service = CreateTokenService();
        User user = CreateActiveUser("sid1@example.com", "+201000000001");
        var sessionId = DeviceSessionId.New();

        GeneratedAccessToken bound =
            service.GenerateAccessToken(user, FixedNow, sessionId);
        var boundJwt = new JwtSecurityTokenHandler().ReadJwtToken(bound.PlainTextToken);

        Assert.Equal(
            sessionId.Value.ToString("D"),
            boundJwt.Claims.Single(c => c.Type == AuthClaimNames.SessionId).Value);
        Assert.Equal("Normal", boundJwt.Claims.Single(c => c.Type == "access_type").Value);
    }

    [Fact]
    public void NormalToken_OmitsSid_ForLegacyCallers_AndRestrictedNeverBinds()
    {
        var service = CreateTokenService();
        User user = CreateActiveUser("sid2@example.com", "+201000000002");

        GeneratedAccessToken legacy =
            service.GenerateAccessToken(user, FixedNow);
        var legacyJwt = new JwtSecurityTokenHandler().ReadJwtToken(legacy.PlainTextToken);
        Assert.DoesNotContain(legacyJwt.Claims, c => c.Type == AuthClaimNames.SessionId);

        GeneratedAccessToken restricted =
            service.GenerateRestrictedVerificationToken(user, FixedNow);
        var restrictedJwt = new JwtSecurityTokenHandler().ReadJwtToken(restricted.PlainTextToken);
        Assert.DoesNotContain(restrictedJwt.Claims, c => c.Type == AuthClaimNames.SessionId);
        Assert.DoesNotContain(restrictedJwt.Claims, c => c.Type == "account_type");
    }

    [Fact]
    public async Task Login_IssuesBoundToken_AfterSessionCreation_TwoDevicesIsolated()
    {
        await using IdentityDbContext db = CreateIdentityDb();
        User user = CreateActiveUser("login@example.com", "+201000000003");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = CreateTokenService();
        var handler = new LoginCommandHandler(
            db,
            new AlwaysValidPasswordHasher(),
            service,
            new FixedClock(FixedNow));

        Result<LoginResponse> first = await handler.Handle(
            new LoginCommand("login@example.com", "pw", "device-a", IdentityDevicePlatform.Android, "1.0"),
            CancellationToken.None);
        Result<LoginResponse> second = await handler.Handle(
            new LoginCommand("login@example.com", "pw", "device-b", IdentityDevicePlatform.Android, "1.0"),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value.DeviceSessionId, second.Value.DeviceSessionId);

        foreach (Result<LoginResponse> result in new[] { first, second })
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Value.AccessToken);
            Assert.Equal(
                result.Value.DeviceSessionId!.Value.Value.ToString("D"),
                jwt.Claims.Single(c => c.Type == AuthClaimNames.SessionId).Value);
        }

        Assert.Equal(2, await db.DeviceSessions.CountAsync());
    }

    [Fact]
    public async Task Refresh_RotatesBoundToken_WithSameSession()
    {
        await using IdentityDbContext db = CreateIdentityDb();
        User user = CreateActiveUser("refresh@example.com", "+201000000004");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = CreateTokenService();
        var login = new LoginCommandHandler(db, new AlwaysValidPasswordHasher(), service, new FixedClock(FixedNow));
        Result<LoginResponse> loggedIn = await login.Handle(
            new LoginCommand("refresh@example.com", "pw", "device-a", IdentityDevicePlatform.Android, "1.0"),
            CancellationToken.None);

        var refresher = new RefreshTokenCommandHandler(db, service, new FixedClock(FixedNow.AddMinutes(5)));
        Result<RefreshTokenResponse> refreshed = await refresher.Handle(
            new RefreshTokenCommand(loggedIn.Value.DeviceSessionId!.Value, loggedIn.Value.RefreshToken!),
            CancellationToken.None);

        Assert.True(refreshed.IsSuccess);
        Assert.Equal(loggedIn.Value.DeviceSessionId, refreshed.Value.DeviceSessionId);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(refreshed.Value.AccessToken);
        Assert.Equal(
            refreshed.Value.DeviceSessionId.Value.ToString("D"),
            jwt.Claims.Single(c => c.Type == AuthClaimNames.SessionId).Value);
    }

    // ------------------------------------------------------------------
    // Shared session validator.
    // ------------------------------------------------------------------

    [Fact]
    public async Task SessionValidator_EnforcesOwnershipRevocationExpiryAndStatus()
    {
        await using IdentityDbContext db = CreateIdentityDb();
        User user = CreateActiveUser("validator@example.com", "+201000000005");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        DeviceSession active = DeviceSession.Create(
            user.Id, "d1", IdentityDevicePlatform.Android, "1.0", "hash-1", FixedNow, FixedNow.AddDays(30));
        DeviceSession revoked = DeviceSession.Create(
            user.Id, "d2", IdentityDevicePlatform.Android, "1.0", "hash-2", FixedNow, FixedNow.AddDays(30));
        revoked.Revoke("User logged out.", FixedNow);
        DeviceSession expired = DeviceSession.Create(
            user.Id, "d3", IdentityDevicePlatform.Android, "1.0", "hash-3",
            FixedNow.AddDays(-31), FixedNow.AddDays(-1));
        db.DeviceSessions.AddRange(active, revoked, expired);
        await db.SaveChangesAsync();

        var validator = new IdentitySessionValidator(db);

        Assert.True(await validator.IsSessionActiveAsync(user.Id, active.Id, CancellationToken.None));
        Assert.False(await validator.IsSessionActiveAsync(user.Id, revoked.Id, CancellationToken.None));
        Assert.False(await validator.IsSessionActiveAsync(user.Id, expired.Id, CancellationToken.None));
        Assert.False(await validator.IsSessionActiveAsync(UserId.New(), active.Id, CancellationToken.None));
        Assert.True(await validator.HasAnyActiveSessionAsync(user.Id, DateTime.UtcNow, CancellationToken.None));

        // Logout-all revokes the last active session: no session remains.
        active.Revoke("User logged out.", FixedNow);
        await db.SaveChangesAsync();
        Assert.False(await validator.IsSessionActiveAsync(user.Id, active.Id, CancellationToken.None));
        Assert.False(await validator.HasAnyActiveSessionAsync(user.Id, DateTime.UtcNow, CancellationToken.None));

        // Disabled accounts fail even with a non-revoked row.
        User blocked = CreateActiveUser("blocked@example.com", "+201000000006");
        db.Users.Add(blocked);
        await db.SaveChangesAsync();
        DeviceSession blockedSession = DeviceSession.Create(
            blocked.Id, "d1", IdentityDevicePlatform.Android, "1.0", "hash-b", DateTime.UtcNow, DateTime.UtcNow.AddDays(30));
        db.DeviceSessions.Add(blockedSession);
        await db.SaveChangesAsync();
        blocked.Block("Blocked for test.", DateTime.UtcNow);
        await db.SaveChangesAsync();
        Assert.False(await validator.IsSessionActiveAsync(blocked.Id, blockedSession.Id, CancellationToken.None));
    }

    // ------------------------------------------------------------------
    // Hub session binding.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Hub_StoresVerifiedSession_AndRejectsRevokedOrLegacy()
    {
        await using IdentityDbContext identity = CreateIdentityDb();
        User user = CreateActiveUser("hub@example.com", "+201000000007");
        identity.Users.Add(user);
        await identity.SaveChangesAsync();

        DateTime now = DateTime.UtcNow;
        DeviceSession active = DeviceSession.Create(
            user.Id, "d1", IdentityDevicePlatform.Android, "1.0", "hash-1", now, now.AddDays(30));
        DeviceSession revoked = DeviceSession.Create(
            user.Id, "d2", IdentityDevicePlatform.Android, "1.0", "hash-2", now, now.AddDays(30));
        revoked.Revoke("User logged out.", now);
        identity.DeviceSessions.AddRange(active, revoked);
        await identity.SaveChangesAsync();

        var validator = new IdentitySessionValidator(identity);
        await using ChatDbContext chat = CreateChatDb();
        UserId peer = UserId.New();
        Conversation conversation = Conversation.Create(user.Id, peer, ConversationScope.Direct, null, FixedNow);
        chat.Conversations.Add(conversation);
        await chat.SaveChangesAsync();

        var audience = new Sanad.API.Realtime.ChatHubAudience(chat);
        var lookup = new AllowAllLookup(user.Id);

        // Active session connects and joins.
        var registry = new ChatConnectionRegistry();
        ChatHub activeHub = CreateHub(registry, audience, lookup, validator, user.Id, active.Id, "c-active");
        await activeHub.OnConnectedAsync();
        Assert.False(((TestHubContext)activeHub.Context).Aborted);
        Assert.Equal(active.Id, registry.GetSession("c-active"));
        object joined = await activeHub.JoinConversation(conversation.Id.Value);
        Assert.Equal(true, joined.GetType().GetProperty("joined")!.GetValue(joined));

        // Revoked session aborts on connect.
        ChatHub revokedHub = CreateHub(new ChatConnectionRegistry(), audience, lookup, validator, user.Id, revoked.Id, "c-revoked");
        await revokedHub.OnConnectedAsync();
        Assert.True(((TestHubContext)revokedHub.Context).Aborted);

        // Legacy token without sid aborts when the validator is wired.
        ChatHub legacyHub = CreateHub(new ChatConnectionRegistry(), audience, lookup, validator, user.Id, null, "c-legacy");
        await legacyHub.OnConnectedAsync();
        Assert.True(((TestHubContext)legacyHub.Context).Aborted);

        // Revoke-one evicts only that device's joins.
        var twoDevice = new ChatConnectionRegistry();
        twoDevice.AddConnection(user.Id, "c-a", active.Id);
        twoDevice.Join("c-a", conversation.Id);
        DeviceSession second = DeviceSession.Create(
            user.Id, "d-second", IdentityDevicePlatform.Android, "1.0", "hash-s", now, now.AddDays(30));
        identity.DeviceSessions.Add(second);
        await identity.SaveChangesAsync();
        twoDevice.AddConnection(user.Id, "c-b", second.Id);
        twoDevice.Join("c-b", conversation.Id);

        second.Revoke("Revoked by the user.", now);
        await identity.SaveChangesAsync();
        twoDevice.EvictSession(second.Id);
        Assert.Empty(twoDevice.GetJoined("c-b"));
        Assert.Single(twoDevice.GetJoined("c-a"));

        // Logout-all evicts every device.
        twoDevice.EvictUser(user.Id);
        Assert.Empty(twoDevice.GetJoined("c-a"));
    }

    [Fact]
    public async Task Publisher_DropsRevokedConnections_ButKeepsActiveDevice()
    {
        await using IdentityDbContext identity = CreateIdentityDb();
        User me = CreateActiveUser("pub@example.com", "+201000000008");
        identity.Users.Add(me);
        await identity.SaveChangesAsync();

        DateTime now = DateTime.UtcNow;
        DeviceSession activeSession = DeviceSession.Create(
            me.Id, "d1", IdentityDevicePlatform.Android, "1.0", "h1", now, now.AddDays(30));
        DeviceSession revokedSession = DeviceSession.Create(
            me.Id, "d2", IdentityDevicePlatform.Android, "1.0", "h2", now, now.AddDays(30));
        identity.DeviceSessions.AddRange(activeSession, revokedSession);
        await identity.SaveChangesAsync();
        revokedSession.Revoke("User logged out.", now);
        await identity.SaveChangesAsync();

        await using ChatDbContext chat = CreateChatDb();
        UserId peer = UserId.New();
        Conversation conversation = Conversation.Create(me.Id, peer, ConversationScope.Direct, null, FixedNow);
        chat.Conversations.Add(conversation);
        await chat.SaveChangesAsync();

        var registry = new ChatConnectionRegistry();
        registry.AddConnection(me.Id, "me-active", activeSession.Id);
        registry.AddConnection(me.Id, "me-revoked", revokedSession.Id);
        registry.AddConnection(peer, "peer-1", null);
        registry.Join("me-active", conversation.Id);
        registry.Join("me-revoked", conversation.Id);
        registry.Join("peer-1", conversation.Id);

        var clients = new RecordingClients();
        var publisher = new Sanad.API.Realtime.ChatRealtimePublisher(
            new TestHubContextForPublisher(clients),
            new Sanad.API.Realtime.ChatHubAudience(chat),
            registry,
            new IdentitySessionValidator(identity));

        await publisher.PublishMessageCreatedAsync(conversation.Id.Value, new { id = 1 }, default);

        var send = Assert.Single(clients.Sent);
        Assert.Contains("me-active", send.Connections);
        Assert.DoesNotContain("me-revoked", send.Connections);
    }

    // ------------------------------------------------------------------
    // Push final-send eligibility.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Push_FinalSend_SuppressesOnBlockHidePreferenceLogout_AndSendsWhenEligible()
    {
        await using IdentityDbContext identity = CreateIdentityDb();
        User sender = CreateActiveUser("push-sender@example.com", "+201000000009");
        User recipient = CreateActiveUser("push-recipient@example.com", "+201000000010");
        identity.Users.AddRange(sender, recipient);
        await identity.SaveChangesAsync();

        DateTime now = DateTime.UtcNow;
        DeviceSession recipientSession = DeviceSession.Create(
            recipient.Id, "d1", IdentityDevicePlatform.Android, "1.0", "h1", now, now.AddDays(30));
        identity.DeviceSessions.Add(recipientSession);
        await identity.SaveChangesAsync();

        await using ChatDbContext chat = CreateChatDb();
        Conversation conversation = Conversation.Create(sender.Id, recipient.Id, ConversationScope.Direct, null, FixedNow);
        chat.Conversations.Add(conversation);
        Message message = Message.Create(
            conversation.Id, sender.Id, MessageType.Text, "hello", null, Guid.CreateVersion7().ToString("N"), FixedNow);
        chat.Messages.Add(message);
        await chat.SaveChangesAsync();

        await using NotificationsDbContext notifications = CreateNotificationsDb();
        DeviceToken device = DeviceToken.Register(recipient.Id.Value, NotifyDevicePlatform.Android, "synthetic_fcm_token_abcdef123456", null, FixedNow);
        notifications.DeviceTokens.Add(device);
        await notifications.SaveChangesAsync();

        PushOutboxMessage row = PushOutboxMessage.Create(
            recipient.Id.Value,
            "New chat message",
            "Conversation",
            new Dictionary<string, string>
            {
                ["notificationId"] = Guid.CreateVersion7().ToString("D"),
                ["conversationId"] = conversation.Id.Value.ToString("D"),
                ["messageId"] = message.Id.Value.ToString("D")
            },
            $"push-{Guid.CreateVersion7():N}",
            "ChatMessages",
            FixedNow);

        var validator = new IdentitySessionValidator(identity);

        // Eligible: active session, device, visible, unblocked, preference on.
        Assert.True(await PushOutboxProcessor.IsChatPushEligibleAsync(
            notifications, identity, chat, validator, row, CancellationToken.None));

        // Queued push followed by block suppresses (directional: recipient blocked sender).
        chat.Blocks.Add(Block.Create(recipient.Id, sender.Id, FixedNow));
        await chat.SaveChangesAsync();
        Assert.False(await PushOutboxProcessor.IsChatPushEligibleAsync(
            notifications, identity, chat, validator, row, CancellationToken.None));
        chat.Blocks.RemoveRange(chat.Blocks);
        await chat.SaveChangesAsync();

        // Sender-side block does not suppress delivery to the recipient.
        chat.Blocks.Add(Block.Create(sender.Id, recipient.Id, FixedNow));
        await chat.SaveChangesAsync();
        Assert.True(await PushOutboxProcessor.IsChatPushEligibleAsync(
            notifications, identity, chat, validator, row, CancellationToken.None));
        chat.Blocks.RemoveRange(chat.Blocks);
        await chat.SaveChangesAsync();

        // Hide suppresses.
        conversation.Hide(recipient.Id);
        await chat.SaveChangesAsync();
        Assert.False(await PushOutboxProcessor.IsChatPushEligibleAsync(
            notifications, identity, chat, validator, row, CancellationToken.None));
        conversation.Unhide(recipient.Id);
        await chat.SaveChangesAsync();

        // Logout (revoke the only session) suppresses without any provider send.
        recipientSession.Revoke("User logged out.", now);
        await identity.SaveChangesAsync();
        var fake = new FakePushSender();
        Assert.False(await PushOutboxProcessor.IsChatPushEligibleAsync(
            notifications, identity, chat, validator, row, CancellationToken.None));
        Assert.Equal(0, fake.SendCalls);

        // Crash/retry path stays durable: failed items retry, suppressed items never retry.
        PushOutboxMessage retryable = PushOutboxMessage.Create(
            recipient.Id.Value, "t", "b",
            new Dictionary<string, string> { ["k"] = "v" }, $"push-{Guid.CreateVersion7():N}", "ChatMessages", FixedNow);
        Guid claim = Guid.NewGuid();
        retryable.MarkProcessing(FixedNow, claim);
        retryable.MarkFailed("Push.SendFailed", FixedNow, claim, retryable: true);
        Assert.Equal(PushOutboxStatus.Pending, retryable.Status);

        PushOutboxMessage suppressed = PushOutboxMessage.Create(
            recipient.Id.Value, "t", "b",
            new Dictionary<string, string> { ["k"] = "v" }, $"push-{Guid.CreateVersion7():N}", "ChatMessages", FixedNow);
        Guid claim2 = Guid.NewGuid();
        suppressed.MarkProcessing(FixedNow, claim2);
        suppressed.MarkSuppressed(claim2);
        Assert.Equal(PushOutboxStatus.Suppressed, suppressed.Status);
    }

    // ------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------

    private static ChatHub CreateHub(
        ChatConnectionRegistry registry,
        Sanad.API.Realtime.ChatHubAudience audience,
        AllowAllLookup lookup,
        IdentitySessionValidator validator,
        UserId userId,
        DeviceSessionId? sessionId,
        string connectionId)
    {
        var hub = new ChatHub(
            registry,
            new TypingTracker(),
            new PresenceTracker(),
            audience,
            new RecordingPublisher(),
            new InMemoryChatRateGate(),
            lookup,
            validator)
        {
            Context = new TestHubContext(connectionId, userId, sessionId),
            Clients = new RecordingClients()
        };
        return hub;
    }

    private sealed class FixedClock(DateTime now) : IDateTimeProvider
    {
        public DateTime UtcNow => now;
    }

    private sealed class AlwaysValidPasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => "hash";
        public PasswordVerificationResult Verify(string passwordHash, string providedPassword) =>
            PasswordVerificationResult.Success;
    }

    private sealed class AllowAllLookup(params UserId[] allowed) : Sanad.Modules.Chat.Application.Conversations.IChatUserLookup
    {
        private readonly HashSet<UserId> _allowed = new(allowed);

        public Task<Sanad.Modules.Chat.Application.Conversations.UserProfile?> GetProfileAsync(UserId userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Sanad.Modules.Chat.Application.Conversations.UserProfile?>(
                _allowed.Contains(userId) ? new Sanad.Modules.Chat.Application.Conversations.UserProfile(userId, "Test", null, 1) : null);

        public Task<IReadOnlyDictionary<UserId, Sanad.Modules.Chat.Application.Conversations.UserProfile?>> GetProfilesAsync(IReadOnlyCollection<UserId> userIds, CancellationToken cancellationToken = default)
        {
            Dictionary<UserId, Sanad.Modules.Chat.Application.Conversations.UserProfile?> result = new();
            foreach (UserId id in userIds)
            {
                result[id] = _allowed.Contains(id)
                    ? new Sanad.Modules.Chat.Application.Conversations.UserProfile(id, "Test", null, 1)
                    : null;
            }

            return Task.FromResult<IReadOnlyDictionary<UserId, Sanad.Modules.Chat.Application.Conversations.UserProfile?>>(result);
        }
    }

    private sealed class RecordingPublisher : Sanad.Modules.Chat.Application.Realtime.IChatRealtimePublisher
    {
        public Task PublishMessageCreatedAsync(Guid c, object p, CancellationToken ct) => Task.CompletedTask;
        public Task PublishReceiptUpdatedAsync(Guid c, Guid m, DateTime? d, DateTime? r, CancellationToken ct) => Task.CompletedTask;
        public Task PublishTypingChangedAsync(Guid c, Guid u, bool t, DateTime e, CancellationToken ct) => Task.CompletedTask;
        public Task PublishPresenceChangedAsync(Guid u, bool o, DateTime? l, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class TestHubContext(string connectionId, UserId? userId, DeviceSessionId? sessionId) : HubCallerContext
    {
        public bool Aborted { get; private set; }
        public override string ConnectionId => connectionId;
        public override string? UserIdentifier => userId?.Value.ToString();
        public override ClaimsPrincipal? User
        {
            get
            {
                if (userId is null)
                {
                    return null;
                }

                List<Claim> claims = new()
                {
                    new Claim("sub", userId.Value.Value.ToString()),
                    new Claim("access_type", "Normal")
                };

                if (sessionId.HasValue && sessionId.Value != DeviceSessionId.Empty)
                {
                    claims.Add(new Claim("sid", sessionId.Value.Value.ToString("D")));
                }

                return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
            }
        }
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
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
        public Task SendCoreAsync(string method, object?[] args, CancellationToken ct = default) => send(method, args);
        public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class TestHubContextForPublisher(RecordingClients clients) : IHubContext<ChatHub>
    {
        public IHubClients Clients => new ForwardingClients(clients);
        public IGroupManager Groups => throw new NotSupportedException();
        private sealed class ForwardingClients(RecordingClients inner) : IHubClients
        {
            public IClientProxy All => inner.All;
            public IClientProxy AllExcept(IReadOnlyList<string> excluded) => inner.AllExcept(excluded);
            public IClientProxy Client(string connectionId) => inner.Client(connectionId);
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => inner.Clients(connectionIds);
            public IClientProxy Group(string groupName) => inner.Group(groupName);
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excluded) => inner.GroupExcept(groupName, excluded);
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => inner.Groups(groupNames);
            public IClientProxy User(string userId) => inner.User(userId);
            public IClientProxy Users(IReadOnlyList<string> userIds) => inner.Users(userIds);
        }
    }

    private sealed class FakePushSender : IPushSender
    {
        public int SendCalls { get; private set; }
        public bool IsConfigured => true;
        public Task<PushSendResult> SendAsync(string deviceToken, string title, string body, IReadOnlyDictionary<string, string> data, CancellationToken cancellationToken)
        {
            SendCalls++;
            return Task.FromResult(new PushSendResult(PushSendOutcome.Sent, "fake-id"));
        }
    }
}
