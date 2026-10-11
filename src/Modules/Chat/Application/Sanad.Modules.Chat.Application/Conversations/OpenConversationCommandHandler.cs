using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.Modules.Chat.Application.Conversations;

public sealed class OpenConversationCommandHandler
    : ICommandHandler<OpenConversationCommand, OpenConversationResponse>
{
    private readonly IChatDbContext _dbContext;
    private readonly IChatEligibilityChecker _eligibilityChecker;
    private readonly IChatBookingGateway _bookingGateway;

    public OpenConversationCommandHandler(
        IChatDbContext dbContext,
        IChatEligibilityChecker eligibilityChecker,
        IChatBookingGateway bookingGateway)
    {
        _dbContext = dbContext;
        _eligibilityChecker = eligibilityChecker;
        _bookingGateway = bookingGateway;
    }

    public async Task<Result<OpenConversationResponse>> Handle(
        OpenConversationCommand request,
        CancellationToken cancellationToken)
    {
        // Exactly one target (Opus delta): both or neither is the caller's
        // own malformed request — a 400 that reveals nothing about any
        // resource. All resource-shaped failures below stay the privacy 404.
        bool hasRecipient = request.Recipient is not null;
        bool hasBooking = request.BookingId is not null;

        if (hasRecipient == hasBooking)
        {
            return ConversationErrors.OpenTargetInvalid;
        }

        UserId recipient;

        if (hasBooking)
        {
            // Booking chat: the caller supplies the booking only; the
            // backend resolves the counterpart. Unknown booking, a booking
            // the opener is not a party to, and a cancelled/expired/
            // refunded booking all collapse into one null → privacy 404.
            UserId? resolved = await _bookingGateway.ResolveCounterpartAsync(
                request.BookingId!.Value,
                request.Opener,
                cancellationToken);

            if (resolved is null)
            {
                return ConversationErrors.NotFound;
            }

            recipient = resolved.Value;
        }
        else
        {
            recipient = request.Recipient!.Value;
        }

        bool canOpen = await _eligibilityChecker.CanOpenAsync(
            request.Opener,
            recipient,
            request.BookingId,
            cancellationToken);

        if (!canOpen)
        {
            // Privacy: self-open, unknown/ineligible recipient, and an
            // unusable booking reference all fail identically, and the
            // caller can never tell which one it was.
            return ConversationErrors.NotFound;
        }

        Conversation? existing = await FindExistingAsync(
            request.Opener,
            recipient,
            request.BookingId,
            cancellationToken);

        if (existing is not null)
        {
            if (existing.IsHiddenFor(request.Opener))
            {
                // Reopen restores the opener's visibility only. The other
                // party's hidden flag and LastActivityOnUtc are untouched.
                existing.Unhide(request.Opener);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return new OpenConversationResponse(
                existing.Id.Value,
                Created: false);
        }

        Conversation conversation = Conversation.Create(
            request.Opener,
            recipient,
            request.BookingId is null
                ? ConversationScope.Direct
                : ConversationScope.Booking,
            request.BookingId,
            DateTime.UtcNow);

        _dbContext.Conversations.Add(conversation);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new OpenConversationResponse(
            conversation.Id.Value,
            Created: true);
    }

    /// <summary>
    /// Both directions are queried and the domain key decides equality, so
    /// the pair normalization stays in one place (<see cref="Conversation.MatchesKey"/>).
    /// A booking-scoped row and a direct row between the same pair are
    /// different conversations.
    /// </summary>
    private async Task<Conversation?> FindExistingAsync(
        UserId opener,
        UserId recipient,
        BookingId? bookingId,
        CancellationToken cancellationToken)
    {
        List<Conversation> candidates = await _dbContext.Conversations
            .Where(conversation =>
                (conversation.ParticipantOne == opener &&
                 conversation.ParticipantTwo == recipient) ||
                (conversation.ParticipantOne == recipient &&
                 conversation.ParticipantTwo == opener))
            .ToListAsync(cancellationToken);

        return candidates.FirstOrDefault(conversation =>
            conversation.MatchesKey(opener, recipient, bookingId));
    }
}
