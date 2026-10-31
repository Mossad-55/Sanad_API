using MediatR;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Application.Messages;
using Ahlena.Modules.Chat.Domain.Conversations;
using Ahlena.Modules.Chat.Domain.Messages;
using Ahlena.Modules.Chat.Infrastructure.Persistence;
using Ahlena.Modules.Notifications.Application.Notifications;
using Ahlena.Modules.Notifications.Domain.Notifications;
using Ahlena.Modules.Notifications.Infrastructure.Persistence;
using Ahlena.UnitTests.Notifications;

namespace Ahlena.UnitTests.Chat;

/// <summary>
/// Lane B item 4 — the new-message in-app alert. Pins the seam contract:
/// one ids-only row for the non-sender participant, the conversation as the
/// destination, the per-(message, recipient) idempotency key, a generic
/// localized copy carrying no message text and no raw id (F8), no producer
/// call and no second row on an exact replay, and a producer failure that
/// surfaces instead of being swallowed.
/// </summary>
public sealed class ChatMessageNotificationTests
{
    private const string MessageText = "top secret lunch plans";

    private static DateTime UtcStamp =>
        DateTime.SpecifyKind(new DateTime(2026, 10, 8, 9, 0, 0), DateTimeKind.Utc);

    // ------------------------------------------------------------------
    // Send → alert row.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Send_writesOneIdsOnlyAlertForTheNonSender()
    {
        await using ChatDbContext chat = CreateChatDb();
        await using NotificationsDbContext notifications = CreateNotificationsDb();
        (UserId alice, UserId bob, Conversation conversation) =
            await SeedConversationAsync(chat);
        var seam = new NotificationSeamSender(notifications);
        var handler = new SendMessageCommandHandler(chat, seam);

        Result<SendMessageResponse> result = await handler.Handle(
            new SendMessageCommand(
                alice, conversation.Id, "client-1", "Text", MessageText, null),
            default);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.True(result.Value.Created);

        Message stored = Assert.Single(chat.Messages);
        List<Notification> rows = await notifications.Notifications
            .AsNoTracking()
            .ToListAsync();
        Notification row = Assert.Single(rows);

        // Right recipient: the non-sender participant. The sender is never
        // alerted about their own message.
        Assert.Equal(bob.Value, row.RecipientUserId);
        Assert.DoesNotContain(rows, item => item.RecipientUserId == alice.Value);

        // Category/type follow the seam's naming; the destination is the
        // conversation the message landed in.
        Assert.Equal("Chat", row.Category);
        Assert.Equal("NewMessage", row.Type);
        Assert.Equal("ChatConversation", row.DestinationEntityKind);
        Assert.Equal(conversation.Id.Value, row.DestinationEntityId);
        Assert.Equal(
            $"chat-message:{stored.Id.Value:N}:{bob.Value:N}",
            row.IdempotencyKey);
        Assert.Equal(DateTimeKind.Utc, row.CreatedOnUtc.Kind);

        // Ids only: the copy is a generic phrase in the recipient's language
        // (Arabic here — the persisted-language default), the structured
        // destination keeps the conversation id, and neither title nor body
        // carries a raw id or a single character of the text.
        Assert.Equal(NotificationTextCatalog.ChatNewMessage(UiLanguage.Arabic).Title, row.Title);
        Assert.Equal(NotificationTextCatalog.ChatNewMessage(UiLanguage.Arabic).Body, row.Body);
        Assert.DoesNotContain(conversation.Id.Value.ToString(), row.Title + row.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(conversation.Id.Value.ToString("N"), row.Title + row.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(stored.Id.Value.ToString(), row.Title + row.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(stored.Id.Value.ToString("N"), row.Title + row.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(MessageText, row.Title, StringComparison.Ordinal);
        Assert.DoesNotContain(MessageText, row.Body, StringComparison.Ordinal);

        Assert.Equal(1, seam.ProducerCalls);
    }

    [Fact]
    public async Task Send_exactReplayFiresNoProducerAndWritesNoSecondRow()
    {
        await using ChatDbContext chat = CreateChatDb();
        await using NotificationsDbContext notifications = CreateNotificationsDb();
        (UserId alice, _, Conversation conversation) =
            await SeedConversationAsync(chat);
        var seam = new NotificationSeamSender(notifications);
        var handler = new SendMessageCommandHandler(chat, seam);
        var command = new SendMessageCommand(
            alice, conversation.Id, "client-1", "Text", MessageText, null);

        Result<SendMessageResponse> created = await handler.Handle(command, default);
        Result<SendMessageResponse> replay = await handler.Handle(command, default);

        Assert.True(created.Value.Created);
        Assert.True(replay.IsSuccess, replay.IsFailure ? replay.Error.Code : null);
        Assert.False(replay.Value.Created);
        Assert.Equal(created.Value.Message.Id, replay.Value.Message.Id);

        // An existing message means its alert was already produced: the
        // replay path never reaches the seam and never writes a second row.
        Assert.Equal(1, seam.ProducerCalls);
        Assert.Single(await notifications.Notifications.ToListAsync());
        Assert.Single(chat.Messages);
    }

    // ------------------------------------------------------------------
    // Failure semantics: surface, never silently swallow.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Send_producerFailureSurfacesAndTheRetryTakesTheReplayPath()
    {
        await using ChatDbContext chat = CreateChatDb();
        await using NotificationsDbContext notifications = CreateNotificationsDb();
        (UserId alice, _, Conversation conversation) =
            await SeedConversationAsync(chat);
        var seam = new ThrowingSeam();
        var handler = new SendMessageCommandHandler(chat, seam);
        var command = new SendMessageCommand(
            alice, conversation.Id, "client-1", "Text", MessageText, null);

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => handler.Handle(command, default));

        Assert.Equal("notifications unavailable", failure.Message);
        Assert.Equal(1, seam.ProducerCalls);

        // The message was committed before the producer ran, so the failure
        // does not roll it back...
        Assert.Single(chat.Messages);
        Assert.Empty(await notifications.Notifications.ToListAsync());

        // ...and the documented retry with the same clientMessageId is the
        // idempotent replay: success without a duplicate message and without
        // a second producer attempt (a producer failure can therefore leave
        // a delivered message unalerted — no retry loop for this seam in V1).
        Result<SendMessageResponse> retry = await handler.Handle(command, default);

        Assert.True(retry.IsSuccess, retry.IsFailure ? retry.Error.Code : null);
        Assert.False(retry.Value.Created);
        Assert.Equal(1, seam.ProducerCalls);
        Assert.Single(chat.Messages);
    }

    // ------------------------------------------------------------------
    // Producer command on its own.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Producer_writesTheKeyedRowOnceAndReplaysByIdempotencyKey()
    {
        await using NotificationsDbContext notifications = CreateNotificationsDb();
        Guid recipient = UserId.New().Value;
        Guid conversationId = Guid.NewGuid();
        Guid messageId = Guid.NewGuid();
        // F8: the producer localizes from the recipient's persisted language,
        // read in one lookup for the single recipient of a direct message.
        var languages = FixedNotificationLanguageGateway.For(recipient, UiLanguage.English);
        var handler = new CreateChatMessageNotificationCommandHandler(notifications, languages);
        var command = new CreateChatMessageNotificationCommand(
            recipient, conversationId, messageId, DateTime.UtcNow);

        Result<Guid> first = await handler.Handle(command, default);
        Result<Guid> retry = await handler.Handle(command, default);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error.Code : null);
        Assert.True(retry.IsSuccess, retry.IsFailure ? retry.Error.Code : null);
        Assert.Equal(first.Value, retry.Value);

        Notification row = Assert.Single(await notifications.Notifications.AsNoTracking().ToListAsync());
        Assert.Equal(first.Value, row.Id);
        Assert.Equal(
            $"chat-message:{messageId:N}:{recipient:N}",
            row.IdempotencyKey);
        Assert.Equal("Chat", row.Category);
        Assert.Equal("NewMessage", row.Type);
        Assert.Equal("ChatConversation", row.DestinationEntityKind);
        Assert.Equal(conversationId, row.DestinationEntityId);
        Assert.Equal(NotificationTextCatalog.ChatNewMessage(UiLanguage.English).Title, row.Title);
        Assert.Equal(NotificationTextCatalog.ChatNewMessage(UiLanguage.English).Body, row.Body);
        Assert.Equal(1, languages.LookupCount);
        Assert.DoesNotContain(conversationId.ToString(), row.Title + row.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(messageId.ToString(), row.Title + row.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Producer_commandCarriesOnlyIdsAndATimestamp()
    {
        // Compile-level intent made executable: no property exists on the
        // producer command that could ever carry message text.
        string[] properties = typeof(CreateChatMessageNotificationCommand)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "ConversationId", "CreatedOnUtc", "MessageId", "RecipientUserId" },
            properties);
    }

    [Fact]
    public void Producer_validatorRequiresIdsAndAUtcStamp()
    {
        var validator = new CreateChatMessageNotificationCommandValidator();
        Guid id = Guid.NewGuid();

        Assert.True(validator.Validate(
            new CreateChatMessageNotificationCommand(id, id, id, DateTime.UtcNow)).IsValid);
        Assert.False(validator.Validate(
            new CreateChatMessageNotificationCommand(Guid.Empty, id, id, DateTime.UtcNow)).IsValid);
        Assert.False(validator.Validate(
            new CreateChatMessageNotificationCommand(id, Guid.Empty, id, DateTime.UtcNow)).IsValid);
        Assert.False(validator.Validate(
            new CreateChatMessageNotificationCommand(id, id, Guid.Empty, DateTime.UtcNow)).IsValid);
        Assert.False(validator.Validate(
            new CreateChatMessageNotificationCommand(
                id, id, id, new DateTime(2026, 10, 8, 9, 0, 0))).IsValid);
    }

    // ------------------------------------------------------------------
    // Harness.
    // ------------------------------------------------------------------

    private static ChatDbContext CreateChatDb() =>
        new(new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static NotificationsDbContext CreateNotificationsDb() =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<
        (UserId Alice, UserId Bob, Conversation Conversation)>
        SeedConversationAsync(ChatDbContext db)
    {
        UserId alice = UserId.New();
        UserId bob = UserId.New();

        Conversation conversation = Conversation.Create(
            alice, bob, ConversationScope.Direct, null, UtcStamp);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();

        return (alice, bob, conversation);
    }

    /// <summary>
    /// Forwards the producer command to the real Notifications handler over
    /// the in-memory seam, so the wiring and the row shape are asserted
    /// together.
    /// </summary>
    private sealed class NotificationSeamSender(NotificationsDbContext notifications) : ISender
    {
        private readonly CreateChatMessageNotificationCommandHandler _producer =
            new(notifications, FixedNotificationLanguageGateway.ArabicDefault);

        public int ProducerCalls { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            if (request is CreateChatMessageNotificationCommand command)
            {
                ProducerCalls++;
                return (Task<TResponse>)(object)_producer.Handle(command, cancellationToken);
            }

            throw new NotSupportedException(
                $"Unexpected request {request.GetType().Name}.");
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

    private sealed class ThrowingSeam : ISender
    {
        public int ProducerCalls { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            if (request is CreateChatMessageNotificationCommand)
            {
                ProducerCalls++;
                return Task.FromException<TResponse>(
                    new InvalidOperationException("notifications unavailable"));
            }

            throw new NotSupportedException(
                $"Unexpected request {request.GetType().Name}.");
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
}
