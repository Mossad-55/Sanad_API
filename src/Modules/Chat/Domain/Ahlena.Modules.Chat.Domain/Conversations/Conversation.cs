using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Chat.Domain.Conversations;

/// <summary>
/// Strictly 1:1 conversation over an ordered participant pair, optionally
/// scoped to a booking. Duplication keys on (normalized pair, booking ref);
/// hiding is per-user and erases nothing.
/// </summary>
public sealed class Conversation : AggregateRoot<ConversationId>
{
    private Conversation()
    {
    }

    private Conversation(
        ConversationId id,
        UserId participantOne,
        UserId participantTwo,
        ConversationScope scope,
        BookingId? bookingId,
        DateTime createdOnUtc)
        : base(id)
    {
        ParticipantOne = participantOne;
        ParticipantTwo = participantTwo;
        Scope = scope;
        BookingId = bookingId;
        CreatedOnUtc = createdOnUtc;
        LastActivityOnUtc = createdOnUtc;
    }

    public UserId ParticipantOne { get; private set; } = default!;

    public UserId ParticipantTwo { get; private set; } = default!;

    public ConversationScope Scope { get; private set; }

    public BookingId? BookingId { get; private set; }

    public bool HiddenForParticipantOne { get; private set; }

    public bool HiddenForParticipantTwo { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime LastActivityOnUtc { get; private set; }

    /// <summary>
    /// Chat Plus commit-safe change cursor: every durable message/receipt
    /// change in this conversation consumes the next value inside the same
    /// transaction that writes the change, so a sync reader that captured
    /// revision N sees exactly the changes committed up to N and nothing
    /// committed later can hide below a returned cursor. The property is an
    /// EF concurrency token: concurrent writers collide on it and retry,
    /// which serializes revision assignment per conversation. A rolled-back
    /// transaction rolls its increment back too, so revisions never skip.
    /// </summary>
    public long ChangeRevision { get; private set; }

    public static Conversation Create(
        UserId participantA,
        UserId participantB,
        ConversationScope scope,
        BookingId? bookingId,
        DateTime createdOnUtc)
    {
        if (participantA == UserId.Empty || participantB == UserId.Empty)
        {
            throw new DomainException(
                "Conversation participants are required.");
        }

        if (participantA == participantB)
        {
            throw new DomainException(
                "Conversation participants must be two different users.");
        }

        if (bookingId is { } booking && booking.Value == Guid.Empty)
        {
            throw new DomainException(
                "Conversation booking reference cannot be empty.");
        }

        if (scope == ConversationScope.Booking && bookingId is null)
        {
            throw new DomainException(
                "Booking-scoped conversation requires a booking reference.");
        }

        if (scope == ConversationScope.Direct && bookingId is not null)
        {
            throw new DomainException(
                "Direct conversation cannot carry a booking reference.");
        }

        if (createdOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Conversation timestamps must be UTC.");
        }

        (UserId first, UserId second) = NormalizePair(participantA, participantB);

        return new Conversation(
            ConversationId.New(),
            first,
            second,
            scope,
            bookingId,
            createdOnUtc);
    }

    /// <summary>
    /// Open-or-get key equality: same normalized participant pair in either
    /// direction plus the same booking reference (null = direct scope).
    /// </summary>
    public bool MatchesKey(
        UserId participantA,
        UserId participantB,
        BookingId? bookingId)
    {
        if (participantA == UserId.Empty || participantB == UserId.Empty)
        {
            return false;
        }

        (UserId first, UserId second) = NormalizePair(participantA, participantB);

        return ParticipantOne == first
            && ParticipantTwo == second
            && BookingId == bookingId;
    }

    public bool IsParticipant(UserId userId) =>
        userId != UserId.Empty
        && (userId == ParticipantOne || userId == ParticipantTwo);

    public void Hide(UserId participant)
    {
        if (participant == ParticipantOne)
        {
            HiddenForParticipantOne = true;
            return;
        }

        if (participant == ParticipantTwo)
        {
            HiddenForParticipantTwo = true;
            return;
        }

        throw new DomainException(
            "Only a participant can hide a conversation.");
    }

    /// <summary>
    /// Reopening a hidden conversation (a fresh open after a per-user hide)
    /// restores visibility for that participant only; the other party's
    /// flag is never touched. Nothing is erased on either side.
    /// </summary>
    public void Unhide(UserId participant)
    {
        if (participant == ParticipantOne)
        {
            HiddenForParticipantOne = false;
            return;
        }

        if (participant == ParticipantTwo)
        {
            HiddenForParticipantTwo = false;
            return;
        }

        throw new DomainException(
            "Only a participant can restore a conversation.");
    }

    public bool IsHiddenFor(UserId participant)
    {
        if (participant == ParticipantOne)
        {
            return HiddenForParticipantOne;
        }

        if (participant == ParticipantTwo)
        {
            return HiddenForParticipantTwo;
        }

        throw new DomainException(
            "Only a participant can check conversation visibility.");
    }

    /// <summary>
    /// Last-activity stamp, updated by the message flow once messages exist.
    /// Monotonic: a concurrent delayed save can never regress it — an older
    /// stamp is ignored rather than applied.
    /// </summary>
    public void UpdateLastActivity(DateTime lastActivityOnUtc)
    {
        if (lastActivityOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Conversation timestamps must be UTC.");
        }

        if (lastActivityOnUtc <= LastActivityOnUtc)
        {
            return;
        }

        LastActivityOnUtc = lastActivityOnUtc;
    }

    /// <summary>
    /// Consumes the next change revision for one durable change. Called once
    /// per change row inside the writer's transaction; the concurrency token
    /// on <see cref="ChangeRevision"/> turns a lost race into a retryable
    /// persistence conflict instead of a duplicated revision.
    /// </summary>
    public long TakeNextRevision()
    {
        ChangeRevision++;
        return ChangeRevision;
    }

    private static (UserId First, UserId Second) NormalizePair(
        UserId participantA,
        UserId participantB) =>
        participantA.Value.CompareTo(participantB.Value) <= 0
            ? (participantA, participantB)
            : (participantB, participantA);
}
