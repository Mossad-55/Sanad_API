using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Application.Abstractions.Data;
using Ahlena.Modules.Chat.Application.Conversations;
using Ahlena.Modules.Chat.Application.Realtime;
using Ahlena.Modules.Chat.Domain.Conversations;
using Ahlena.Modules.Chat.Domain.Privacy;

namespace Ahlena.Modules.Chat.Application.Presence;

/// <summary>
/// Authorized presence snapshot for one conversation's counterpart.
/// `isOnline` is null when undisclosed (privacy off) or unknown — never a
/// leaked offline; `lastSeenOnUtc` is null when withheld or unknown after a
/// restart. Either-direction block suppresses both fields without removing
/// existing REST history access. Only authorized joined conversations
/// subscribe to counterpart presence; this snapshot is the documented
/// client reset after privacy changes, authorization changes, foreground,
/// and reconnect.
/// </summary>
public sealed record ConversationPresenceResponse(
    Guid UserId,
    bool? IsOnline,
    DateTime? LastSeenOnUtc);

public sealed record GetConversationPresenceQuery(
    UserId Requester,
    ConversationId ConversationId)
    : IQuery<ConversationPresenceResponse>;

public sealed class GetConversationPresenceQueryHandler
    : IQueryHandler<GetConversationPresenceQuery, ConversationPresenceResponse>
{
    private readonly IChatDbContext _dbContext;
    private readonly IPresenceTracker? _presence;

    public GetConversationPresenceQueryHandler(
        IChatDbContext dbContext,
        IPresenceTracker? presence = null)
    {
        _dbContext = dbContext;
        _presence = presence;
    }

    public async Task<Result<ConversationPresenceResponse>> Handle(
        GetConversationPresenceQuery request,
        CancellationToken cancellationToken)
    {
        Conversation? conversation = await _dbContext.Conversations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == request.ConversationId,
                cancellationToken);

        if (conversation is null ||
            !conversation.IsParticipant(request.Requester) ||
            conversation.IsHiddenFor(request.Requester))
        {
            return Result<ConversationPresenceResponse>.Failure(
                ConversationErrors.NotFound);
        }

        UserId counterpart =
            conversation.ParticipantOne == request.Requester
                ? conversation.ParticipantTwo
                : conversation.ParticipantOne;

        // Either-direction block suppresses all presence between the pair.
        bool blocked = await _dbContext.Blocks
            .AsNoTracking()
            .AnyAsync(
                block =>
                    (block.BlockerUserId == request.Requester &&
                     block.BlockedUserId == counterpart) ||
                    (block.BlockerUserId == counterpart &&
                     block.BlockedUserId == request.Requester),
                cancellationToken);

        if (blocked)
        {
            return Result<ConversationPresenceResponse>.Success(
                new ConversationPresenceResponse(
                    counterpart.Value, null, null));
        }

        ChatPrivacySettings? settings = await _dbContext.PrivacySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.OwnerUserId == counterpart,
                cancellationToken);

        if (settings is null || _presence is null)
        {
            return Result<ConversationPresenceResponse>.Success(
                new ConversationPresenceResponse(
                    counterpart.Value, null, null));
        }

        DateTime now = DateTime.UtcNow;

        // When online sharing is off but last-seen sharing is on, the
        // snapshot still carries permitted last seen without ever emitting
        // an online-state event that would leak liveness.
        bool? isOnline = settings.ShowOnlineStatus
            ? _presence.IsOnline(counterpart, now)
            : null;

        DateTime? lastSeen = settings.ShareLastSeen
            ? _presence.GetLastSeen(counterpart, now)
            : null;

        return Result<ConversationPresenceResponse>.Success(
            new ConversationPresenceResponse(
                counterpart.Value, isOnline, lastSeen));
    }
}
