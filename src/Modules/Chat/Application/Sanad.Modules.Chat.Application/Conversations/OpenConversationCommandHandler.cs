using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Application.Persistence;
using Sanad.Modules.Chat.Application.RateLimiting;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.Modules.Chat.Application.Conversations;

public sealed class OpenConversationCommandHandler
    : ICommandHandler<OpenConversationCommand, OpenConversationResponse>
{
    private readonly IChatDbContext _dbContext;
    private readonly IChatEligibilityChecker _eligibilityChecker;
    private readonly IChatBookingGateway _bookingGateway;
    private readonly IChatUserLookup _userLookup;
    private readonly IChatRateGate? _rateGate;

    public OpenConversationCommandHandler(
        IChatDbContext dbContext,
        IChatEligibilityChecker eligibilityChecker,
        IChatBookingGateway bookingGateway,
        IChatUserLookup? userLookup = null,
        IChatRateGate? rateGate = null)
    {
        _dbContext = dbContext;
        _eligibilityChecker = eligibilityChecker;
        _bookingGateway = bookingGateway;
        _userLookup = userLookup ?? new UnknownUserLookup();
        _rateGate = rateGate;
    }

    public async Task<Result<OpenConversationResponse>> Handle(
        OpenConversationCommand request,
        CancellationToken cancellationToken)
    {
        // Exactly one target: both or neither is the caller's own malformed
        // request — a 400 that reveals nothing about any resource. All
        // resource-shaped failures below stay the privacy 404.
        bool hasRecipient = request.Recipient is not null;
        bool hasBooking = request.BookingId is not null;

        if (hasRecipient == hasBooking)
        {
            return ConversationErrors.OpenTargetInvalid;
        }

        if (_rateGate is not null &&
            !_rateGate.TryAcquireOpen(request.Opener, out TimeSpan retryAfter))
        {
            return ChatRateErrors.Opens(retryAfter);
        }

        UserId recipient;
        bool bookingActive = true;

        if (hasBooking)
        {
            // Reopen-capable resolution: unknown bookings and bookings the
            // opener was never a party to collapse into the privacy 404,
            // but a terminal (cancelled/expired/refunded) booking still
            // resolves its parties so retained history can reopen.
            BookingOpenTarget? target = await _bookingGateway.ResolveForReopenAsync(
                request.BookingId!.Value,
                request.Opener,
                cancellationToken);

            if (target is null)
            {
                return ConversationErrors.NotFound;
            }

            recipient = target.Counterpart;
            bookingActive = target.IsActive;
        }
        else
        {
            recipient = request.Recipient!.Value;
        }

        // Reopening an existing visible/hidden conversation checks
        // membership and the caller's entitlement to its retained history —
        // never current initiation eligibility, so a terminal booking alone
        // cannot lock the parties out of their history. Creating a new
        // conversation checks current initiation eligibility instead.
        Conversation? existing = await FindExistingAsync(
            request.Opener,
            recipient,
            request.BookingId,
            cancellationToken);

        if (existing is not null)
        {
            if (!existing.IsParticipant(request.Opener))
            {
                return ConversationErrors.NotFound;
            }

            UserProfile? recipientProfile = await _userLookup.GetProfileAsync(
                recipient,
                cancellationToken);

            if (recipientProfile is null)
            {
                return ConversationErrors.NotFound;
            }

            if (existing.IsHiddenFor(request.Opener))
            {
                // Reopen restores the opener's visibility only. The other
                // party's hidden flag and LastActivityOnUtc are untouched.
                // Incoming sends never unhide: only an explicit open does.
                existing.Unhide(request.Opener);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return new OpenConversationResponse(
                existing.Id.Value,
                Created: false);
        }

        if (hasBooking && !bookingActive)
        {
            return ConversationErrors.NotFound;
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

        Conversation conversation = Conversation.Create(
            request.Opener,
            recipient,
            request.BookingId is null
                ? ConversationScope.Direct
                : ConversationScope.Booking,
            request.BookingId,
            DateTime.UtcNow);

        _dbContext.Conversations.Add(conversation);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            ChatPersistenceConflicts.IsUniqueViolation(exception))
        {
            // A concurrent open won the pair/booking key race. Catch only
            // the identified unique conflict, clear the stale tracked add,
            // and re-read the scoped authoritative state — the winner.
            if (_dbContext is DbContext context)
            {
                context.Entry(conversation).State = EntityState.Detached;
            }

            Conversation? winner = await FindExistingAsync(
                request.Opener,
                recipient,
                request.BookingId,
                cancellationToken);

            if (winner is not null)
            {
                return new OpenConversationResponse(
                    winner.Id.Value,
                    Created: false);
            }

            throw;
        }

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

    /// <summary>
    /// Fallback when no user lookup is wired (pre-existing focused tests):
    /// the recipient is assumed retained. Production always injects the
    /// real lookup.
    /// </summary>
    private sealed class UnknownUserLookup : IChatUserLookup
    {
        public Task<UserProfile?> GetProfileAsync(
            UserId userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<UserProfile?>(
                new UserProfile(userId, string.Empty, null, null));
    }
}
