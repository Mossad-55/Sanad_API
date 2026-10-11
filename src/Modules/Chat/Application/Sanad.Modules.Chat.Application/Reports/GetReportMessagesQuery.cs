using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Application.Messages;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Domain.Messages;
using Sanad.Modules.Chat.Domain.Reports;

namespace Sanad.Modules.Chat.Application.Reports;

/// <summary>
/// Endpoint 13 — the reported conversation's messages, the only
/// non-participant read path in the slice. The report must exist first (its
/// id is the caller's entire authorization to see this conversation), then
/// every message of its conversation is returned with the endpoint 3 item
/// shape, newest first — the contract fixes the item shape and the read
/// boundary, not the ordering, so the two read surfaces stay comparable.
/// <para>
/// Read-only: unlike endpoint 3 this path never stamps delivery, never
/// writes a read state, and never saves — Support observes, it does not
/// participate. The <c>readOnUtc</c> tick uses the same rule as endpoint 3
/// with the reader fixed to whoever did not send the message (the only
/// reader that exists once the viewer is not a participant), so the view can
/// never disagree with what the participants saw.
/// </para>
/// </summary>
public sealed record GetReportMessagesQuery(ReportId ReportId)
    : IQuery<IReadOnlyList<MessageItemResponse>>;

public sealed class GetReportMessagesQueryHandler
    : IQueryHandler<GetReportMessagesQuery, IReadOnlyList<MessageItemResponse>>
{
    private readonly IChatDbContext _dbContext;

    public GetReportMessagesQueryHandler(IChatDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<MessageItemResponse>>> Handle(
        GetReportMessagesQuery request,
        CancellationToken cancellationToken)
    {
        Report? report = await _dbContext.Reports
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == request.ReportId,
                cancellationToken);

        if (report is null)
        {
            return Result<IReadOnlyList<MessageItemResponse>>.Failure(
                ReportErrors.NotFound);
        }

        Conversation? conversation = await _dbContext.Conversations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == report.ConversationId,
                cancellationToken);

        // Integrity backstop: a report cascades away with its conversation,
        // so this row cannot go missing through any supported path. Answer
        // the same plain 404 as an unknown report rather than a 500.
        if (conversation is null)
        {
            return Result<IReadOnlyList<MessageItemResponse>>.Failure(
                ReportErrors.NotFound);
        }

        List<Message> rows = await _dbContext.Messages
            .AsNoTracking()
            .Where(message =>
                message.ConversationId == conversation.Id)
            .OrderByDescending(message => message.SentOnUtc)
            .ThenByDescending(message => message.Id)
            .ToListAsync(cancellationToken);

        IReadOnlyDictionary<UserId, ReadMark> marks =
            await MessageReadRules.LoadMarksAsync(
                _dbContext,
                conversation.Id,
                [conversation.ParticipantOne, conversation.ParticipantTwo],
                cancellationToken);

        var items = new List<MessageItemResponse>(rows.Count);

        foreach (Message row in rows)
        {
            // The reader of an item is whoever did not send it — endpoint
            // 3's rule with no viewer in the way.
            UserId reader = row.SenderUserId == conversation.ParticipantOne
                ? conversation.ParticipantTwo
                : conversation.ParticipantOne;

            ReadMark readerMark = marks.TryGetValue(
                    reader,
                    out ReadMark mark)
                ? mark
                : ReadMark.None;

            items.Add(MessageItemFactory.Create(
                row,
                MessageReadRules.ReadStamp(row, readerMark)));
        }

        return Result<IReadOnlyList<MessageItemResponse>>.Success(items);
    }
}
