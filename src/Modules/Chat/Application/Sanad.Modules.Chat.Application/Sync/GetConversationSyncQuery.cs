using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Application.Conversations;
using Sanad.Modules.Chat.Application.Events;
using Sanad.Modules.Chat.Application.Messages;
using Sanad.Modules.Chat.Application.Persistence;
using Sanad.Modules.Chat.Application.RateLimiting;
using Sanad.Modules.Chat.Domain.Changes;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Domain.Messages;

namespace Sanad.Modules.Chat.Application.Sync;

/// <summary>
/// `GET /api/v1/chat/conversations/{id}/sync` — commit-safe REST recovery
/// for sockets, degraded polling, app foreground, and restart.
/// Initial/empty cursor starts a paginated authoritative snapshot (stable
/// total order) with a consistent high-water revision; subsequent reads
/// consume committed changes above the cursor. Revisions are gapless per
/// conversation (assigned under the conversation row lock in the writer's
/// transaction), so advancing the cursor over returned rows cannot skip a
/// later-committing transaction: its revision sorts above the cursor and the
/// next poll returns it. Snapshot pages run in short transactions that hold
/// the conversation row while capturing the boundary, so a concurrent writer
/// either commits before the snapshot (visible, revision at/below the
/// boundary) or blocks until commit (revision above, caught by the change
/// poll).
/// </summary>
public sealed record GetConversationSyncQuery(
    UserId Requester,
    ConversationId ConversationId,
    string? Cursor,
    int PageSize)
    : IQuery<ConversationSyncPage>;

public sealed class GetConversationSyncQueryHandler
    : IQueryHandler<GetConversationSyncQuery, ConversationSyncPage>
{
    public const int DefaultLimit = 100;
    public const int MaximumLimit = 100;

    private readonly IChatDbContext _dbContext;
    private readonly IChatSyncCursorProtector _protector;
    private readonly IChatRateGate? _rateGate;
    private readonly IChatEventRecorder _recorder;

    public GetConversationSyncQueryHandler(
        IChatDbContext dbContext,
        IChatSyncCursorProtector protector,
        IChatRateGate? rateGate = null,
        IChatEventRecorder? recorder = null)
    {
        _dbContext = dbContext;
        _protector = protector;
        _rateGate = rateGate;
        _recorder = recorder ?? NullChatEventRecorder.Instance;
    }

    public async Task<Result<ConversationSyncPage>> Handle(
        GetConversationSyncQuery request,
        CancellationToken cancellationToken)
    {
        if (_rateGate is not null &&
            !_rateGate.TryAcquireSync(
                request.Requester,
                request.ConversationId,
                out TimeSpan retryAfter))
        {
            return Result<ConversationSyncPage>.Failure(
                ChatRateErrors.Sync(retryAfter));
        }

        Conversation? conversation = await _dbContext.Conversations
            .SingleOrDefaultAsync(
                item => item.Id == request.ConversationId,
                cancellationToken);

        if (conversation is null ||
            !conversation.IsParticipant(request.Requester) ||
            conversation.IsHiddenFor(request.Requester))
        {
            return Result<ConversationSyncPage>.Failure(
                ChatSyncErrors.NotFound);
        }

        int limit = request.PageSize <= 0 ? DefaultLimit : Math.Clamp(request.PageSize, 1, MaximumLimit);

        if (string.IsNullOrWhiteSpace(request.Cursor))
        {
            return await SnapshotPageAsync(
                conversation,
                request.Requester,
                snapshotStart: null,
                lastSentOnUtc: null,
                lastMessageId: null,
                limit,
                cancellationToken);
        }

        if (!_protector.TryUnprotect(request.Cursor, out ChatSyncCursor? cursor) ||
            cursor is null ||
            cursor.ConversationId != request.ConversationId.Value ||
            cursor.RequesterId != request.Requester.Value)
        {
            return Result<ConversationSyncPage>.Failure(
                ChatSyncErrors.NotFound);
        }

        if (cursor.Version != ChatSyncCursor.CurrentVersion)
        {
            return Result<ConversationSyncPage>.Success(
                new ConversationSyncPage([], null, false, true));
        }

        if (string.Equals(cursor.Mode, ChatSyncCursor.SnapshotMode, StringComparison.Ordinal))
        {
            return await SnapshotPageAsync(
                conversation,
                request.Requester,
                cursor.Boundary == 0 ? null : cursor.Boundary,
                cursor.LastSentOnUtc,
                cursor.LastMessageId,
                limit,
                cancellationToken);
        }

        if (!string.Equals(cursor.Mode, ChatSyncCursor.ChangesMode, StringComparison.Ordinal))
        {
            return Result<ConversationSyncPage>.Failure(
                ChatSyncErrors.NotFound);
        }

        return await ChangesPageAsync(
            conversation,
            request.Requester,
            cursor.Revision,
            limit,
            cancellationToken);
    }

    private async Task<Result<ConversationSyncPage>> SnapshotPageAsync(
        Conversation conversation,
        UserId requester,
        long? snapshotStart,
        DateTime? lastSentOnUtc,
        Guid? lastMessageId,
        int limit,
        CancellationToken cancellationToken)
    {
        DbContext? relational = RelationalContext();
        await using var transaction = relational is null
            ? null
            : await relational.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Serialize writers around the boundary capture (relational
            // only): a concurrent writer either commits before this
            // transaction (visible below, revision at/below the boundary)
            // or blocks until commit (revision above, caught later).
            if (relational is not null)
            {
                await _dbContext.Conversations
                    .Where(item => item.Id == conversation.Id)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            item => item.ChangeRevision,
                            item => item.ChangeRevision),
                        cancellationToken);

                await relational.Entry(conversation).ReloadAsync(cancellationToken);
            }

            long boundary = conversation.ChangeRevision;
            long start = snapshotStart ?? boundary;

            List<Message> rows = await LoadSnapshotMessagesAsync(
                conversation.Id,
                lastSentOnUtc,
                lastMessageId,
                limit,
                cancellationToken);

            bool hasMore = rows.Count > limit;

            if (hasMore)
            {
                rows.RemoveAt(rows.Count - 1);
            }

            UserId other = Counterpart(conversation, requester);

            IReadOnlyDictionary<UserId, ReadMark> marks =
                await MessageReadRules.LoadMarksAsync(
                    _dbContext,
                    conversation.Id,
                    [requester, other],
                    cancellationToken);

            DateTime now = DateTime.UtcNow;
            var items = new List<SyncChangeItem>(rows.Count);
            var stamped = new List<Message>(rows.Count);

            foreach (Message row in rows)
            {
                StampDelivery(row, requester, conversation, now, stamped);
                items.Add(CreatedItem(row, conversation, requester, marks));
            }

            string? nextCursor;
            bool deltaReplay = !hasMore;

            Message? last = hasMore ? rows[^1] : null;

            if (deltaReplay)
            {
                // Final snapshot page: also replay concurrent changes above
                // the snapshot start so a writer that committed
                // mid-pagination is not lost between the pages and the
                // change cursor.
                List<SyncChangeItem> deltas = await LoadChangeItemsAsync(
                    conversation,
                    requester,
                    marks,
                    now,
                    afterRevision: start,
                    atMostRevision: boundary,
                    cancellationToken,
                    stampDelivery: false);

                foreach (SyncChangeItem delta in deltas)
                {
                    bool duplicate = items.Any(item =>
                        item.Type == delta.Type &&
                        item.Revision == delta.Revision);

                    if (!duplicate)
                    {
                        items.Add(delta);
                    }
                }
            }

            // The cursor advances only over content returned in this page
            // (messages above, deltas above): own delivery stamps are
            // visible in the returned snapshots, and any concurrently
            // assigned higher revision sorts above the cursor, so nothing
            // committed can be skipped.
            await SaveWithRevisionRetryAsync(conversation, stamped, now, cancellationToken);

            if (hasMore && last is not null)
            {
                nextCursor = _protector.Protect(new ChatSyncCursor(
                    ChatSyncCursor.CurrentVersion,
                    ChatSyncCursor.SnapshotMode,
                    conversation.Id.Value,
                    requester.Value,
                    0,
                    start,
                    last.SentOnUtc,
                    last.Id.Value));
            }
            else
            {
                long highWater = Math.Max(boundary, conversation.ChangeRevision);
                nextCursor = _protector.Protect(new ChatSyncCursor(
                    ChatSyncCursor.CurrentVersion,
                    ChatSyncCursor.ChangesMode,
                    conversation.Id.Value,
                    requester.Value,
                    highWater,
                    highWater,
                    null,
                    null));
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return Result<ConversationSyncPage>.Success(
                new ConversationSyncPage(items, nextCursor, hasMore, false));
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
    }

    private async Task<Result<ConversationSyncPage>> ChangesPageAsync(
        Conversation conversation,
        UserId requester,
        long afterRevision,
        int limit,
        CancellationToken cancellationToken)
    {
        List<ChatChange> rows = await _dbContext.Changes
            .AsNoTracking()
            .Where(item =>
                item.ConversationId == conversation.Id &&
                item.Revision > afterRevision)
            .OrderBy(item => item.Revision)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        bool hasMore = rows.Count > limit;

        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        UserId other = Counterpart(conversation, requester);

        IReadOnlyDictionary<UserId, ReadMark> marks =
            await MessageReadRules.LoadMarksAsync(
                _dbContext,
                conversation.Id,
                [requester, other],
                cancellationToken);

        DateTime now = DateTime.UtcNow;
        var items = new List<SyncChangeItem>(rows.Count);
        var stamped = new List<Message>(rows.Count);

        foreach (ChatChange row in rows)
        {
            SyncChangeItem? item = await ChangeItemAsync(
                conversation,
                requester,
                marks,
                row,
                now,
                stamped,
                cancellationToken);

            if (item is not null)
            {
                items.Add(item);
            }
        }

        // Own delivery stamps ride along in the returned snapshots; the
        // cursor advances only over rows READ in this poll — never over
        // concurrently assigned revisions — so a late-committing
        // transaction below a stamp can never be skipped.
        await SaveWithRevisionRetryAsync(conversation, stamped, now, cancellationToken);

        long nextRevision = rows.Count == 0
            ? afterRevision
            : rows.Max(item => item.Revision);

        string nextCursor = _protector.Protect(new ChatSyncCursor(
            ChatSyncCursor.CurrentVersion,
            ChatSyncCursor.ChangesMode,
            conversation.Id.Value,
            requester.Value,
            nextRevision,
            nextRevision,
            null,
            null));

        return Result<ConversationSyncPage>.Success(
            new ConversationSyncPage(items, nextCursor, hasMore, false));
    }

    private async Task<List<Message>> LoadSnapshotMessagesAsync(
        ConversationId conversationId,
        DateTime? lastSentOnUtc,
        Guid? lastMessageId,
        int limit,
        CancellationToken cancellationToken)
    {
        // Ascending stable total order. The id tie-break is applied in
        // memory (whole-struct ordering does not evaluate on every
        // provider); the SQL window over-reads the position timestamp's
        // ties the same way the history endpoint does, so no row is lost
        // between pages. Guids are v7 (time-ordered, created in send
        // order), so the in-memory order matches insertion order.
        IQueryable<Message> scoped = _dbContext.Messages
            .Where(item => item.ConversationId == conversationId);

        List<Message> window;

        if (lastSentOnUtc is null || lastMessageId is null)
        {
            window = await scoped
                .OrderBy(item => item.SentOnUtc)
                .Take(limit + 1)
                .ToListAsync(cancellationToken);
        }
        else
        {
            DateTime sent = lastSentOnUtc.Value;

            int ties = await scoped.CountAsync(
                item => item.SentOnUtc == sent,
                cancellationToken);

            window = await scoped
                .Where(item => item.SentOnUtc >= sent)
                .OrderBy(item => item.SentOnUtc)
                .Take(limit + 1 + ties)
                .ToListAsync(cancellationToken);

            Guid id = lastMessageId.Value;

            window = window
                .Where(item =>
                    item.SentOnUtc > sent ||
                    (item.SentOnUtc == sent &&
                     item.Id.Value.CompareTo(id) > 0))
                .ToList();
        }

        return window
            .OrderBy(item => item.SentOnUtc)
            .ThenBy(item => item.Id.Value)
            .Take(limit + 1)
            .ToList();
    }

    private async Task<List<SyncChangeItem>> LoadChangeItemsAsync(
        Conversation conversation,
        UserId requester,
        IReadOnlyDictionary<UserId, ReadMark> marks,
        DateTime now,
        long afterRevision,
        long atMostRevision,
        CancellationToken cancellationToken,
        bool stampDelivery)
    {
        List<ChatChange> rows = await _dbContext.Changes
            .AsNoTracking()
            .Where(item =>
                item.ConversationId == conversation.Id &&
                item.Revision > afterRevision &&
                item.Revision <= atMostRevision)
            .OrderBy(item => item.Revision)
            .ToListAsync(cancellationToken);

        var items = new List<SyncChangeItem>(rows.Count);

        foreach (ChatChange row in rows)
        {
            SyncChangeItem? item = await ChangeItemAsync(
                conversation,
                requester,
                marks,
                row,
                now,
                stamped: null,
                cancellationToken,
                stampDelivery);

            if (item is not null)
            {
                items.Add(item);
            }
        }

        return items;
    }

    private async Task<SyncChangeItem?> ChangeItemAsync(
        Conversation conversation,
        UserId requester,
        IReadOnlyDictionary<UserId, ReadMark> marks,
        ChatChange row,
        DateTime now,
        List<Message>? stamped = null,
        CancellationToken cancellationToken = default,
        bool stampDelivery = true)
    {
        Message? message = await _dbContext.Messages
            .SingleOrDefaultAsync(
                item => item.Id == row.MessageId,
                cancellationToken);

        if (message is null || message.ConversationId != conversation.Id)
        {
            return null;
        }

        if (row.Type == ChatChangeType.MessageCreated)
        {
            if (stampDelivery)
            {
                StampDelivery(message, requester, conversation, now, stamped);
            }

            return new SyncChangeItem(
                row.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                nameof(ChatChangeType.MessageCreated),
                CreateMessagePayload(message, conversation, requester, marks));
        }

        return new SyncChangeItem(
            row.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            nameof(ChatChangeType.MessageReceiptUpdated),
            ReceiptPayload(message, conversation, requester, marks));
    }

    private static SyncChangeItem CreatedItem(
        Message message,
        Conversation conversation,
        UserId requester,
        IReadOnlyDictionary<UserId, ReadMark> marks) =>
        new("0",
            nameof(ChatChangeType.MessageCreated),
            CreateMessagePayload(message, conversation, requester, marks));

    private static MessageItemResponse CreateMessagePayload(
        Message message,
        Conversation conversation,
        UserId requester,
        IReadOnlyDictionary<UserId, ReadMark> marks)
    {
        UserId reader = ReaderOf(message, conversation, requester);
        ReadMark readerMark = marks.TryGetValue(reader, out ReadMark mark)
            ? mark
            : ReadMark.None;

        return MessageItemFactory.Create(
            message,
            MessageReadRules.ReadStamp(message, readerMark));
    }

    private static SyncReceiptPayload ReceiptPayload(
        Message message,
        Conversation conversation,
        UserId requester,
        IReadOnlyDictionary<UserId, ReadMark> marks)
    {
        UserId reader = ReaderOf(message, conversation, requester);
        ReadMark readerMark = marks.TryGetValue(reader, out ReadMark mark)
            ? mark
            : ReadMark.None;

        return new SyncReceiptPayload(
            conversation.Id.Value,
            message.Id.Value,
            message.DeliveredOnUtc,
            MessageReadRules.ReadStamp(message, readerMark));
    }

    private static UserId ReaderOf(
        Message message,
        Conversation conversation,
        UserId requester) =>
        message.SenderUserId == requester
            ? Counterpart(conversation, requester)
            : requester;

    private static UserId Counterpart(Conversation conversation, UserId requester) =>
        conversation.ParticipantOne == requester
            ? conversation.ParticipantTwo
            : conversation.ParticipantOne;

    /// <summary>
    /// Delivery acknowledgement for messages actually returned to the
    /// recipient in this page. Set-once: later polls no-op and unchanged
    /// receipts produce no new event, preventing polling-generated loops.
    /// Stamped messages are collected so a revision-token retry can
    /// re-stage their change rows after the reload.
    /// </summary>
    private void StampDelivery(
        Message message,
        UserId requester,
        Conversation conversation,
        DateTime now,
        List<Message>? stamped = null)
    {
        if (message.SenderUserId == requester ||
            message.DeliveredOnUtc is not null)
        {
            return;
        }

        message.MarkDelivered(now);
        _recorder.RecordReceiptUpdated(conversation, message, now);
        stamped?.Add(message);
    }

    private async Task SaveWithRevisionRetryAsync(
        Conversation conversation,
        List<Message> stamped,
        DateTime now,
        CancellationToken cancellationToken)
    {
        DbContext? context = _dbContext as DbContext;

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException) when (
                context is not null &&
                attempt + 1 < ChatPersistenceConflicts.MaxRevisionRetryAttempts)
            {
                ChatPersistenceConflicts.DetachAdded<ChatChange>(context);
                await context.Entry(conversation).ReloadAsync(cancellationToken);

                // Re-stage receipt changes detached above; the recorder's
                // Local guard keeps this duplicate-safe.
                foreach (Message message in stamped)
                {
                    _recorder.RecordReceiptUpdated(conversation, message, now);
                }
            }
        }
    }

    private DbContext? RelationalContext() =>
        _dbContext is DbContext context &&
        !string.Equals(
            context.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.InMemory",
            StringComparison.Ordinal)
            ? context
            : null;
}
