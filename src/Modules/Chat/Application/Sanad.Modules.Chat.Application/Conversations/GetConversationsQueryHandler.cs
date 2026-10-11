using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Application.Messages;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.Modules.Chat.Application.Conversations;

public sealed class GetConversationsQueryHandler
    : IQueryHandler<
        GetConversationsQuery,
        PagedResult<ConversationListItemResponse>>
{
    private const int MaxPageSize = 100;

    private readonly IChatDbContext _dbContext;
    private readonly IChatUserLookup _userLookup;
    private readonly IChatFacilityLookup _facilityLookup;

    public GetConversationsQueryHandler(
        IChatDbContext dbContext,
        IChatUserLookup userLookup,
        IChatFacilityLookup facilityLookup)
    {
        _dbContext = dbContext;
        _userLookup = userLookup;
        _facilityLookup = facilityLookup;
    }

    public async Task<Result<PagedResult<ConversationListItemResponse>>> Handle(
        GetConversationsQuery request,
        CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        IQueryable<Conversation> visible = _dbContext.Conversations
            .AsNoTracking()
            .Where(conversation =>
                // Participant authorization: only the requester's own
                // conversations, minus the rows the requester hid.
                // Unknown/foreign rows are simply not visible.
                (conversation.ParticipantOne == request.Requester ||
                 conversation.ParticipantTwo == request.Requester) &&
                (conversation.ParticipantOne != request.Requester ||
                 !conversation.HiddenForParticipantOne) &&
                (conversation.ParticipantTwo != request.Requester ||
                 !conversation.HiddenForParticipantTwo));

        int totalCount = await visible.CountAsync(cancellationToken);

        List<Conversation> conversations = await visible
            .OrderByDescending(conversation => conversation.LastActivityOnUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // Lane B: one pass over the message facts for this page (last
        // message + not-read count per conversation), computed by the same
        // rule that backs endpoint 8 so the two can never disagree.
        MessageFacts facts = await MessageReadRules.LoadAsync(
            _dbContext,
            request.Requester,
            conversations,
            cancellationToken);

        var items = new List<ConversationListItemResponse>(
            conversations.Count);

        foreach (Conversation conversation in conversations)
        {
            UserId otherParticipant =
                conversation.ParticipantOne == request.Requester
                    ? conversation.ParticipantTwo
                    : conversation.ParticipantOne;

            UserProfile? profile = await _userLookup.GetProfileAsync(
                otherParticipant,
                cancellationToken);

            // Opus delta: a care-home owner participant presents as the
            // facility — name and cover resolve over the owner's user id.
            // Users without a facility keep their personal profile.
            string displayName = profile?.DisplayName ?? string.Empty;
            string? avatarUrl = profile?.AvatarUrl;

            FacilityPresentation? facility = await _facilityLookup
                .GetForOwnerAsync(otherParticipant, cancellationToken);

            if (facility is not null)
            {
                displayName = facility.DisplayName;
                avatarUrl = facility.CoverImageUrl;
            }

            items.Add(new ConversationListItemResponse(
                conversation.Id.Value,
                new ParticipantResponse(
                    otherParticipant.Value,
                    displayName,
                    avatarUrl,
                    profile?.AccountType),
                // LoadAsync gives every conversation it was handed an entry,
                // so both reads are safe indexers.
                facts.LastMessages[conversation.Id],
                facts.UnreadCounts[conversation.Id]));
        }

        return Result<PagedResult<ConversationListItemResponse>>.Success(
            new PagedResult<ConversationListItemResponse>(
                items,
                page,
                pageSize,
                totalCount,
                (int)Math.Ceiling(totalCount / (double)pageSize)));
    }
}
