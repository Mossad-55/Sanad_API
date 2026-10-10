using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Chat.Domain.Conversations;

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
    /// </summary>
    public void UpdateLastActivity(DateTime lastActivityOnUtc)
    {
        if (lastActivityOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Conversation timestamps must be UTC.");
        }

        LastActivityOnUtc = lastActivityOnUtc;
    }

    private static (UserId First, UserId Second) NormalizePair(
        UserId participantA,
        UserId participantB) =>
        participantA.Value.CompareTo(participantB.Value) <= 0
            ? (participantA, participantB)
            : (participantB, participantA);
}
