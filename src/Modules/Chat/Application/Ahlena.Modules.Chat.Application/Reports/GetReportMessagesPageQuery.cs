using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Application.Abstractions.Data;
using Ahlena.Modules.Chat.Application.Messages;
using Ahlena.Modules.Chat.Domain.Conversations;
using Ahlena.Modules.Chat.Domain.Messages;
using Ahlena.Modules.Chat.Domain.Reports;

namespace Ahlena.Modules.Chat.Application.Reports;

/// <summary>
/// Support paged read of a reported conversation's messages (read-only, like
/// the legacy array endpoint): cursor page desc by (`createdOnUtc`, `id`)
/// in the existing `MessageCursorPage` shape, `beforeId` accepting an opaque
/// cursor or a raw message id of THIS conversation. `pageSize` defaults to
/// 30, clamped 1–100. Unknown reports and unresolvable cursors are the plain
/// admin 404; Support observes and never stamps delivery or writes marks.
/// </summary>
public sealed record GetReportMessagesPageQuery(
    ReportId ReportId,
    string? BeforeId,
    int PageSize)
    : IQuery<MessageCursorPage>;

public sealed class GetReportMessagesPageQueryHandler
    : IQueryHandler<GetReportMessagesPageQuery, MessageCursorPage>
{
    public const int DefaultLimit = 30;
    public const int MaximumLimit = 100;

    private readonly IChatDbContext _dbContext;

    public GetReportMessagesPageQueryHandler(IChatDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<MessageCursorPage>> Handle(
        GetReportMessagesPageQuery request,
        CancellationToken cancellationToken)
    {
        Report? report = await _dbContext.Reports
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == request.ReportId,
                cancellationToken);

        if (report is null)
        {
            return Result<MessageCursorPage>.Failure(
                ReportErrors.NotFound);
        }

        Conversation? conversation = await _dbContext.Conversations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == report.ConversationId,
                cancellationToken);

        if (conversation is null)
        {
            return Result<MessageCursorPage>.Failure(
                ReportErrors.NotFound);
        }

        int limit = request.PageSize <= 0
            ? DefaultLimit
            : Math.Clamp(request.PageSize, 1, MaximumLimit);

        (DateTime SentOnUtc, Guid Id)? cursor = null;

        if (!string.IsNullOrWhiteSpace(request.BeforeId))
        {
            if (MessageCursor.TryDecode(
                    request.BeforeId,
                    out DateTime sentOnUtc,
                    out Guid decodedId))
            {
                cursor = (sentOnUtc, decodedId);
            }
            else if (Guid.TryParse(request.BeforeId, out Guid rawId))
            {
                var resolved = await _dbContext.Messages
                    .AsNoTracking()
                    .Where(item =>
                        item.ConversationId == conversation.Id &&
                        item.Id == new MessageId(rawId))
                    .Select(item => new { item.SentOnUtc })
                    .SingleOrDefaultAsync(cancellationToken);

                if (resolved is null)
                {
                    return Result<MessageCursorPage>.Failure(
                        ReportErrors.NotFound);
                }

                cursor = (resolved.SentOnUtc, rawId);
            }
            else
            {
                return Result<MessageCursorPage>.Failure(
                    ReportErrors.NotFound);
            }
        }

        IQueryable<Message> scoped = _dbContext.Messages
            .AsNoTracking()
            .Where(item => item.ConversationId == conversation.Id);

        List<Message> rows;

        if (cursor is null)
        {
            rows = await scoped
                .OrderByDescending(item => item.SentOnUtc)
                .ThenByDescending(item => item.Id)
                .Take(limit + 1)
                .ToListAsync(cancellationToken);
        }
        else
        {
            DateTime cursorSentOnUtc = cursor.Value.SentOnUtc;
            Guid cursorId = cursor.Value.Id;

            int boundary = await scoped.CountAsync(
                item => item.SentOnUtc == cursorSentOnUtc,
                cancellationToken);

            rows = (await scoped
                    .Where(item => item.SentOnUtc <= cursorSentOnUtc)
                    .OrderByDescending(item => item.SentOnUtc)
                    .ThenByDescending(item => item.Id)
                    .Take(limit + 1 + boundary)
                    .ToListAsync(cancellationToken))
                .Where(item =>
                    item.SentOnUtc < cursorSentOnUtc ||
                    (item.SentOnUtc == cursorSentOnUtc &&
                     item.Id.Value.CompareTo(cursorId) < 0))
                .Take(limit + 1)
                .ToList();
        }

        bool hasMore = rows.Count > limit;

        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        IReadOnlyDictionary<UserId, ReadMark> marks =
            await MessageReadRules.LoadMarksAsync(
                _dbContext,
                conversation.Id,
                [conversation.ParticipantOne, conversation.ParticipantTwo],
                cancellationToken);

        var items = new List<MessageItemResponse>(rows.Count);

        foreach (Message row in rows)
        {
            UserId reader = row.SenderUserId == conversation.ParticipantOne
                ? conversation.ParticipantTwo
                : conversation.ParticipantOne;

            ReadMark readerMark = marks.TryGetValue(reader, out ReadMark mark)
                ? mark
                : ReadMark.None;

            items.Add(MessageItemFactory.Create(
                row,
                MessageReadRules.ReadStamp(row, readerMark)));
        }

        Message? last = rows.Count > 0 ? rows[^1] : null;

        return Result<MessageCursorPage>.Success(
            new MessageCursorPage(
                items,
                hasMore && last is not null
                    ? MessageCursor.Encode(last.SentOnUtc, last.Id.Value)
                    : null,
                hasMore));
    }
}
