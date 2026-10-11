using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.Modules.Chat.Domain.Attachments;

/// <summary>
/// Conversation-scoped, uploader-bound file record. Each upload binds to a
/// single message at most once (<see cref="Bind"/>).
/// </summary>
public sealed class Attachment : AggregateRoot<AttachmentId>
{
    public const int MaximumStorageKeyLength = 500;

    private Attachment()
    {
    }

    private Attachment(
        AttachmentId id,
        ConversationId conversationId,
        UserId uploadedBy,
        string storageKey,
        string contentType,
        long sizeBytes,
        DateTime uploadedOnUtc)
        : base(id)
    {
        ConversationId = conversationId;
        UploadedBy = uploadedBy;
        StorageKey = storageKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        UploadedOnUtc = uploadedOnUtc;
    }

    public ConversationId ConversationId { get; private set; }

    public UserId UploadedBy { get; private set; } = default!;

    public string StorageKey { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public DateTime UploadedOnUtc { get; private set; }

    public bool IsBound { get; private set; }

    public static Attachment Create(
        ConversationId conversationId,
        UserId uploadedBy,
        string storageKey,
        string contentType,
        long sizeBytes,
        DateTime uploadedOnUtc)
    {
        if (conversationId.Value == Guid.Empty)
        {
            throw new DomainException(
                "Attachment conversation is required.");
        }

        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new DomainException(
                "Attachment storage key is required.");
        }

        string normalizedKey = storageKey.Trim();

        if (normalizedKey.Length > MaximumStorageKeyLength)
        {
            throw new DomainException(
                "Attachment storage key cannot exceed " +
                $"{MaximumStorageKeyLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new DomainException(
                "Attachment content type is required.");
        }

        if (sizeBytes <= 0)
        {
            throw new DomainException(
                "Attachment size must be greater than zero.");
        }

        if (uploadedBy == UserId.Empty)
        {
            throw new DomainException(
                "Attachment uploader is required.");
        }

        if (uploadedOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Attachment timestamps must be UTC.");
        }

        return new Attachment(
            AttachmentId.New(),
            conversationId,
            uploadedBy,
            normalizedKey,
            contentType.Trim().ToLowerInvariant(),
            sizeBytes,
            uploadedOnUtc);
    }

    public void Bind()
    {
        if (IsBound)
        {
            throw new DomainException(
                "Attachment is already bound to a message.");
        }

        IsBound = true;
    }
}
