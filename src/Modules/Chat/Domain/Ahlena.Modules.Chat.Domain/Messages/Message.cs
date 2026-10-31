using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Domain.Attachments;
using Ahlena.Modules.Chat.Domain.Conversations;

namespace Ahlena.Modules.Chat.Domain.Messages;

/// <summary>
/// Immutable chat message inside a conversation. A message is created once
/// with its server-set send stamp and never edited or deleted; the only
/// mutations that ever happen are the set-once delivered/read stamps.
/// Retention is indefinite. <see cref="ClientMessageId"/> is the caller's
/// idempotency key: uniqueness per conversation is enforced by a database
/// index plus a handler pre-check (exact replay returns the original
/// message; same key with different content is a conflict) — the domain
/// only validates the key's shape.
/// </summary>
public sealed class Message : AggregateRoot<MessageId>
{
    public const int MaximumTextLength = 2000;
    public const int MaximumClientMessageIdLength = 100;
    public const int MaximumLocationLabelLength = 200;

    private Message()
    {
    }

    private Message(
        MessageId id,
        ConversationId conversationId,
        UserId senderUserId,
        MessageType type,
        string? text,
        AttachmentId? attachmentId,
        string clientMessageId,
        DateTime sentOnUtc,
        double? latitude,
        double? longitude,
        string? locationLabel)
        : base(id)
    {
        ConversationId = conversationId;
        SenderUserId = senderUserId;
        Type = type;
        Text = text;
        AttachmentId = attachmentId;
        ClientMessageId = clientMessageId;
        SentOnUtc = sentOnUtc;
        Latitude = latitude;
        Longitude = longitude;
        LocationLabel = locationLabel;
    }

    public ConversationId ConversationId { get; private set; }

    public UserId SenderUserId { get; private set; } = default!;

    public MessageType Type { get; private set; }

    /// <summary>Trimmed body for <see cref="MessageType.Text"/>; always
    /// <see langword="null"/> for <see cref="MessageType.File"/>.</summary>
    public string? Text { get; private set; }

    /// <summary>Optional bound attachment reference; the handler binds the
    /// attachment exactly once when the message is persisted.</summary>
    public AttachmentId? AttachmentId { get; private set; }

    /// <summary>Caller-supplied idempotency key, unique per conversation.</summary>
    public string ClientMessageId { get; private set; } = string.Empty;

    public DateTime SentOnUtc { get; private set; }

    /// <summary>Latitude of a <see cref="MessageType.Location"/> message
    /// (-90…90); always <see langword="null"/> for other types.</summary>
    public double? Latitude { get; private set; }

    /// <summary>Longitude of a <see cref="MessageType.Location"/> message
    /// (-180…180); always <see langword="null"/> for other types.</summary>
    public double? Longitude { get; private set; }

    /// <summary>Optional human label of a <see cref="MessageType.Location"/>
    /// message (trimmed, ≤ <see cref="MaximumLocationLabelLength"/>); always
    /// <see langword="null"/> for other types.</summary>
    public string? LocationLabel { get; private set; }

    /// <summary>Set-once: stamped on the recipient's first fetch; later
    /// fetches no-op.</summary>
    public DateTime? DeliveredOnUtc { get; private set; }

    /// <summary>Set-once: stamped by an explicit read mark; later marks
    /// no-op.</summary>
    public DateTime? ReadOnUtc { get; private set; }

    public static Message Create(
        ConversationId conversationId,
        UserId senderUserId,
        MessageType type,
        string? text,
        AttachmentId? attachmentId,
        string clientMessageId,
        DateTime sentOnUtc,
        double? latitude = null,
        double? longitude = null,
        string? locationLabel = null)
    {
        if (conversationId.Value == Guid.Empty)
        {
            throw new DomainException(
                "Message conversation is required.");
        }

        if (senderUserId == UserId.Empty)
        {
            throw new DomainException(
                "Message sender is required.");
        }

        if (type == MessageType.Text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new DomainException(
                    "Text message requires a body.");
            }

            string normalizedText = text.Trim();

            if (normalizedText.Length > MaximumTextLength)
            {
                throw new DomainException(
                    $"Message text cannot exceed {MaximumTextLength} characters.");
            }

            text = normalizedText;

            if (attachmentId is not null ||
                latitude is not null ||
                longitude is not null ||
                locationLabel is not null)
            {
                throw new DomainException(
                    "Text message cannot carry a payload.");
            }
        }
        else if (type == MessageType.File)
        {
            if (text is not null ||
                latitude is not null ||
                longitude is not null ||
                locationLabel is not null)
            {
                throw new DomainException(
                    "File message cannot carry a payload.");
            }
        }
        else if (type == MessageType.Location)
        {
            if (text is not null || attachmentId is not null)
            {
                throw new DomainException(
                    "Location message cannot carry a body or attachment.");
            }

            if (latitude is null || longitude is null)
            {
                throw new DomainException(
                    "Location message requires coordinates.");
            }

            if (latitude is < -90 or > 90)
            {
                throw new DomainException(
                    "Location latitude must be between -90 and 90.");
            }

            if (longitude is < -180 or > 180)
            {
                throw new DomainException(
                    "Location longitude must be between -180 and 180.");
            }

            if (locationLabel is not null)
            {
                string normalizedLabel = locationLabel.Trim();

                if (normalizedLabel.Length > MaximumLocationLabelLength)
                {
                    throw new DomainException(
                        $"Location label cannot exceed {MaximumLocationLabelLength} characters.");
                }

                locationLabel = normalizedLabel.Length == 0
                    ? null
                    : normalizedLabel;
            }
        }
        else
        {
            throw new DomainException("Message type is not supported.");
        }

        if (string.IsNullOrWhiteSpace(clientMessageId))
        {
            throw new DomainException(
                "Message client reference is required.");
        }

        string normalizedClientMessageId = clientMessageId.Trim();

        if (normalizedClientMessageId.Length > MaximumClientMessageIdLength)
        {
            throw new DomainException(
                "Message client reference cannot exceed " +
                $"{MaximumClientMessageIdLength} characters.");
        }

        if (sentOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Message timestamps must be UTC.");
        }

        return new Message(
            MessageId.New(),
            conversationId,
            senderUserId,
            type,
            text,
            attachmentId,
            normalizedClientMessageId,
            sentOnUtc,
            latitude,
            longitude,
            locationLabel);
    }

    /// <summary>
    /// Set-once delivery stamp: the recipient's first fetch wins, any later
    /// call is a no-op. Still validates the timestamp, so a non-UTC argument
    /// is rejected even on the no-op path.
    /// </summary>
    public void MarkDelivered(DateTime deliveredOnUtc)
    {
        if (deliveredOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Message timestamps must be UTC.");
        }

        if (DeliveredOnUtc is not null)
        {
            return;
        }

        DeliveredOnUtc = deliveredOnUtc;
    }

    /// <summary>
    /// Set-once read stamp: an explicit read mark wins once, any later mark
    /// is a no-op. Still validates the timestamp, so a non-UTC argument is
    /// rejected even on the no-op path.
    /// </summary>
    public void MarkRead(DateTime readOnUtc)
    {
        if (readOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Message timestamps must be UTC.");
        }

        if (ReadOnUtc is not null)
        {
            return;
        }

        ReadOnUtc = readOnUtc;
    }
}
