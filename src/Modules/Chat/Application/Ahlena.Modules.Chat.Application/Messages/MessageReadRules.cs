using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Application.Abstractions.Data;
using Ahlena.Modules.Chat.Application.Conversations;
using Ahlena.Modules.Chat.Domain.Conversations;
using Ahlena.Modules.Chat.Domain.Messages;

namespace Ahlena.Modules.Chat.Application.Messages;

/// <summary>
/// Facts about the messages of a set of conversations for one viewer:
/// the newest message per conversation and the not-read count per
/// conversation. Both consumers (endpoint 1's list wiring and endpoint 8's
/// totals) read the same computation, so the two can never disagree.
/// </summary>
internal sealed record MessageFacts(
    IReadOnlyDictionary<ConversationId, LastMessageResponse?> LastMessages,
    IReadOnlyDictionary<ConversationId, int> UnreadCounts);

/// <summary>
/// The read watermark of one participant in one conversation: Lane A's
/// <see cref="ConversationReadState"/> row plus the message that row points
/// at, when that message resolves inside the same conversation.
/// </summary>
internal readonly record struct ReadMark(
    ConversationReadState? State,
    (Guid Id, DateTime SentOnUtc)? Watermark)
{
    public static ReadMark None => new(null, null);
}

/// <summary>
/// The single definition of "read" for Lane B.
///
/// <para><b>The rule (implemented):</b> a message M counts as READ for a
/// reader V iff V has a <see cref="ConversationReadState"/> row for M's
/// conversation and either</para>
/// <list type="bullet">
/// <item>the mark's <c>LastReadMessageId</c> resolves to a message T in the
/// same conversation: M is read when (M.SentOnUtc, M.Id) &lt;= (T.SentOnUtc,
/// T.Id) — the watermark covers everything up to and including T, ties broken
/// by id because message ids are Guid v7 (time-ordered) and the ids are
/// created in send order;</item>
/// <item>or the mark's id does not resolve to a message in that conversation
/// (Lane A accepted any non-empty id and never validated it): M is read when
/// <c>state.ReadOnUtc &gt;= M.SentOnUtc</c>, i.e. the mark's own timestamp
/// is the fallback watermark;</item>
/// <item>or M carries its own set-once <c>ReadOnUtc</c> stamp, which is an
/// explicit read marking and wins outright.</item>
/// </list>
/// <para>No read state at all means nothing in that conversation is read for
/// that reader. Unread = not read.</para>
///
/// <para><b>Who the reader is:</b> for unread counting (endpoints 1 and 8)
/// the reader is the requester, and only messages the requester did not send
/// are ever unread for them. For the <c>readOnUtc</c> tick on an item
/// (endpoints 3 and 4) the reader is the party who is supposed to have read
/// it — the other participant when the viewer sent the message, the viewer
/// themselves when the viewer received it. That is the brief's example rule
/// applied verbatim to received messages, and it is the only reading that
/// makes the frozen "read blue" tick mean anything for a sender: a viewer's
/// own watermark can never be the reason the viewer's own outgoing message
/// was read.</para>
///
/// <para>The same predicate therefore backs unread counts, the
/// <c>unreadCount</c> field of endpoint 1, and the <c>readOnUtc</c> field of
/// an item, so no two surfaces can disagree.</para>
/// </summary>
internal static class MessageReadRules
{
    public static bool IsRead(
        DateTime messageSentOnUtc,
        Guid messageId,
        DateTime? explicitReadOnUtc,
        ConversationReadState? readState,
        (Guid Id, DateTime SentOnUtc)? watermark)
    {
        if (explicitReadOnUtc is not null)
        {
            return true;
        }

        if (readState is null)
        {
            return false;
        }

        if (watermark is null)
        {
            // Lane A recorded an id this schema cannot resolve — fall back
            // to the mark's own timestamp.
            return readState.ReadOnUtc >= messageSentOnUtc;
        }

        int byTime = messageSentOnUtc.CompareTo(watermark.Value.SentOnUtc);

        return byTime < 0 ||
               (byTime == 0 &&
                messageId.CompareTo(watermark.Value.Id) <= 0);
    }

    /// <summary>
    /// The <c>readOnUtc</c> an item shows: the message's own set-once stamp
    /// when it has one, otherwise the reader's mark timestamp while the rule
    /// considers the message read, otherwise null (never read).
    /// </summary>
    public static DateTime? ReadStamp(Message message, ReadMark reader)
    {
        if (message.ReadOnUtc is { } explicitStamp)
        {
            return explicitStamp;
        }

        return IsRead(
                   message.SentOnUtc,
                   message.Id.Value,
                   explicitReadOnUtc: null,
                   reader.State,
                   reader.Watermark)
            ? reader.State!.ReadOnUtc
            : null;
    }

    /// <summary>
    /// Loads the read marks of the given participants for one conversation
    /// in two queries (marks, then the messages the marks point at).
    /// </summary>
    public static async Task<
        IReadOnlyDictionary<UserId, ReadMark>> LoadMarksAsync(
        IChatDbContext dbContext,
        ConversationId conversationId,
        IReadOnlyCollection<UserId> participants,
        CancellationToken cancellationToken)
    {
        var marks = new Dictionary<UserId, ReadMark>();

        UserId[] participantsInOrder = participants.Distinct().ToArray();

        if (participantsInOrder.Length == 0)
        {
            return marks;
        }

        List<ConversationReadState> states = await dbContext.ReadStates
            .AsNoTracking()
            .Where(state =>
                state.ConversationId == conversationId &&
                participantsInOrder.Contains(state.UserId))
            .ToListAsync(cancellationToken);

        IReadOnlyDictionary<Guid, WatermarkRow> watermarks =
            await LoadWatermarksAsync(
                dbContext,
                states.Select(state => state.LastReadMessageId),
                cancellationToken);

        foreach (UserId participant in participantsInOrder)
        {
            // One row per (conversation, user) in practice; if more ever
            // exist, the last mark wins rather than throwing.
            ConversationReadState? state = null;

            foreach (ConversationReadState candidate in states)
            {
                if (candidate.UserId == participant)
                {
                    state = candidate;
                }
            }

            if (state is null)
            {
                marks[participant] = ReadMark.None;
                continue;
            }

            // A mark only resolves against a message in its own
            // conversation; anything else falls back to the timestamp rule.
            (Guid Id, DateTime SentOnUtc)? watermark =
                watermarks.TryGetValue(
                        state.LastReadMessageId,
                        out WatermarkRow? resolved) &&
                    resolved.ConversationId == conversationId
                    ? (resolved.Id, resolved.SentOnUtc)
                    : null;

            marks[participant] = new ReadMark(state, watermark);
        }

        return marks;
    }

    /// <summary>
    /// Loads last message + unread count for the given conversations in a
    /// fixed number of queries (messages, the requester's read states, the
    /// watermark messages). Every conversation gets an entry, so consumers
    /// can index without existence checks.
    /// </summary>
    public static async Task<MessageFacts> LoadAsync(
        IChatDbContext dbContext,
        UserId requester,
        IReadOnlyCollection<Conversation> conversations,
        CancellationToken cancellationToken)
    {
        var lastMessages = new Dictionary<ConversationId, LastMessageResponse?>();
        var unreadCounts = new Dictionary<ConversationId, int>();

        ConversationId[] conversationIds =
            conversations.Select(conversation => conversation.Id).ToArray();

        if (conversationIds.Length == 0)
        {
            return new MessageFacts(lastMessages, unreadCounts);
        }

        // NOTE: strongly-typed id members (MessageId.Value, ...) do NOT
        // translate to SQL — Npgsql only rewrites whole property accesses.
        // Project the properties themselves and read .Value in memory.
        var loaded = await dbContext.Messages
            .AsNoTracking()
            .Where(message => conversationIds.Contains(message.ConversationId))
            .Select(message => new
            {
                message.ConversationId,
                message.Id,
                message.SenderUserId,
                message.Type,
                message.Text,
                message.SentOnUtc,
                message.ReadOnUtc
            })
            .ToListAsync(cancellationToken);

        List<MessageRow> rows = loaded
            .Select(row => new MessageRow(
                row.ConversationId,
                row.Id.Value,
                row.SenderUserId.Value,
                row.Type,
                row.Text,
                row.SentOnUtc,
                row.ReadOnUtc))
            .ToList();

        List<ConversationReadState> states = await dbContext.ReadStates
            .AsNoTracking()
            .Where(state =>
                state.UserId == requester &&
                conversationIds.Contains(state.ConversationId))
            .ToListAsync(cancellationToken);

        var stateByConversation = new Dictionary<ConversationId, ConversationReadState>();

        foreach (ConversationReadState state in states)
        {
            stateByConversation[state.ConversationId] = state;
        }

        var watermarks = await LoadWatermarksAsync(
            dbContext,
            states.Select(state => state.LastReadMessageId),
            cancellationToken);

        foreach (Conversation conversation in conversations)
        {
            List<MessageRow> forConversation = rows
                .Where(row => row.ConversationId == conversation.Id)
                .ToList();

            MessageRow? newest = forConversation
                .OrderByDescending(row => row.SentOnUtc)
                .ThenByDescending(row => row.Id)
                .FirstOrDefault();

            lastMessages[conversation.Id] = newest is null
                ? null
                : new LastMessageResponse(
                    newest.Id,
                    newest.Type.ToString(),
                    // A File message has no body; the frozen lastMessage
                    // shape carries a non-nullable string, so it travels as
                    // empty rather than an invented placeholder.
                    newest.Text ?? string.Empty,
                    newest.SentOnUtc);

            stateByConversation.TryGetValue(
                conversation.Id,
                out ConversationReadState? state);

            (Guid Id, DateTime SentOnUtc)? watermark =
                ResolveWatermark(watermarks, state, conversation.Id);

            int unread = forConversation.Count(row =>
                row.SenderUserId != requester.Value &&
                !IsRead(
                    row.SentOnUtc,
                    row.Id,
                    row.ReadOnUtc,
                    state,
                    watermark));

            unreadCounts[conversation.Id] = unread;
        }

        return new MessageFacts(lastMessages, unreadCounts);
    }

    /// <summary>
    /// The mark's target message, but only when it resolves to a message of
    /// the mark's own conversation; anything else (unknown id, or an id that
    /// belongs to another conversation) leaves the watermark null so the
    /// timestamp fallback applies.
    /// </summary>
    private static (Guid Id, DateTime SentOnUtc)? ResolveWatermark(
        IReadOnlyDictionary<Guid, WatermarkRow> watermarks,
        ConversationReadState? state,
        ConversationId conversationId)
    {
        if (state is null ||
            !watermarks.TryGetValue(
                state.LastReadMessageId,
                out WatermarkRow? resolved) ||
            resolved.ConversationId != conversationId)
        {
            return null;
        }

        return (resolved.Id, resolved.SentOnUtc);
    }

    private static async Task<
        IReadOnlyDictionary<Guid, WatermarkRow>>
        LoadWatermarksAsync(
            IChatDbContext dbContext,
            IEnumerable<Guid> watermarkIds,
            CancellationToken cancellationToken)
    {
        MessageId[] ids = watermarkIds
            .Distinct()
            .Select(id => new MessageId(id))
            .ToArray();

        if (ids.Length == 0)
        {
            return new Dictionary<Guid, WatermarkRow>();
        }

        // Message ids are globally unique, so the conversation of the
        // resolved row is what proves the mark names a message in its own
        // conversation (validated by the caller).
        var loaded = await dbContext.Messages
            .AsNoTracking()
            .Where(message => ids.Contains(message.Id))
            .Select(message => new
            {
                message.ConversationId,
                message.Id,
                message.SentOnUtc
            })
            .ToListAsync(cancellationToken);

        var watermarks = new Dictionary<Guid, WatermarkRow>();

        foreach (var row in loaded)
        {
            watermarks[row.Id.Value] = new WatermarkRow(
                row.ConversationId,
                row.Id.Value,
                row.SentOnUtc);
        }

        return watermarks;
    }

    private sealed record MessageRow(
        ConversationId ConversationId,
        Guid Id,
        Guid SenderUserId,
        MessageType Type,
        string? Text,
        DateTime SentOnUtc,
        DateTime? ReadOnUtc);

    /// <summary>A read mark's target message, if it resolves inside the
    /// mark's own conversation.</summary>
    private sealed record WatermarkRow(
        ConversationId ConversationId,
        Guid Id,
        DateTime SentOnUtc);
}
