using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.Modules.Chat.Application.Messages;

/// <summary>
/// Endpoint 8 — the requester's unread totals over the conversations they
/// can see. Unread = a message the requester did not send that the read rule
/// (<see cref="MessageReadRules"/>) does not count as read for them;
/// conversations the requester hid are excluded entirely (documented
/// decision: a hidden conversation is not visible anywhere, so it cannot
/// contribute a badge — reopening restores it).
/// </summary>
public sealed record GetUnreadCountQuery(UserId Requester)
    : IQuery<UnreadCountResponse>;

public sealed class GetUnreadCountQueryHandler
    : IQueryHandler<GetUnreadCountQuery, UnreadCountResponse>
{
    private readonly IChatDbContext _dbContext;

    public GetUnreadCountQueryHandler(IChatDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<UnreadCountResponse>> Handle(
        GetUnreadCountQuery request,
        CancellationToken cancellationToken)
    {
        // Participant authorization, not a 404 path: a requester with no
        // visible conversations simply answers 0/0.
        List<Conversation> conversations = await _dbContext.Conversations
            .AsNoTracking()
            .Where(conversation =>
                (conversation.ParticipantOne == request.Requester ||
                 conversation.ParticipantTwo == request.Requester) &&
                (conversation.ParticipantOne != request.Requester ||
                 !conversation.HiddenForParticipantOne) &&
                (conversation.ParticipantTwo != request.Requester ||
                 !conversation.HiddenForParticipantTwo))
            .ToListAsync(cancellationToken);

        MessageFacts facts = await MessageReadRules.LoadAsync(
            _dbContext,
            request.Requester,
            conversations,
            cancellationToken);

        int unreadMessages = 0;
        int unreadConversations = 0;

        foreach (Conversation conversation in conversations)
        {
            int unread = facts.UnreadCounts[conversation.Id];

            unreadMessages += unread;

            if (unread > 0)
            {
                unreadConversations++;
            }
        }

        return Result<UnreadCountResponse>.Success(
            new UnreadCountResponse(
                unreadMessages,
                unreadConversations));
    }
}
