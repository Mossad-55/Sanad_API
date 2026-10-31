using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Application.Conversations;
using Ahlena.Modules.Chat.Application.Messages;
using Ahlena.Modules.Chat.Application.Privacy;
using Ahlena.Modules.Chat.Application.RateLimiting;
using Ahlena.Modules.Chat.Application.Reports;
using Ahlena.Modules.Chat.Application.Sync;
using Ahlena.Modules.Chat.Domain.Conversations;
using Ahlena.Modules.Chat.Domain.Messages;
using Ahlena.Modules.Chat.Domain.Reports;
using Ahlena.Modules.Chat.Infrastructure.Persistence;

namespace Ahlena.UnitTests.ChatPlus;

/// <summary>
/// Phase 1 focused tests: eligibility-gated opens with retained-history
/// reopening, sender-bound replay, validated monotonic reads with stable
/// first-read stamps, conditional report resolution, ordered/batched lists,
/// abuse-control gates, privacy settings, and the sync contract.
/// InMemory proves handler logic; provider translation (unique indexes,
/// concurrency tokens, Npgsql ordering) needs the authorized PostgreSQL
/// gate (Phase 7) and is recorded as such — never claimed from these runs.
/// </summary>
public sealed class ChatPlusPhase1Tests
{
    private static DateTime UtcStamp =>
        DateTime.SpecifyKind(new DateTime(2026, 10, 1, 12, 0, 0), DateTimeKind.Utc);

    private static ChatDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    // ------------------------------------------------------------------
    // Open: retained-history reopening vs creation eligibility.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Open_ReopensHiddenConversationOnTerminalBookingWithoutRecheckingEligibility()
    {
        await using ChatDbContext db = CreateDb();
        UserId opener = UserId.New();
        UserId recipient = UserId.New();
        BookingId bookingId = BookingId.New();

        Conversation conversation = Conversation.Create(
            opener, recipient, ConversationScope.Booking, bookingId, UtcStamp);
        conversation.Hide(opener);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();

        // Terminal booking (inactive) that still resolves its parties, and
        // an eligibility checker that would DENY a new conversation.
        var bookings = new FakeBookingGateway();
        bookings.Resolve(bookingId, recipient, isActive: false);
        var handler = new OpenConversationCommandHandler(
            db, new DenyEligibility(), bookings, new FakeUserLookup(recipient));

        Result<OpenConversationResponse> result = await handler.Handle(
            new OpenConversationCommand(opener, null, bookingId), default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Created);
        Assert.Equal(conversation.Id.Value, result.Value.ConversationId);
        Assert.False((await db.Conversations.SingleAsync()).IsHiddenFor(opener));
    }

    [Fact]
    public async Task Open_TerminalBookingWithoutExistingConversationIsPrivacy404()
    {
        await using ChatDbContext db = CreateDb();
        UserId opener = UserId.New();
        UserId recipient = UserId.New();
        BookingId bookingId = BookingId.New();

        var bookings = new FakeBookingGateway();
        bookings.Resolve(bookingId, recipient, isActive: false);
        var handler = new OpenConversationCommandHandler(
            db, new AllowEligibility(), bookings, new FakeUserLookup(recipient));

        Result<OpenConversationResponse> result = await handler.Handle(
            new OpenConversationCommand(opener, null, bookingId), default);

        Assert.True(result.IsFailure);
        Assert.Equal(ConversationErrors.NotFound, result.Error);
        Assert.Equal(0, await db.Conversations.CountAsync());
    }

    [Fact]
    public async Task Open_DirectReopenChecksRecipientStillExists()
    {
        await using ChatDbContext db = CreateDb();
        UserId opener = UserId.New();
        UserId recipient = UserId.New();

        Conversation conversation = Conversation.Create(
            opener, recipient, ConversationScope.Direct, null, UtcStamp);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();

        // Recipient user row gone: reopen denied as privacy 404.
        var handler = new OpenConversationCommandHandler(
            db, new AllowEligibility(), new FakeBookingGateway(), new FakeUserLookup());

        Result<OpenConversationResponse> result = await handler.Handle(
            new OpenConversationCommand(opener, recipient, null), default);

        Assert.True(result.IsFailure);
        Assert.Equal(ConversationErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Open_RateLimitedAfterThirtyOpensPerMinute()
    {
        await using ChatDbContext db = CreateDb();
        var gate = new InMemoryChatRateGate();
        UserId opener = UserId.New();
        var handler = new OpenConversationCommandHandler(
            db, new AllowEligibility(), new FakeBookingGateway(), new FakeUserLookup(),
            gate);

        for (int i = 0; i < 30; i++)
        {
            UserId recipient = UserId.New();
            ((FakeUserLookup)GetLookup(handler)).Add(recipient);
            Result<OpenConversationResponse> result = await handler.Handle(
                new OpenConversationCommand(opener, recipient, null), default);
            Assert.True(result.IsSuccess);
        }

        UserId extra = UserId.New();
        ((FakeUserLookup)GetLookup(handler)).Add(extra);
        Result<OpenConversationResponse> denied = await handler.Handle(
            new OpenConversationCommand(opener, extra, null), default);

        Assert.True(denied.IsFailure);
        Assert.StartsWith("Chat.RateLimited.", denied.Error.Code);
        Assert.NotNull(denied.Error.RetryAfter);
    }

    // ------------------------------------------------------------------
    // Send: sender-bound replay.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Send_OtherParticipantReusingKeyIsConflictNeverReplay()
    {
        await using ChatDbContext db = CreateDb();
        UserId sender = UserId.New();
        UserId peer = UserId.New();
        Conversation conversation = await AddConversationAsync(db, sender, peer);

        var handler = new SendMessageCommandHandler(db);
        Result<SendMessageResponse> first = await handler.Handle(
            new SendMessageCommand(sender, conversation.Id, "key-1", "Text", "hello", null),
            default);
        Assert.True(first.IsSuccess);

        // Same key, same content, but the OTHER participant: 409.
        Result<SendMessageResponse> stolen = await handler.Handle(
            new SendMessageCommand(peer, conversation.Id, "key-1", "Text", "hello", null),
            default);

        Assert.True(stolen.IsFailure);
        Assert.Equal(MessageErrors.IdempotencyConflict, stolen.Error);

        // Same key, same sender, different content: 409.
        Result<SendMessageResponse> conflict = await handler.Handle(
            new SendMessageCommand(sender, conversation.Id, "key-1", "Text", "other", null),
            default);

        Assert.True(conflict.IsFailure);
        Assert.Equal(MessageErrors.IdempotencyConflict, conflict.Error);

        // Exact replay by the owner: 200 with the original id.
        Result<SendMessageResponse> replay = await handler.Handle(
            new SendMessageCommand(sender, conversation.Id, "key-1", "Text", "hello", null),
            default);

        Assert.True(replay.IsSuccess);
        Assert.False(replay.Value.Created);
        Assert.Equal(first.Value.Message.Id, replay.Value.Message.Id);
        Assert.Equal(1, await db.Messages.CountAsync());
    }

    [Fact]
    public async Task Send_RecordsDurableChangeAndIntentAtomically()
    {
        await using ChatDbContext db = CreateDb();
        UserId sender = UserId.New();
        UserId peer = UserId.New();
        Conversation conversation = await AddConversationAsync(db, sender, peer);

        var recorder = new Ahlena.Modules.Chat.Application.Events.ChatEventRecorder(db);
        var handler = new SendMessageCommandHandler(db, null, null, recorder);

        Result<SendMessageResponse> result = await handler.Handle(
            new SendMessageCommand(sender, conversation.Id, "key-d", "Text", "hi", null),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, await db.Changes.CountAsync());
        Assert.Equal(1, await db.Outbox.CountAsync());
        Assert.True((await db.Conversations.SingleAsync()).ChangeRevision >= 1);
    }

    [Fact]
    public async Task Send_ExpiredUnboundAttachmentIsPrivacy404()
    {
        await using ChatDbContext db = CreateDb();
        UserId sender = UserId.New();
        UserId peer = UserId.New();
        Conversation conversation = await AddConversationAsync(db, sender, peer);

        var attachment = Ahlena.Modules.Chat.Domain.Attachments.Attachment.Create(
            conversation.Id,
            sender,
            "private/chat-attachments/old.bin",
            "image/png",
            100,
            DateTime.UtcNow - TimeSpan.FromHours(25));
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync();

        var handler = new SendMessageCommandHandler(db);
        Result<SendMessageResponse> result = await handler.Handle(
            new SendMessageCommand(
                sender, conversation.Id, "key-f", "File", null,
                attachment.Id),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            Ahlena.Modules.Chat.Application.Attachments.AttachmentErrors.NotFound,
            result.Error);
    }

    [Fact]
    public async Task Send_LocationRejectsNonFiniteCoordinates()
    {
        await using ChatDbContext db = CreateDb();
        UserId sender = UserId.New();
        Conversation conversation = await AddConversationAsync(db, sender, UserId.New());

        var handler = new SendMessageCommandHandler(db);
        Result<SendMessageResponse> result = await handler.Handle(
            new SendMessageCommand(
                sender, conversation.Id, "key-l", "Location", null, null,
                double.NaN, 10.0, null),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(MessageErrors.Invalid, result.Error);
    }

    // ------------------------------------------------------------------
    // Read: validation, monotonicity, stable first-read stamps.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Read_UnknownOrForeignMessageIdIsPrivacy404()
    {
        await using ChatDbContext db = CreateDb();
        UserId reader = UserId.New();
        UserId peer = UserId.New();
        Conversation conversation = await AddConversationAsync(db, reader, peer);
        Conversation other = await AddConversationAsync(db, UserId.New(), UserId.New());

        Message foreign = Message.Create(
            other.Id, peer, MessageType.Text, "x", null, "fk", UtcStamp);
        db.Messages.Add(foreign);
        await db.SaveChangesAsync();

        var handler = new MarkConversationReadCommandHandler(db);

        Result unknown = await handler.Handle(
            new MarkConversationReadCommand(reader, conversation.Id, Guid.CreateVersion7()),
            default);
        Result crossConversation = await handler.Handle(
            new MarkConversationReadCommand(reader, conversation.Id, foreign.Id.Value),
            default);

        Assert.Equal(ConversationErrors.NotFound, ((Result)unknown).Error);
        Assert.Equal(ConversationErrors.NotFound, ((Result)crossConversation).Error);
        Assert.Equal(0, await db.ReadStates.CountAsync());
    }

    [Fact]
    public async Task Read_HiddenConversationIsPrivacy404()
    {
        await using ChatDbContext db = CreateDb();
        UserId reader = UserId.New();
        UserId peer = UserId.New();
        Conversation conversation = await AddConversationAsync(db, reader, peer);

        Message message = Message.Create(
            conversation.Id, peer, MessageType.Text, "x", null, "fk", UtcStamp);
        db.Messages.Add(message);
        conversation.Hide(reader);
        await db.SaveChangesAsync();

        var handler = new MarkConversationReadCommandHandler(db);

        Result hidden = await handler.Handle(
            new MarkConversationReadCommand(reader, conversation.Id, message.Id.Value),
            default);

        Assert.Equal(ConversationErrors.NotFound, ((Result)hidden).Error);
        Assert.Equal(0, await db.ReadStates.CountAsync());
    }

    [Fact]
    public async Task Read_OlderMarkSucceedsWithoutRegressionAndKeepsFirstStamps()
    {
        await using ChatDbContext db = CreateDb();
        UserId sender = UserId.New();
        UserId reader = UserId.New();
        Conversation conversation = await AddConversationAsync(db, sender, reader);

        Message first = Message.Create(
            conversation.Id, sender, MessageType.Text, "one", null, "k1", UtcStamp);
        Message second = Message.Create(
            conversation.Id, sender, MessageType.Text, "two", null, "k2",
            UtcStamp.AddMinutes(1));
        db.Messages.AddRange(first, second);
        await db.SaveChangesAsync();

        var recorder = new Ahlena.Modules.Chat.Application.Events.ChatEventRecorder(db);
        var handler = new MarkConversationReadCommandHandler(db, recorder);

        Result newer = await handler.Handle(
            new MarkConversationReadCommand(reader, conversation.Id, second.Id.Value),
            default);
        Assert.True(newer.IsSuccess);

        DateTime? firstStampAfterNewer =
            (await db.Messages.SingleAsync(m => m.Id == first.Id)).ReadOnUtc;
        Assert.NotNull(firstStampAfterNewer);

        // Older mark: success, no regression, no stamp rewrite.
        Result older = await handler.Handle(
            new MarkConversationReadCommand(reader, conversation.Id, first.Id.Value),
            default);
        Assert.True(older.IsSuccess);

        ConversationReadState state = await db.ReadStates.SingleAsync();
        Assert.Equal(second.Id.Value, state.LastReadMessageId);
        Assert.Equal(
            firstStampAfterNewer,
            (await db.Messages.SingleAsync(m => m.Id == first.Id)).ReadOnUtc);
    }

    // ------------------------------------------------------------------
    // Reports: exactly-once resolution.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Report_DoubleResolveIsConflictAndUpholdIsAtomic()
    {
        await using ChatDbContext db = CreateDb();
        UserId reporter = UserId.New();
        UserId peer = UserId.New();
        Conversation conversation = await AddConversationAsync(db, reporter, peer);

        Report report = Report.Create(
            conversation.Id, null, reporter, "spam", UtcStamp);
        db.Reports.Add(report);
        await db.SaveChangesAsync();

        var uphold = new UpholdReportCommandHandler(db);
        Assert.True((await uphold.Handle(
            new UpholdReportCommand(UserId.New(), report.Id), default)).IsSuccess);
        Assert.Equal(2, await db.Blocks.CountAsync());

        // Second resolve (either direction) is the 409 with no extra rows.
        Assert.Equal(
            ReportErrors.InvalidState,
            (await uphold.Handle(
                new UpholdReportCommand(UserId.New(), report.Id), default)).Error);
        Assert.Equal(
            ReportErrors.InvalidState,
            (await new DismissReportCommandHandler(db).Handle(
                new DismissReportCommand(UserId.New(), report.Id), default)).Error);
        Assert.Equal(2, await db.Blocks.CountAsync());
        Assert.Equal(ReportStatus.Upheld, (await db.Reports.SingleAsync()).Status);
    }

    [Fact]
    public async Task Report_RateLimitedAfterFivePerHour()
    {
        await using ChatDbContext db = CreateDb();
        var gate = new InMemoryChatRateGate();
        UserId reporter = UserId.New();
        Conversation conversation = await AddConversationAsync(db, reporter, UserId.New());
        var handler = new SubmitReportCommandHandler(db, gate);

        for (int i = 0; i < 5; i++)
        {
            Result<SubmitReportResponse> result = await handler.Handle(
                new SubmitReportCommand(reporter, conversation.Id, null, $"reason {i}"),
                default);
            Assert.True(result.IsSuccess);
        }

        Result<SubmitReportResponse> denied = await handler.Handle(
            new SubmitReportCommand(reporter, conversation.Id, null, "one more"),
            default);

        Assert.True(denied.IsFailure);
        Assert.StartsWith("Chat.RateLimited.", denied.Error.Code);
    }

    // ------------------------------------------------------------------
    // List: stable order + unread agreement.
    // ------------------------------------------------------------------

    [Fact]
    public async Task List_OrdersByActivityWithIdTieBreak()
    {
        await using ChatDbContext db = CreateDb();
        UserId me = UserId.New();
        var lookup = new FakeUserLookup();
        var facilities = new FakeFacilityLookup();

        var first = await AddConversationAsync(db, me, NewKnown(lookup));
        var second = await AddConversationAsync(db, me, NewKnown(lookup));

        // Force identical activity stamps.
        foreach (Conversation conversation in await db.Conversations.ToListAsync())
        {
            db.Entry(conversation).Property(c => c.LastActivityOnUtc).CurrentValue = UtcStamp;
        }

        await db.SaveChangesAsync();

        var handler = new GetConversationsQueryHandler(db, lookup, facilities);
        var page = (await handler.Handle(
            new GetConversationsQuery(me, 1, 20), default)).Value;

        Assert.Equal(2, page.Items.Count);
        // Id desc tie-break: larger Guid first.
        Assert.True(
            page.Items[0].Id.CompareTo(page.Items[1].Id) > 0);
    }

    // ------------------------------------------------------------------
    // Privacy settings.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Privacy_DefaultsFalseRequiresBothAndRoundTrips()
    {
        await using ChatDbContext db = CreateDb();
        UserId owner = UserId.New();

        var get = new GetChatPrivacyQueryHandler(db);
        ChatPrivacyResponse defaults = (await get.Handle(
            new GetChatPrivacyQuery(owner), default)).Value;
        Assert.False(defaults.ShowOnlineStatus);
        Assert.False(defaults.ShareLastSeen);

        var update = new UpdateChatPrivacyCommandHandler(db);
        var missing = await update.Handle(
            new UpdateChatPrivacyCommand(owner, true, null), default);
        Assert.True(missing.IsFailure);

        ChatPrivacyResponse stored = (await update.Handle(
            new UpdateChatPrivacyCommand(owner, true, false), default)).Value;
        Assert.True(stored.ShowOnlineStatus);
        Assert.False(stored.ShareLastSeen);

        ChatPrivacyResponse reloaded = (await get.Handle(
            new GetChatPrivacyQuery(owner), default)).Value;
        Assert.Equal(stored, reloaded);
    }

    // ------------------------------------------------------------------
    // Rate gate unit behavior.
    // ------------------------------------------------------------------

    [Fact]
    public void RateGate_SendAllowsSixtyPerUserThirtyPerConversation()
    {
        var gate = new InMemoryChatRateGate();
        UserId user = UserId.New();
        ConversationId conversation = new(Guid.CreateVersion7());

        for (int i = 0; i < 30; i++)
        {
            Assert.True(gate.TryAcquireSend(user, conversation, out _));
        }

        // Conversation budget exhausted while the user budget remains.
        Assert.False(gate.TryAcquireSend(user, conversation, out TimeSpan retry));
        Assert.True(retry > TimeSpan.Zero);

        // A different conversation still has budget.
        Assert.True(gate.TryAcquireSend(
            user, new ConversationId(Guid.CreateVersion7()), out _));
    }

    [Fact]
    public void RateGate_SyncAllowsSixtyPerConversation()
    {
        var gate = new InMemoryChatRateGate();
        UserId user = UserId.New();
        ConversationId conversation = new(Guid.CreateVersion7());

        for (int i = 0; i < 60; i++)
        {
            Assert.True(gate.TryAcquireSync(user, conversation, out _));
        }

        Assert.False(gate.TryAcquireSync(user, conversation, out TimeSpan retry));
        Assert.True(retry > TimeSpan.Zero);
    }

    // ------------------------------------------------------------------
    // Controller authorization posture.
    // ------------------------------------------------------------------

    [Fact]
    public void ChatController_RequiresNormalAccess()
    {
        var attribute = Assert.IsType<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>(
            typeof(Ahlena.API.Controllers.ChatController)
                .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
                .Single());

        // Sensitive Chat access requires session-bound Normal tokens (sid).
        Assert.Equal(
            Ahlena.API.Authorization.AuthorizationPolicies.SessionBoundAccess,
            attribute.Policy);
    }

    // ------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------

    private static UserId NewKnown(FakeUserLookup lookup)
    {
        UserId id = UserId.New();
        lookup.Add(id);
        return id;
    }

    private static object GetLookup(OpenConversationCommandHandler handler) =>
        typeof(OpenConversationCommandHandler)
            .GetField("_userLookup", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(handler)!;

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

    private sealed class FakeUserLookup : IChatUserLookup
    {
        private readonly Dictionary<UserId, UserProfile> _profiles = new();

        public void Add(UserId id, int accountType = 1) =>
            _profiles[id] = new UserProfile(id, "User", null, accountType);

        public FakeUserLookup(UserId known)
            : this() => Add(known);

        public FakeUserLookup()
        {
        }

        public Task<UserProfile?> GetProfileAsync(
            UserId userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<UserProfile?>(
                _profiles.TryGetValue(userId, out UserProfile? profile) ? profile : null);
    }

    private sealed class FakeFacilityLookup : IChatFacilityLookup
    {
        public Task<FacilityPresentation?> GetForOwnerAsync(
            UserId ownerUserId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<FacilityPresentation?>(null);
    }

    private sealed class AllowEligibility : IChatEligibilityChecker
    {
        public Task<bool> CanOpenAsync(
            UserId openerUserId,
            UserId recipientUserId,
            BookingId? bookingId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class DenyEligibility : IChatEligibilityChecker
    {
        public Task<bool> CanOpenAsync(
            UserId openerUserId,
            UserId recipientUserId,
            BookingId? bookingId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class FakeBookingGateway : IChatBookingGateway
    {
        private readonly Dictionary<BookingId, BookingOpenTarget> _targets = new();

        public void Resolve(BookingId bookingId, UserId counterpart, bool isActive = true) =>
            _targets[bookingId] = new BookingOpenTarget(counterpart, isActive);

        public Task<UserId?> ResolveCounterpartAsync(
            BookingId bookingId,
            UserId openerUserId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<UserId?>(
                _targets.TryGetValue(bookingId, out BookingOpenTarget? target) && target.IsActive
                    ? target.Counterpart
                    : null);

        public Task<BookingOpenTarget?> ResolveForReopenAsync(
            BookingId bookingId,
            UserId openerUserId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BookingOpenTarget?>(
                _targets.TryGetValue(bookingId, out BookingOpenTarget? target) ? target : null);
    }
}
