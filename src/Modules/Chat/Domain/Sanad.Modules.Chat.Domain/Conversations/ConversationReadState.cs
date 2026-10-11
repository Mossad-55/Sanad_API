using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Chat.Domain.Conversations;

/// <summary>
/// Per-participant read mark for exactly one conversation: the id of the
/// last message this user has read plus when the mark was taken. One row per
/// (conversation, user) — the unique index is the idempotency backstop, and
/// a repeat mark updates the single row instead of inserting a second one.
/// </summary>
/// <remarks>
/// LANE B SEAM — Lane A has no <c>Message</c> table, so it cannot and does
/// not verify that <c>LastReadMessageId</c> names a real message in this
/// conversation, nor that it is the newest one. Lane A only records the
/// participant's mark. Lane B interprets marks against messages (unread
/// counts, read ticks) and owns any ordering/monotonicity rule; until then
/// the mark is plain last-write-wins.
///
/// A read mark is deliberately NOT conversation activity: it never touches
/// <see cref="Conversation.LastActivityOnUtc"/> (that stamp is owned by the
/// message flow), and it is not visibility state, so hiding, unhiding, or
/// reopening a conversation neither creates nor clears a mark.
/// </remarks>
public sealed class ConversationReadState
    : AggregateRoot<ConversationReadStateId>
{
    private ConversationReadState()
    {
    }

    private ConversationReadState(
        ConversationReadStateId id,
        ConversationId conversationId,
        UserId userId,
        Guid lastReadMessageId,
        DateTime readOnUtc)
        : base(id)
    {
        ConversationId = conversationId;
        UserId = userId;
        LastReadMessageId = lastReadMessageId;
        ReadOnUtc = readOnUtc;
    }

    public ConversationId ConversationId { get; private set; }

    public UserId UserId { get; private set; } = default!;

    public Guid LastReadMessageId { get; private set; }

    public DateTime ReadOnUtc { get; private set; }

    public static ConversationReadState Create(
        ConversationId conversationId,
        UserId userId,
        Guid lastReadMessageId,
        DateTime readOnUtc)
    {
        if (conversationId.Value == Guid.Empty)
        {
            throw new DomainException(
                "Read state conversation is required.");
        }

        if (userId == UserId.Empty)
        {
            throw new DomainException(
                "Read state user is required.");
        }

        if (lastReadMessageId == Guid.Empty)
        {
            throw new DomainException(
                "Read state message reference is required.");
        }

        if (readOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Read state timestamps must be UTC.");
        }

        return new ConversationReadState(
            ConversationReadStateId.New(),
            conversationId,
            userId,
            lastReadMessageId,
            readOnUtc);
    }

    /// <summary>
    /// Re-mark path of the idempotent upsert: moves the mark forward (or
    /// anywhere — see the Lane B seam above) and refreshes the stamp, on the
    /// same row. Never a second row for the same (conversation, user).
    /// </summary>
    public void Mark(Guid lastReadMessageId, DateTime readOnUtc)
    {
        if (lastReadMessageId == Guid.Empty)
        {
            throw new DomainException(
                "Read state message reference is required.");
        }

        if (readOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Read state timestamps must be UTC.");
        }

        LastReadMessageId = lastReadMessageId;
        ReadOnUtc = readOnUtc;
    }
}
