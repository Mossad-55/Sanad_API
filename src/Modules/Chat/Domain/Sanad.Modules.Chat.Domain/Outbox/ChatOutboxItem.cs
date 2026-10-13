using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Domain.Messages;

namespace Sanad.Modules.Chat.Domain.Outbox;

/// <summary>
/// Durable new-message notification intent, written in the same Chat
/// transaction as the message itself. The dispatcher publishes it after
/// commit (in-app alert + realtime fan-out) with bounded retries; a crash
/// between commit and publish loses nothing because the row stays pending.
/// At most one row per (message, recipient); the Notifications-side producer
/// key dedupes any redelivery.
/// </summary>
public sealed class ChatOutboxItem : AggregateRoot<ChatOutboxItemId>
{
    public const int MaximumAttempts = 8;

    private ChatOutboxItem()
    {
    }

    private ChatOutboxItem(
        ChatOutboxItemId id,
        ConversationId conversationId,
        MessageId messageId,
        UserId recipientUserId,
        DateTime createdOnUtc,
        DateTime nextAttemptOnUtc)
        : base(id)
    {
        ConversationId = conversationId;
        MessageId = messageId;
        RecipientUserId = recipientUserId;
        CreatedOnUtc = createdOnUtc;
        NextAttemptOnUtc = nextAttemptOnUtc;
    }

    public ConversationId ConversationId { get; private set; }

    public MessageId MessageId { get; private set; }

    public UserId RecipientUserId { get; private set; } = default!;

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime? PublishedOnUtc { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTime NextAttemptOnUtc { get; private set; }

    public Guid? ClaimToken { get; private set; }

    /// <summary>Last claim instant; a live claim older than the stale window
    /// may be reclaimed after a crash.</summary>
    public DateTime? ClaimedOnUtc { get; private set; }

    public string? LastErrorCode { get; private set; }

    public static ChatOutboxItem Create(
        ConversationId conversationId,
        MessageId messageId,
        UserId recipientUserId,
        DateTime createdOnUtc)
    {
        if (conversationId.Value == Guid.Empty)
        {
            throw new DomainException(
                "Chat outbox conversation is required.");
        }

        if (messageId.Value == Guid.Empty)
        {
            throw new DomainException(
                "Chat outbox message is required.");
        }

        if (recipientUserId == UserId.Empty)
        {
            throw new DomainException(
                "Chat outbox recipient is required.");
        }

        if (createdOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Chat outbox timestamps must be UTC.");
        }

        return new ChatOutboxItem(
            ChatOutboxItemId.New(),
            conversationId,
            messageId,
            recipientUserId,
            createdOnUtc,
            createdOnUtc);
    }

    public void MarkPublished(DateTime publishedOnUtc, Guid claimToken)
    {
        if (ClaimToken != claimToken)
        {
            throw new DomainException(
                "Chat outbox claim does not match.");
        }

        PublishedOnUtc = publishedOnUtc;
        LastErrorCode = null;
    }

    /// <summary>
    /// Drop a pending intent that is no longer eligible (recipient left,
    /// hid, blocked, or deactivated between send-commit and dispatch).
    /// Suppressed rows leave the due set like published rows, but keep the
    /// reason in <see cref="LastErrorCode"/> for operator visibility.
    /// </summary>
    public void MarkSuppressed(string errorCode, DateTime suppressedOnUtc, Guid claimToken)
    {
        if (ClaimToken != claimToken)
        {
            throw new DomainException(
                "Chat outbox claim does not match.");
        }

        if (string.IsNullOrWhiteSpace(errorCode))
        {
            throw new DomainException(
                "Chat outbox suppression reason is required.");
        }

        PublishedOnUtc = suppressedOnUtc;
        LastErrorCode = errorCode;
    }

    public void MarkAttemptFailed(
        string errorCode,
        DateTime now,
        Guid claimToken,
        TimeSpan nextDelay)
    {
        if (ClaimToken != claimToken)
        {
            throw new DomainException(
                "Chat outbox claim does not match.");
        }

        AttemptCount++;
        LastErrorCode = errorCode;
        NextAttemptOnUtc = now + nextDelay;
        ClaimToken = null;
    }

    public void Claim(Guid claimToken, DateTime claimedOnUtc)
    {
        if (claimedOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Chat outbox timestamps must be UTC.");
        }

        ClaimToken = claimToken;
        ClaimedOnUtc = claimedOnUtc;
    }

    public void ReleaseClaim() => ClaimToken = null;

    /// <summary>Exponential backoff with jitter, 5s base doubling to 60min.</summary>
    public static TimeSpan DelayForAttempt(int attemptCount, int jitterMilliseconds)
    {
        double seconds = Math.Min(5 * Math.Pow(2, Math.Max(0, attemptCount - 1)), 3600);
        return TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(jitterMilliseconds);
    }
}
