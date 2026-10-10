using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Abstractions.Data;
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

    public GetConversationsQueryHandler(
        IChatDbContext dbContext,
        IChatUserLookup userLookup)
    {
        _dbContext = dbContext;
        _userLookup = userLookup;
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

            items.Add(new ConversationListItemResponse(
                conversation.Id.Value,
                new ParticipantResponse(
                    otherParticipant.Value,
                    profile?.DisplayName ?? string.Empty,
                    profile?.AvatarUrl),
                // STUB (Lane B): the Messages module does not exist in this
                // lane, so lastMessage is always null and unreadCount is
                // always 0. Wire both to Message once Lane B lands — the
                // field names are already frozen by the contract.
                LastMessage: null,
                UnreadCount: 0));
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
