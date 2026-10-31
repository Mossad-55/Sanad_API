using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Domain.Conversations;
using Ahlena.Modules.Chat.Domain.Messages;

namespace Ahlena.Modules.Chat.Domain.Changes;

/// <summary>
/// One committed, replayable change inside a conversation: a created message
/// or a receipt transition (first delivery / first read) of an existing
/// message. Immutable once written; retention is indefinite in this package.
/// </summary>
public sealed class ChatChange : AggregateRoot<ChatChangeId>
{
    private ChatChange()
    {
    }

    private ChatChange(
        ChatChangeId id,
        ConversationId conversationId,
        long revision,
        ChatChangeType type,
        MessageId messageId,
        DateTime createdOnUtc)
        : base(id)
    {
        ConversationId = conversationId;
        Revision = revision;
        Type = type;
        MessageId = messageId;
        CreatedOnUtc = createdOnUtc;
    }

    public ConversationId ConversationId { get; private set; }

    /// <summary>Gapless per-conversation cursor, see <see cref="ChatChangeType"/>.</summary>
    public long Revision { get; private set; }

    public ChatChangeType Type { get; private set; }

    public MessageId MessageId { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    /// <summary>After-commit realtime fan-out watermark (dispatcher).</summary>
    public DateTime? PublishedOnUtc { get; private set; }

    public static ChatChange Create(
        ConversationId conversationId,
        long revision,
        ChatChangeType type,
        MessageId messageId,
        DateTime createdOnUtc)
    {
        if (conversationId.Value == Guid.Empty)
        {
            throw new DomainException(
                "Chat change conversation is required.");
        }

        if (revision <= 0)
        {
            throw new DomainException(
                "Chat change revision must be positive.");
        }

        if (!Enum.IsDefined(type))
        {
            throw new DomainException(
                "Chat change type is not supported.");
        }

        if (messageId.Value == Guid.Empty)
        {
            throw new DomainException(
                "Chat change message is required.");
        }

        if (createdOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Chat change timestamps must be UTC.");
        }

        return new ChatChange(
            ChatChangeId.New(),
            conversationId,
            revision,
            type,
            messageId,
            createdOnUtc);
    }

    public void MarkPublished(DateTime publishedOnUtc)
    {
        if (publishedOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Chat change timestamps must be UTC.");
        }

        PublishedOnUtc = publishedOnUtc;
    }
}
