using MediatR;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Dispatch;
using Sanad.Modules.Chat.Application.Events;
using Sanad.Modules.Chat.Application.Messages;
using Sanad.Modules.Chat.Application.Realtime;
using Sanad.Modules.Chat.Domain.Changes;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Domain.Messages;
using Sanad.Modules.Chat.Domain.Outbox;
using Sanad.Modules.Chat.Infrastructure.Persistence;
using Sanad.Modules.Notifications.Application.Notifications;

namespace Sanad.UnitTests.ChatPlus;

/// <summary>
/// Phase 3 dispatcher tests: durable intent survives the producer crash,
/// retries use bounded backoff, redelivery is idempotent, stale claims are
/// reclaimed, and exhausted attempts park for operator retry.
/// </summary>
public sealed class ChatPlusDispatchTests
{
    private static DateTime UtcStamp =>
        DateTime.SpecifyKind(new DateTime(2026, 10, 1, 12, 0, 0), DateTimeKind.Utc);

    private static ChatDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task Dispatch_PublishesChangesAndDeliversIntentAfterCommit()
    {
        await using ChatDbContext db = CreateDb();
        UserId alice = UserId.New();
        UserId bob = UserId.New();
        Conversation conversation = await AddConversationAsync(db, alice, bob);

        var recorder = new ChatEventRecorder(db);
        var sender = new SendMessageCommandHandler(db, null, null, recorder);
        var sent = await sender.Handle(
            new SendMessageCommand(alice, conversation.Id, "k-1", "Text", "hi", null),
            default);
        Assert.True(sent.IsSuccess);

        var publisher = new FakePublisher();
        var notifications = new FakeSender();
        var dispatcher = new ChatOutboxDispatcher(
            db, notifications, publisher, null, () => DateTime.UtcNow, () => 0);

        (int changes, int intents) = await dispatcher.DispatchAsync(default);

        Assert.Equal(1, changes);
        Assert.Equal(1, intents);
        Assert.Single(publisher.Created);
        var command = Assert.IsType<CreateChatMessageNotificationCommand>(
            Assert.Single(notifications.Sent));
        Assert.Equal(bob.Value, command.RecipientUserId);
        Assert.Equal(conversation.Id.Value, command.ConversationId);

        // Second cycle is quiet: published rows never republish.
        (int changes2, int intents2) = await dispatcher.DispatchAsync(default);
        Assert.Equal(0, changes2);
        Assert.Equal(0, intents2);
        Assert.Single(publisher.Created);
        Assert.Single(notifications.Sent);
    }

    [Fact]
    public async Task Dispatch_RetriesFailuresWithBackoffThenParksForOperator()
    {
        await using ChatDbContext db = CreateDb();
        UserId alice = UserId.New();
        UserId bob = UserId.New();
        Conversation conversation = await AddConversationAsync(db, alice, bob);

        var recorder = new ChatEventRecorder(db);
        var sender = new SendMessageCommandHandler(db, null, null, recorder);
        Assert.True((await sender.Handle(
            new SendMessageCommand(alice, conversation.Id, "k-r", "Text", "hi", null),
            default)).IsSuccess);

        DateTime now = DateTime.UtcNow;
        var notifications = new ThrowingSender();
        var dispatcher = new ChatOutboxDispatcher(
            db, notifications, new FakePublisher(), null, () => now, () => 0);

        (int _, int intents) = await dispatcher.DispatchAsync(default);
        Assert.Equal(0, intents);

        var item = await db.Outbox.SingleAsync();
        Assert.Equal(1, item.AttemptCount);
        Assert.Equal("Chat.Outbox.InvalidOperationException", item.LastErrorCode);
        // First retry borrows the attempt-1 delay: 5s base with zero
        // jitter in this test.
        Assert.True(item.NextAttemptOnUtc > now.AddSeconds(4));
        Assert.True(item.NextAttemptOnUtc <= now.AddSeconds(6));

        // Not due yet: quiet cycle.
        (int _, int intents2) = await dispatcher.DispatchAsync(default);
        Assert.Equal(0, intents2);

        // Exhaust attempts: parked unpublished for operator retry.
        for (int i = 0; i < 7; i++)
        {
            now += TimeSpan.FromHours(2);
            await dispatcher.DispatchAsync(default);
        }

        var parked = await db.Outbox.SingleAsync();
        Assert.Equal(8, parked.AttemptCount);
        Assert.Null(parked.PublishedOnUtc);

        now += TimeSpan.FromDays(30);
        (int _, int intents3) = await dispatcher.DispatchAsync(default);
        Assert.Equal(0, intents3);
    }

    [Fact]
    public async Task Dispatch_ReclaimsStaleClaimsAfterACrash()
    {
        await using ChatDbContext db = CreateDb();
        UserId alice = UserId.New();
        UserId bob = UserId.New();
        Conversation conversation = await AddConversationAsync(db, alice, bob);

        var recorder = new ChatEventRecorder(db);
        var sender = new SendMessageCommandHandler(db, null, null, recorder);
        Assert.True((await sender.Handle(
            new SendMessageCommand(alice, conversation.Id, "k-c", "Text", "hi", null),
            default)).IsSuccess);

        // Simulate a crash between claim and publish: live claim token with
        // an old claim instant.
        var item = await db.Outbox.SingleAsync();
        item.Claim(Guid.CreateVersion7(), DateTime.UtcNow - TimeSpan.FromMinutes(30));
        await db.SaveChangesAsync();

        var notifications = new FakeSender();
        var dispatcher = new ChatOutboxDispatcher(
            db, notifications, new FakePublisher(), null, () => DateTime.UtcNow, () => 0);

        (int _, int intents) = await dispatcher.DispatchAsync(default);

        Assert.Equal(1, intents);
        Assert.Single(notifications.Sent);
        Assert.NotNull((await db.Outbox.SingleAsync()).PublishedOnUtc);
    }

    [Fact]
    public void Backoff_DoublesFromFiveSecondsCappedAtSixtyMinutes()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), ChatOutboxItem.DelayForAttempt(1, 0));
        Assert.Equal(TimeSpan.FromSeconds(10), ChatOutboxItem.DelayForAttempt(2, 0));
        Assert.Equal(TimeSpan.FromSeconds(20), ChatOutboxItem.DelayForAttempt(3, 0));
        Assert.Equal(TimeSpan.FromSeconds(640), ChatOutboxItem.DelayForAttempt(8, 0));
        Assert.Equal(TimeSpan.FromMinutes(60), ChatOutboxItem.DelayForAttempt(99, 0));
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

    private sealed class FakePublisher : IChatRealtimePublisher
    {
        public List<object> Created { get; } = new();
        public List<(Guid, Guid)> Receipts { get; } = new();

        public Task PublishMessageCreatedAsync(
            Guid conversationId,
            object messagePayload,
            CancellationToken cancellationToken)
        {
            Created.Add(messagePayload);
            return Task.CompletedTask;
        }

        public Task PublishReceiptUpdatedAsync(
            Guid conversationId,
            Guid messageId,
            DateTime? deliveredOnUtc,
            DateTime? readOnUtc,
            CancellationToken cancellationToken)
        {
            Receipts.Add((conversationId, messageId));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSender : ISender
    {
        public List<object> Sent { get; } = new();

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            Sent.Add(request!);
            object response = typeof(TResponse) == typeof(Result<Guid>)
                ? Result<Guid>.Success(Guid.NewGuid())
                : Guid.NewGuid();
            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingSender : ISender
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("provider down");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
