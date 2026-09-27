using Sanad.BuildingBlocks.Domain.Abstractions;

namespace Sanad.Modules.Notifications.Domain.Notifications;

public sealed class Notification : Entity<Guid>
{
    public const int MaximumCategoryLength = 80;
    public const int MaximumTypeLength = 120;
    public const int MaximumTitleLength = 240;
    public const int MaximumBodyLength = 2000;
    public const int MaximumDestinationKindLength = 80;
    public const int MaximumIdempotencyKeyLength = 240;

    private Notification() { }

    private Notification(
        Guid id,
        Guid recipientUserId,
        string category,
        string type,
        string title,
        string body,
        string destinationEntityKind,
        Guid destinationEntityId,
        DateTime createdOnUtc,
        string? idempotencyKey) : base(id)
    {
        RecipientUserId = recipientUserId;
        Category = category;
        Type = type;
        Title = title;
        Body = body;
        DestinationEntityKind = destinationEntityKind;
        DestinationEntityId = destinationEntityId;
        CreatedOnUtc = createdOnUtc;
        IdempotencyKey = idempotencyKey;
    }

    public Guid RecipientUserId { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public string Type { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public string DestinationEntityKind { get; private set; } = string.Empty;
    public Guid DestinationEntityId { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? ReadOnUtc { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public static Notification Create(
        Guid recipientUserId,
        string category,
        string type,
        string title,
        string body,
        string destinationEntityKind,
        Guid destinationEntityId,
        DateTime createdOnUtc,
        string? idempotencyKey = null)
    {
        if (recipientUserId == Guid.Empty || destinationEntityId == Guid.Empty)
            throw new ArgumentException("Notification identifiers are required.");
        if (createdOnUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Notification time must be UTC.");
        if (idempotencyKey is { Length: > MaximumIdempotencyKeyLength })
            throw new ArgumentException("Notification idempotency key is too long.");
        return new Notification(Guid.NewGuid(), recipientUserId, category, type, title, body,
            destinationEntityKind, destinationEntityId, createdOnUtc, idempotencyKey);
    }

    public void MarkRead(DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc) throw new ArgumentException("Read time must be UTC.");
        ReadOnUtc ??= utcNow;
    }
}
