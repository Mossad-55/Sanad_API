using MediatR;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Application.Conversations;
using Sanad.Modules.Chat.Application.Messages;
using Sanad.Modules.Chat.Application.Realtime;
using Sanad.Modules.Chat.Domain.Changes;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Domain.Messages;
using Sanad.Modules.Chat.Domain.Outbox;
using Sanad.Modules.Notifications.Application.Notifications;

namespace Sanad.Modules.Chat.Application.Dispatch;

/// <summary>Dispatcher tuning (section `ChatPlus:Dispatch`).</summary>
public sealed record ChatDispatchOptions
{
    public int BatchSize { get; init; } = 100;

    public int MaximumAttempts { get; init; } = ChatOutboxItem.MaximumAttempts;

    public TimeSpan StaleClaimAfter { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan BaseRetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromMinutes(60);
}

/// <summary>
/// After-commit dispatcher: publishes durable chat changes to realtime and
/// delivers notification intent to the Notifications seam, with bounded
/// retries, exponential backoff with jitter, idempotency, and crash
/// recovery. Cross-module persistence never relies on a later synchronous
/// call: writers stage rows in the Chat transaction, and this dispatcher —
/// the only reader that mutates publish state — recovers everything pending
/// after a crash or restart, including replays (the Notifications producer
/// key dedupes redelivery; hub clients dedupe by message id/revision).
/// Two independent streams per cycle: (1) unpublished change rows ordered
/// by (conversation, revision) go to the realtime publisher; (2) due
/// outbox rows are claimed and sent as
/// <see cref="CreateChatMessageNotificationCommand"/> (ids only).
/// A failed stream never blocks the other; exhausted attempts (8) leave the
/// row unpublished with its error code for operator retry (resetting
/// `NextAttemptOnUtc`).
/// </summary>
public sealed class ChatOutboxDispatcher
{
    private readonly IChatDbContext _dbContext;
    private readonly ISender _sender;
    private readonly IChatRealtimePublisher? _publisher;
    private readonly IChatUserLookup? _userLookup;
    private readonly ChatDispatchOptions _options;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<int> _jitterMilliseconds;

    public ChatOutboxDispatcher(
        IChatDbContext dbContext,
        ISender sender,
        IChatRealtimePublisher? publisher = null,
        ChatDispatchOptions? options = null,
        Func<DateTime>? utcNow = null,
        Func<int>? jitterMilliseconds = null,
        IChatUserLookup? userLookup = null)
    {
        _dbContext = dbContext;
        _sender = sender;
        _publisher = publisher;
        _options = options ?? new ChatDispatchOptions();
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _jitterMilliseconds = jitterMilliseconds ?? (() => Random.Shared.Next(0, 1000));
        _userLookup = userLookup;
    }

    /// <returns>Published change count + delivered intent count.</returns>
    public async Task<(int ChangesPublished, int IntentsDelivered)> DispatchAsync(
        CancellationToken cancellationToken = default)
    {
        int changes = await PublishChangesAsync(cancellationToken);
        int intents = await DeliverIntentsAsync(cancellationToken);
        return (changes, intents);
    }

    private async Task<int> PublishChangesAsync(CancellationToken cancellationToken)
    {
        if (_publisher is null)
        {
            return 0;
        }

        // Whole-struct ordering does not evaluate on every provider, so the
        // bounded batch is ordered in memory by (conversation, revision).
        List<ChatChange> rows = (await _dbContext.Changes
                .Where(item => item.PublishedOnUtc == null)
                .Take(_options.BatchSize)
                .ToListAsync(cancellationToken))
            .OrderBy(item => item.ConversationId.Value)
            .ThenBy(item => item.Revision)
            .ToList();

        int published = 0;
        DateTime now = _utcNow();

        foreach (ChatChange row in rows)
        {
            Message? message = await _dbContext.Messages
                .SingleOrDefaultAsync(
                    item => item.Id == row.MessageId,
                    cancellationToken);

            if (message is null)
            {
                continue;
            }

            try
            {
                if (row.Type == ChatChangeType.MessageCreated)
                {
                    await _publisher.PublishMessageCreatedAsync(
                        row.ConversationId.Value,
                        MessageItemFactory.Create(message, readOnUtc: null),
                        cancellationToken);
                }
                else
                {
                    // Stored set-once stamps: the receipt payload carries
                    // exactly what REST history shows; clients merge
                    // monotonically and hydrate unknowns through sync.
                    await _publisher.PublishReceiptUpdatedAsync(
                        row.ConversationId.Value,
                        message.Id.Value,
                        message.DeliveredOnUtc,
                        message.ReadOnUtc,
                        cancellationToken);
                }

                row.MarkPublished(now);
                published++;
            }
            catch (Exception)
            {
                // Leave unpublished for the next cycle with backoff: the
                // dispatcher never throws a single-item failure out of the
                // batch. Hub clients converge through REST sync meanwhile.
                break;
            }
        }

        if (published > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return published;
    }

    private async Task<int> DeliverIntentsAsync(CancellationToken cancellationToken)
    {
        DateTime now = _utcNow();
        DateTime staleBefore = now - _options.StaleClaimAfter;

        List<ChatOutboxItem> due = await _dbContext.Outbox
            .Where(item =>
                item.PublishedOnUtc == null &&
                item.NextAttemptOnUtc <= now &&
                item.AttemptCount < _options.MaximumAttempts &&
                (item.ClaimToken == null || item.ClaimedOnUtc == null || item.ClaimedOnUtc <= staleBefore))
            .OrderBy(item => item.CreatedOnUtc)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        int delivered = 0;

        foreach (ChatOutboxItem item in due)
        {
            Guid claim = Guid.CreateVersion7();
            item.Claim(claim, now);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception)
            {
                continue;
            }

            // Dispatch-time eligibility: block/visibility/account state may
            // have changed between send-commit and this cycle. Mirror the
            // send-time direction — suppress when the intent recipient is no
            // longer an eligible visible participant, or blocked this
            // counterpart (the sender). The account liveness check runs only
            // when a lookup is wired (production); unit fakes omit it.
            if (!await IsIntentRecipientEligibleAsync(item, cancellationToken))
            {
                item.MarkSuppressed(
                    "Chat.Outbox.RecipientIneligible",
                    _utcNow(),
                    claim);
                await _dbContext.SaveChangesAsync(cancellationToken);
                continue;
            }

            try
            {
                await _sender.Send(
                    new CreateChatMessageNotificationCommand(
                        item.RecipientUserId.Value,
                        item.ConversationId.Value,
                        item.MessageId.Value,
                        item.CreatedOnUtc),
                    cancellationToken);

                item.MarkPublished(_utcNow(), claim);
                delivered++;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                TimeSpan delay = RetryDelay(item.AttemptCount + 1);
                item.MarkAttemptFailed(
                    "Chat.Outbox." + exception.GetType().Name,
                    _utcNow(),
                    claim,
                    delay);

                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (Exception)
                {
                    // The claim stays with its stale instant and is
                    // reclaimed by a later cycle.
                }
            }
        }

        return delivered;
    }

    private async Task<bool> IsIntentRecipientEligibleAsync(
        ChatOutboxItem item,
        CancellationToken cancellationToken)
    {
        Conversation? conversation = await _dbContext.Conversations
            .SingleOrDefaultAsync(
                entity => entity.Id == item.ConversationId,
                cancellationToken);

        if (conversation is null ||
            !conversation.IsParticipant(item.RecipientUserId) ||
            conversation.IsHiddenFor(item.RecipientUserId))
        {
            return false;
        }

        UserId counterpart =
            conversation.ParticipantOne == item.RecipientUserId
                ? conversation.ParticipantTwo
                : conversation.ParticipantOne;

        bool blockedByRecipient = await _dbContext.Blocks.AnyAsync(
            block =>
                block.BlockedUserId == counterpart &&
                block.BlockerUserId == item.RecipientUserId,
            cancellationToken);

        if (blockedByRecipient)
        {
            return false;
        }

        if (_userLookup is not null)
        {
            UserProfile? profile = await _userLookup.GetProfileAsync(
                item.RecipientUserId,
                cancellationToken);

            if (profile is null)
            {
                return false;
            }
        }

        return true;
    }

    internal TimeSpan RetryDelay(int attempt) =>
        ChatOutboxItem.DelayForAttempt(attempt, _jitterMilliseconds());
}
