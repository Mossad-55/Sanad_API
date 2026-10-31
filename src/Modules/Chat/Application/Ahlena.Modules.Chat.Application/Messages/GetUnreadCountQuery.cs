using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Application.Abstractions.Data;
using Ahlena.Modules.Chat.Domain.Conversations;

namespace Ahlena.Modules.Chat.Application.Messages;

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
    /// <summary>
    /// Bounded scan: visible conversations are aggregated in fixed pages so
    /// retained histories and large fleets never load unbounded state into
    /// memory. Totals stay exact — every page uses the same read rule.
    /// </summary>
    private const int ScanPageSize = 200;

    private readonly IChatDbContext _dbContext;

    public GetUnreadCountQueryHandler(IChatDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<UnreadCountResponse>> Handle(
        GetUnreadCountQuery request,
        CancellationToken cancellationToken)
    {
        int unreadMessages = 0;
        int unreadConversations = 0;
        int skip = 0;

        while (true)
        {
            // Participant authorization, not a 404 path: a requester with no
            // visible conversations simply answers 0/0.
            List<Conversation> page = await _dbContext.Conversations
                .AsNoTracking()
                .Where(conversation =>
                    (conversation.ParticipantOne == request.Requester ||
                     conversation.ParticipantTwo == request.Requester) &&
                    (conversation.ParticipantOne != request.Requester ||
                     !conversation.HiddenForParticipantOne) &&
                    (conversation.ParticipantTwo != request.Requester ||
                     !conversation.HiddenForParticipantTwo))
                .OrderBy(conversation => conversation.Id)
                .Skip(skip)
                .Take(ScanPageSize)
                .ToListAsync(cancellationToken);

            if (page.Count == 0)
            {
                break;
            }

            MessageFacts facts = await MessageReadRules.LoadAsync(
                _dbContext,
                request.Requester,
                page,
                cancellationToken);

            foreach (Conversation conversation in page)
            {
                int unread = facts.UnreadCounts[conversation.Id];

                unreadMessages += unread;

                if (unread > 0)
                {
                    unreadConversations++;
                }
            }

            if (page.Count < ScanPageSize)
            {
                break;
            }

            skip += page.Count;
        }

        return Result<UnreadCountResponse>.Success(
            new UnreadCountResponse(
                unreadMessages,
                unreadConversations));
    }
}
