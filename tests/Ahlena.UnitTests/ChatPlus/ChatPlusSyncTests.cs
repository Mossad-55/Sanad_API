using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Application.Conversations;
using Ahlena.Modules.Chat.Application.Events;
using Ahlena.Modules.Chat.Application.Messages;
using Ahlena.Modules.Chat.Application.Sync;
using Ahlena.Modules.Chat.Domain.Conversations;
using Ahlena.Modules.Chat.Domain.Messages;
using Ahlena.Modules.Chat.Infrastructure.Persistence;

namespace Ahlena.UnitTests.ChatPlus;

/// <summary>
/// Sync contract tests: snapshot pagination, change replay (including
/// receipt-only changes and >100-message catch-up), cursor authentication,
/// reset semantics, and delivery acknowledgement through sync.
/// </summary>
public sealed class ChatPlusSyncTests
{
    private static DateTime UtcStamp =>
        DateTime.SpecifyKind(new DateTime(2026, 10, 1, 12, 0, 0), DateTimeKind.Utc);

    private static ChatDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task Sync_SnapshotThenChangesConvergesWithoutGaps()
    {
        await using ChatDbContext db = CreateDb();
        UserId alice = UserId.New();
        UserId bob = UserId.New();
        Conversation conversation = await AddConversationAsync(db, alice, bob);

        var protector = new FakeProtector();
        var recorder = new ChatEventRecorder(db);
        var sender = new SendMessageCommandHandler(db, null, null, recorder);

        for (int i = 0; i < 5; i++)
        {
            var sent = await sender.Handle(
                new SendMessageCommand(
                    alice, conversation.Id, $"k-{i}", "Text", $"msg {i}", null),
                default);
            Assert.True(sent.IsSuccess);
        }

        var sync = new GetConversationSyncQueryHandler(db, protector, null, recorder);

        // Snapshot with pageSize 2: three pages, then a changes cursor.
        var seen = new List<string>();
        string? cursor = null;
        bool hasMore = true;
        int pages = 0;

        while (hasMore)
        {
            var page = (await sync.Handle(
                new GetConversationSyncQuery(bob, conversation.Id, cursor, 2),
                default)).Value;

            Assert.False(page.ResetRequired);
            seen.AddRange(page.Items.Select(item => item.Revision + ":" + item.Type));
            cursor = page.NextCursor;
            hasMore = page.HasMore;
            pages++;
            Assert.True(pages <= 10);
        }

        Assert.Equal(3, pages);
        Assert.NotNull(cursor);

        // No further changes: empty page, same cursor advances nothing.
        var caughtUp = (await sync.Handle(
            new GetConversationSyncQuery(bob, conversation.Id, cursor, 100),
            default)).Value;
        Assert.False(caughtUp.HasMore);
        Assert.Empty(caughtUp.Items);

        // One more message + a receipt-only change: both replay.
        var late = await sender.Handle(
            new SendMessageCommand(
                alice, conversation.Id, "k-late", "Text", "late", null),
            default);
        Assert.True(late.IsSuccess);

        var read = new MarkConversationReadCommandHandler(db, recorder);
        Assert.True((await read.Handle(
            new MarkConversationReadCommand(
                bob, conversation.Id, late.Value.Message.Id),
            default)).IsSuccess);

        var delta = (await sync.Handle(
            new GetConversationSyncQuery(bob, conversation.Id, cursor, 100),
            default)).Value;

        Assert.True(delta.Items.Count >= 2);
        Assert.Contains(
            delta.Items,
            item => item.Type == nameof(Ahlena.Modules.Chat.Domain.Changes.ChatChangeType.MessageCreated));
        Assert.Contains(
            delta.Items,
            item => item.Type == nameof(Ahlena.Modules.Chat.Domain.Changes.ChatChangeType.MessageReceiptUpdated));

        // Change-mode payloads are never double-wrapped: created items
        // carry the message item, receipt items the receipt envelope.
        foreach (SyncChangeItem item in delta.Items)
        {
            if (item.Type == nameof(Ahlena.Modules.Chat.Domain.Changes.ChatChangeType.MessageCreated))
            {
                Assert.IsType<MessageItemResponse>(item.Payload);
            }
            else
            {
                Assert.IsType<SyncReceiptPayload>(item.Payload);
            }
        }

        // Every revision is a positive decimal string; change revisions are
        // strictly increasing within the poll.
        long[] revisions = delta.Items
            .Where(item => item.Revision != "0")
            .Select(item => long.Parse(item.Revision))
            .ToArray();
        Assert.All(revisions, revision => Assert.True(revision > 0));
        Assert.Equal(revisions.OrderBy(r => r).ToArray(), revisions);
    }

    [Fact]
    public async Task Sync_RecoversMoreThanOneHundredMissedMessages()
    {
        await using ChatDbContext db = CreateDb();
        UserId alice = UserId.New();
        UserId bob = UserId.New();
        Conversation conversation = await AddConversationAsync(db, alice, bob);

        var protector = new FakeProtector();
        var recorder = new ChatEventRecorder(db);
        var sender = new SendMessageCommandHandler(db, null, null, recorder);

        for (int i = 0; i < 120; i++)
        {
            var sent = await sender.Handle(
                new SendMessageCommand(
                    alice, conversation.Id, $"bulk-{i}", "Text", $"bulk {i}", null),
                default);
            Assert.True(sent.IsSuccess);
        }

        var sync = new GetConversationSyncQueryHandler(db, protector, null, recorder);

        var recovered = new HashSet<Guid>();
        string? cursor = null;
        bool hasMore = true;
        int polls = 0;

        while (hasMore)
        {
            var page = (await sync.Handle(
                new GetConversationSyncQuery(bob, conversation.Id, cursor, 100),
                default)).Value;

            foreach (SyncChangeItem item in page.Items)
            {
                if (item.Payload is MessageItemResponse message)
                {
                    recovered.Add(message.Id);
                }
            }

            cursor = page.NextCursor;
            hasMore = page.HasMore;
            polls++;
            Assert.True(polls <= 10);
        }

        Assert.Equal(120, recovered.Count);
    }

    [Fact]
    public async Task Sync_ForeignOrMalformedCursorIsPrivacy404()
    {
        await using ChatDbContext db = CreateDb();
        UserId alice = UserId.New();
        UserId bob = UserId.New();
        Conversation conversation = await AddConversationAsync(db, alice, bob);
        Conversation foreign = await AddConversationAsync(db, UserId.New(), UserId.New());

        var protector = new FakeProtector();
        var sync = new GetConversationSyncQueryHandler(db, protector);

        string foreignCursor = protector.Protect(new ChatSyncCursor(
            ChatSyncCursor.CurrentVersion,
            ChatSyncCursor.ChangesMode,
            foreign.Id.Value,
            bob.Value,
            0, 0, null, null));

        var foreignResult = await sync.Handle(
            new GetConversationSyncQuery(bob, conversation.Id, foreignCursor, 100),
            default);
        Assert.True(foreignResult.IsFailure);
        Assert.Equal(ChatSyncErrors.NotFound, foreignResult.Error);

        var malformed = await sync.Handle(
            new GetConversationSyncQuery(bob, conversation.Id, "not-a-cursor", 100),
            default);
        Assert.True(malformed.IsFailure);
        Assert.Equal(ChatSyncErrors.NotFound, malformed.Error);

        // A cursor minted for another user is equally rejected.
        string otherUser = protector.Protect(new ChatSyncCursor(
            ChatSyncCursor.CurrentVersion,
            ChatSyncCursor.ChangesMode,
            conversation.Id.Value,
            UserId.New().Value,
            0, 0, null, null));
        var crossUser = await sync.Handle(
            new GetConversationSyncQuery(bob, conversation.Id, otherUser, 100),
            default);
        Assert.True(crossUser.IsFailure);
        Assert.Equal(ChatSyncErrors.NotFound, crossUser.Error);
    }

    [Fact]
    public async Task Sync_UnsupportedCursorVersionRequestsReset()
    {
        await using ChatDbContext db = CreateDb();
        UserId alice = UserId.New();
        UserId bob = UserId.New();
        Conversation conversation = await AddConversationAsync(db, alice, bob);

        var protector = new FakeProtector();
        var sync = new GetConversationSyncQueryHandler(db, protector);

        string old = protector.Protect(new ChatSyncCursor(
            999,
            ChatSyncCursor.ChangesMode,
            conversation.Id.Value,
            bob.Value,
            0, 0, null, null));

        var page = (await sync.Handle(
            new GetConversationSyncQuery(bob, conversation.Id, old, 100),
            default)).Value;

        Assert.True(page.ResetRequired);
        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task Sync_AcknowledgesDeliveryOnlyForReturnedSnapshots()
    {
        await using ChatDbContext db = CreateDb();
        UserId alice = UserId.New();
        UserId bob = UserId.New();
        Conversation conversation = await AddConversationAsync(db, alice, bob);

        var protector = new FakeProtector();
        var recorder = new ChatEventRecorder(db);
        var sender = new SendMessageCommandHandler(db, null, null, recorder);

        var sent = await sender.Handle(
            new SendMessageCommand(alice, conversation.Id, "k-ack", "Text", "hi", null),
            default);
        Assert.True(sent.IsSuccess);

        var sync = new GetConversationSyncQueryHandler(db, protector, null, recorder);
        var page = (await sync.Handle(
            new GetConversationSyncQuery(bob, conversation.Id, null, 100),
            default)).Value;

        SyncChangeItem first = page.Items.Single(
            item => item.Payload is MessageItemResponse);
        var payload = Assert.IsType<MessageItemResponse>(first.Payload);
        Assert.NotNull(payload.DeliveredOnUtc);

        // The sender's own messages are never stamped for the sender: bob
        // sends one, syncs as himself, and the snapshot carries no delivery
        // tick for his own message.
        var own = await sender.Handle(
            new SendMessageCommand(bob, conversation.Id, "k-own", "Text", "mine", null),
            default);
        Assert.True(own.IsSuccess);

        var again = (await sync.Handle(
            new GetConversationSyncQuery(bob, conversation.Id, null, 100),
            default)).Value;

        MessageItemResponse ownSnapshot = again.Items
            .Select(item => item.Payload)
            .OfType<MessageItemResponse>()
            .Single(message => message.Id == own.Value.Message.Id);
        Assert.Null(ownSnapshot.DeliveredOnUtc);
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

    private sealed class FakeProtector : IChatSyncCursorProtector
    {
        public string Protect(ChatSyncCursor cursor) =>
            Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(cursor)));

        public bool TryUnprotect(string? token, out ChatSyncCursor? cursor)
        {
            cursor = null;

            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            try
            {
                cursor = JsonSerializer.Deserialize<ChatSyncCursor>(
                    System.Text.Encoding.UTF8.GetString(
                        Convert.FromBase64String(token)));
                return cursor is not null;
            }
            catch (Exception ex) when (
                ex is FormatException || ex is JsonException)
            {
                return false;
            }
        }
    }
}
