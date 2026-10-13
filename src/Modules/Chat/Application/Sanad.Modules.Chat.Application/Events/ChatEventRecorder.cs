using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Domain.Changes;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Domain.Messages;
using Sanad.Modules.Chat.Domain.Outbox;

namespace Sanad.Modules.Chat.Application.Events;

/// <summary>
/// Default <see cref="IChatEventRecorder"/>: consumes one gapless revision
/// per change from the conversation and stages the change-log row (and, for
/// created messages, the notification-intent row) in the ambient Chat
/// transaction. The handler's single `SaveChangesAsync` commits everything
/// together. Intent dedupe: at most one outbox row per (message, recipient).
/// </summary>
public sealed class ChatEventRecorder : IChatEventRecorder
{
    private readonly IChatDbContext _dbContext;

    public ChatEventRecorder(IChatDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void RecordMessageCreated(
        Conversation conversation,
        Message message,
        UserId recipientUserId,
        DateTime now)
    {
        if (IsStaged(message.Id, ChatChangeType.MessageCreated))
        {
            return;
        }

        long revision = conversation.TakeNextRevision();

        _dbContext.Changes.Add(ChatChange.Create(
            conversation.Id,
            revision,
            ChatChangeType.MessageCreated,
            message.Id,
            now));

        bool intentExists = _dbContext.Outbox.Local.Any(item =>
                item.MessageId == message.Id &&
                item.RecipientUserId == recipientUserId) ||
            _dbContext.Outbox.Any(item =>
                item.MessageId == message.Id &&
                item.RecipientUserId == recipientUserId);

        if (!intentExists)
        {
            _dbContext.Outbox.Add(ChatOutboxItem.Create(
                conversation.Id,
                message.Id,
                recipientUserId,
                now));
        }
    }

    public void RecordReceiptUpdated(
        Conversation conversation,
        Message message,
        DateTime now)
    {
        if (IsStaged(message.Id, ChatChangeType.MessageReceiptUpdated))
        {
            return;
        }

        long revision = conversation.TakeNextRevision();

        _dbContext.Changes.Add(ChatChange.Create(
            conversation.Id,
            revision,
            ChatChangeType.MessageReceiptUpdated,
            message.Id,
            now));
    }

    /// <summary>
    /// Retry safety: a revision-token collision detaches staged rows and
    /// re-records after reload; the Local check keeps the re-record from
    /// duplicating rows staged earlier in the same attempt.
    /// </summary>
    private bool IsStaged(MessageId messageId, ChatChangeType type) =>
        _dbContext.Changes.Local.Any(item =>
            item.MessageId == messageId &&
            item.Type == type);
}

/// <summary>
/// No-op recorder for handler constructions without an ambient recorder
/// (pre-existing focused tests). Production always registers
/// <see cref="ChatEventRecorder"/>.
/// </summary>
public sealed class NullChatEventRecorder : IChatEventRecorder
{
    public static readonly NullChatEventRecorder Instance = new();

    public void RecordMessageCreated(
        Conversation conversation,
        Message message,
        UserId recipientUserId,
        DateTime now)
    {
    }

    public void RecordReceiptUpdated(
        Conversation conversation,
        Message message,
        DateTime now)
    {
    }
}
