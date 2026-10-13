using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Application.Conversations;
using Sanad.Modules.Chat.Application.Events;
using Sanad.Modules.Chat.Application.Persistence;
using Sanad.Modules.Chat.Domain.Changes;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Domain.Messages;

namespace Sanad.Modules.Chat.Application.Messages;

/// <summary>
/// Endpoint 3 — a cursor page of one conversation's messages, newest first.
/// <c>BeforeId</c> is either the <c>nextCursor</c> this endpoint returned or
/// a raw message id (Opus delta: the wire parameter is <c>beforeId</c>);
/// <c>PageSize</c> is clamped to 1–100 by the handler (30 is the controller
/// default).
/// </summary>
public sealed record GetMessagesQuery(
    UserId Requester,
    ConversationId ConversationId,
    string? BeforeId,
    int PageSize)
    : IQuery<MessageCursorPage>;

public sealed class GetMessagesQueryHandler
    : IQueryHandler<GetMessagesQuery, MessageCursorPage>
{
    public const int DefaultLimit = 30;
    public const int MaximumLimit = 100;

    private readonly IChatDbContext _dbContext;
    private readonly IChatEventRecorder _recorder;

    public GetMessagesQueryHandler(
        IChatDbContext dbContext,
        IChatEventRecorder? recorder = null)
    {
        _dbContext = dbContext;
        _recorder = recorder ?? NullChatEventRecorder.Instance;
    }

    public async Task<Result<MessageCursorPage>> Handle(
        GetMessagesQuery request,
        CancellationToken cancellationToken)
    {
        // Tracked: the delivery side effect below mutates rows and the
        // conversation revision in the same commit.
        Conversation? conversation = await _dbContext.Conversations
            .SingleOrDefaultAsync(
                item => item.Id == request.ConversationId,
                cancellationToken);

        // Same participant/visibility gate as the send path: unknown,
        // foreign, and hidden-for-requester conversations are one generic
        // privacy 404.
        if (conversation is null ||
            !conversation.IsParticipant(request.Requester) ||
            conversation.IsHiddenFor(request.Requester))
        {
            return Result<MessageCursorPage>.Failure(
                ConversationErrors.NotFound);
        }

        int limit = Math.Clamp(request.PageSize, 1, MaximumLimit);

        CursorResult cursor = await ResolveCursorAsync(
            conversation.Id,
            request.BeforeId,
            cancellationToken);

        if (cursor.Error is not null)
        {
            return Result<MessageCursorPage>.Failure(cursor.Error);
        }

        List<Message> rows = await LoadPageAsync(
            conversation.Id,
            cursor.Cursor,
            limit,
            cancellationToken);

        bool hasMore = rows.Count > limit;

        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        // Write side effect: the recipient's first fetch of a message stamps
        // delivery. Set-once, so later fetches no-op; the sender never
        // stamps their own messages. First stamps also write durable
        // receipt changes in the same commit, so sync and realtime observe
        // exactly the transitions this fetch caused — and only those, so
        // polling can never generate loops.
        DateTime deliveredOnUtc = DateTime.UtcNow;
        var stamped = new List<Message>(rows.Count);

        foreach (Message row in rows)
        {
            if (row.SenderUserId == request.Requester ||
                row.DeliveredOnUtc is not null)
            {
                continue;
            }

            row.MarkDelivered(deliveredOnUtc);
            _recorder.RecordReceiptUpdated(conversation, row, deliveredOnUtc);
            stamped.Add(row);
        }

        await SaveWithRevisionRetryAsync(conversation, stamped, deliveredOnUtc, cancellationToken);

        UserId otherParticipant =
            conversation.ParticipantOne == request.Requester
                ? conversation.ParticipantTwo
                : conversation.ParticipantOne;

        IReadOnlyDictionary<UserId, ReadMark> marks =
            await MessageReadRules.LoadMarksAsync(
                _dbContext,
                conversation.Id,
                [request.Requester, otherParticipant],
                cancellationToken);

        var items = new List<MessageItemResponse>(rows.Count);

        foreach (Message row in rows)
        {
            // The reader of an item is whoever did not send it: the viewer
            // for incoming messages, the other participant for the viewer's
            // own messages (see MessageReadRules).
            UserId reader = row.SenderUserId == request.Requester
                ? otherParticipant
                : request.Requester;

            ReadMark readerMark = marks.TryGetValue(
                    reader,
                    out ReadMark mark)
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

    /// <summary>
    /// Reads <c>beforeId</c> in either accepted form. The opaque
    /// cursor is self-contained; a raw message id must resolve to a message
    /// of THIS conversation. Anything that does not resolve (malformed,
    /// unknown, or another conversation's id) answers the message-scoped
    /// privacy 404, so this parameter can never be used to probe ids.
    /// </summary>
    private async Task<CursorResult> ResolveCursorAsync(
        ConversationId conversationId,
        string? beforeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(beforeId))
        {
            return CursorResult.None;
        }

        if (MessageCursor.TryDecode(
                beforeId,
                out DateTime sentOnUtc,
                out Guid decodedId))
        {
            return new CursorResult((sentOnUtc, decodedId), null);
        }

        if (Guid.TryParse(beforeId, out Guid rawId))
        {
            MessageId messageId = new(rawId);

            var resolved = await _dbContext.Messages
                .AsNoTracking()
                .Where(item =>
                    item.ConversationId == conversationId &&
                    item.Id == messageId)
                .Select(item => new { item.SentOnUtc })
                .SingleOrDefaultAsync(cancellationToken);

            return resolved is null
                ? CursorResult.Rejected
                : new CursorResult((resolved.SentOnUtc, rawId), null);
        }

        return CursorResult.Rejected;
    }

    /// <summary>The outcome of resolving <c>beforeId</c>: either a
    /// cursor to page from, or the privacy 404 to answer with.</summary>
    private sealed record CursorResult(
        (DateTime SentOnUtc, Guid Id)? Cursor,
        Error? Error)
    {
        public static CursorResult None => new(null, null);

        public static CursorResult Rejected =>
            new(null, MessageErrors.NotFound);
    }

    /// <summary>
    /// Newest-first keyset page over (<c>sentOnUtc</c>, <c>id</c>), one row
    /// past the limit so <c>hasMore</c> is exact.
    ///
    /// <para>Guid comparison does not translate to SQL (Npgsql rewrites
    /// property accesses, not <c>Guid.CompareTo</c>), so the id tie-break is
    /// applied in memory: the boundary stamp is fetched whole, then filtered.
    /// Timestamps come from the server, so a tie is rare — but the page stays
    /// total-ordered and stable even when it happens.</para>
    /// </summary>
    private async Task<List<Message>> LoadPageAsync(
        ConversationId conversationId,
        (DateTime SentOnUtc, Guid Id)? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        IQueryable<Message> scoped = _dbContext.Messages
            .Where(item => item.ConversationId == conversationId);

        if (cursor is null)
        {
            List<Message> firstPage = await scoped
                .OrderByDescending(item => item.SentOnUtc)
                .ThenByDescending(item => item.Id)
                .Take(limit + 1)
                .ToListAsync(cancellationToken);

            return firstPage;
        }

        DateTime cursorSentOnUtc = cursor.Value.SentOnUtc;
        Guid cursorId = cursor.Value.Id;

        int boundary = await scoped.CountAsync(
            item => item.SentOnUtc == cursorSentOnUtc,
            cancellationToken);

        List<Message> window = await scoped
            .Where(item => item.SentOnUtc <= cursorSentOnUtc)
            .OrderByDescending(item => item.SentOnUtc)
            .ThenByDescending(item => item.Id)
            .Take(limit + 1 + boundary)
            .ToListAsync(cancellationToken);

        return window
            .Where(item =>
                item.SentOnUtc < cursorSentOnUtc ||
                (item.SentOnUtc == cursorSentOnUtc &&
                 item.Id.Value.CompareTo(cursorId) < 0))
            .Take(limit + 1)
            .ToList();
    }

    /// <summary>
    /// Commits delivery stamps with their receipt changes. A concurrent
    /// writer wins the revision token instead of corrupting it: staged
    /// rows are detached and the set-once stamps that survive the reload
    /// are re-recorded (duplicate-safe), so no first stamp is ever lost.
    /// </summary>
    private async Task SaveWithRevisionRetryAsync(
        Conversation conversation,
        List<Message> stamped,
        DateTime now,
        CancellationToken cancellationToken)
    {
        DbContext? context = _dbContext as DbContext;

        if (context is null)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException) when (
                attempt + 1 < ChatPersistenceConflicts.MaxRevisionRetryAttempts)
            {
                ChatPersistenceConflicts.DetachAdded<ChatChange>(context);
                await context.Entry(conversation).ReloadAsync(cancellationToken);

                foreach (Message message in stamped)
                {
                    _recorder.RecordReceiptUpdated(conversation, message, now);
                }
            }
        }
    }
}
