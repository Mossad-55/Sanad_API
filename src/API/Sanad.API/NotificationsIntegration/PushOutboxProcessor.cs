using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Notifications.Application.Abstractions.Push;
using Sanad.Modules.Notifications.Domain.Notifications;
using Sanad.Modules.Notifications.Infrastructure.Persistence;

namespace Sanad.API.NotificationsIntegration;

public sealed class PushOutboxProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<PushOutboxProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessBatchAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogError("Push outbox processing failed with {ErrorType}.", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    public async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IPushSender>();
        var chat = scope.ServiceProvider.GetService<Sanad.Modules.Chat.Application.Abstractions.Data.IChatDbContext>();
        var sessionValidator = scope.ServiceProvider.GetService<Security.IIdentitySessionValidator>();
        DateTime now = DateTime.UtcNow;
        DateTime staleClaim = now.AddMinutes(-15);
        Guid[] ids = await notifications.PushOutboxMessages.AsNoTracking()
            .Where(x => ((x.Status == PushOutboxStatus.Pending && x.NextAttemptOnUtc <= now) ||
                         (x.Status == PushOutboxStatus.Processing && x.LastAttemptOnUtc < staleClaim)))
            .OrderBy(x => x.CreatedOnUtc).Take(20).Select(x => x.Id).ToArrayAsync(cancellationToken);

        foreach (Guid id in ids)
        {
            Guid claimToken = Guid.NewGuid();
            int claimed = await notifications.PushOutboxMessages.Where(x => x.Id == id &&
                    ((x.Status == PushOutboxStatus.Pending && x.NextAttemptOnUtc <= now) ||
                     (x.Status == PushOutboxStatus.Processing && x.LastAttemptOnUtc < staleClaim)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, PushOutboxStatus.Processing)
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                    .SetProperty(x => x.LastAttemptOnUtc, now)
                    .SetProperty(x => x.ClaimToken, claimToken), cancellationToken);
            if (claimed != 1) continue;

            PushOutboxMessage? row = await notifications.PushOutboxMessages.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (row is null) continue;
            notifications.Entry(row).State = EntityState.Detached;
            try
            {
                await DeliverAsync(notifications, identity, sender, id, claimToken, chat, sessionValidator, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                notifications.ChangeTracker.Clear();
                PushOutboxMessage? failed = await notifications.PushOutboxMessages.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
                if (failed is null || failed.ClaimToken != claimToken) continue;
                failed.MarkFailed("Push.ProcessorFailure", DateTime.UtcNow, claimToken, retryable: true);
                await notifications.SaveChangesAsync(cancellationToken);
                logger.LogWarning("Push outbox item {MessageId} failed with {ErrorType}; retry scheduled.", id, ex.GetType().Name);
            }
        }
    }

    private static async Task DeliverAsync(NotificationsDbContext notifications, IdentityDbContext identity,
        IPushSender sender, Guid id, Guid claimToken,
        Sanad.Modules.Chat.Application.Abstractions.Data.IChatDbContext? chat,
        Security.IIdentitySessionValidator? sessionValidator,
        CancellationToken cancellationToken)
    {
        PushOutboxMessage row = await notifications.PushOutboxMessages.SingleAsync(x => x.Id == id, cancellationToken);
        var preference = await identity.Users.AsNoTracking().Where(x => x.Id.Value == row.RecipientUserId)
            .Select(x => x.NotificationPreferences).SingleOrDefaultAsync(cancellationToken);
        if (preference is null || !IsEnabled(preference, row.PreferenceKey))
        {
            row.MarkSuppressed(claimToken);
            await notifications.SaveChangesAsync(cancellationToken);
            return;
        }

        // Final-send chat gate: recheck current eligibility immediately
        // before any provider send. Preserves the established directional
        // message/block rule (suppress only when the recipient blocked the
        // sender); never invents a different recipient policy. Ineligible
        // work is suppressed durably, never retried as a provider failure.
        if (string.Equals(row.PreferenceKey, "ChatMessages", StringComparison.Ordinal))
        {
            bool eligible = await IsChatPushEligibleAsync(
                notifications, identity, chat, sessionValidator, row, cancellationToken);

            if (!eligible)
            {
                // Re-read the row in this context before suppressing: the
                // earlier SingleAsync entity is still tracked.
                row.MarkSuppressed(claimToken);
                await notifications.SaveChangesAsync(cancellationToken);
                return;
            }
        }

        List<DeviceToken> devices = await notifications.DeviceTokens.Where(x => x.UserId == row.RecipientUserId).ToListAsync(cancellationToken);
        if (devices.Count == 0)
        {
            row.MarkSuppressed(claimToken);
            await notifications.SaveChangesAsync(cancellationToken);
            return;
        }

        bool retry = false;
        string? firstMessageId = null;
        string retryCode = "Push.SendFailed";
        IReadOnlyDictionary<string, string> data = row.ReadData();
        foreach (DeviceToken device in devices)
        {
            PushSendResult result = await sender.SendAsync(device.Token, row.Title, row.Body, data, cancellationToken);
            switch (result.Outcome)
            {
                case PushSendOutcome.Sent:
                    firstMessageId ??= result.MessageId;
                    break;
                case PushSendOutcome.InvalidToken:
                    notifications.DeviceTokens.Remove(device);
                    break;
                case PushSendOutcome.NotConfigured:
                    retry = true;
                    retryCode = "Push.NotConfigured";
                    break;
                default:
                    retry = true;
                    retryCode = result.ErrorCode;
                    break;
            }
        }

        if (retry) row.MarkFailed(retryCode, DateTime.UtcNow, claimToken, retryable: true);
        else row.MarkSent(DateTime.UtcNow, claimToken, firstMessageId);
        await notifications.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Final-send chat eligibility, evaluated immediately before each queued
    /// chat push provider send: current recipient session/device ownership,
    /// account status, chat visibility, directional block policy, and
    /// chatMessages preference. Returns false to suppress durably. Uses only
    /// fake-provider-safe reads; never sends a provider notification here.
    /// </summary>
    public static async Task<bool> IsChatPushEligibleAsync(
        NotificationsDbContext notifications,
        IdentityDbContext identity,
        Sanad.Modules.Chat.Application.Abstractions.Data.IChatDbContext? chat,
        Security.IIdentitySessionValidator? sessionValidator,
        PushOutboxMessage row,
        CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;
        var recipient = new Sanad.BuildingBlocks.Domain.Primitives.Ids.UserId(
            row.RecipientUserId);

        // Session/device ownership + account status: the recipient must
        // still own at least one active session and be Active. When the
        // validator is unavailable (isolated tests), fall back to the
        // preference/devices checks the caller already performs.
        if (sessionValidator is not null)
        {
            bool hasSession;

            try
            {
                hasSession = await sessionValidator.HasAnyActiveSessionAsync(
                    recipient, now, cancellationToken);
            }
            catch (Exception)
            {
                return false;
            }

            if (!hasSession)
            {
                return false;
            }
        }

        // Device ownership recheck: the recipient must still own a push
        // device at send time (logout/unregister suppresses).
        bool ownsDevice = await notifications.DeviceTokens
            .AsNoTracking()
            .AnyAsync(
                device => device.UserId == row.RecipientUserId,
                cancellationToken);

        if (!ownsDevice)
        {
            return false;
        }

        // Chat visibility/block/message sender: requires the Chat store.
        // When unavailable (isolated notification tests), preserve the
        // existing preference/device behavior.
        if (chat is null)
        {
            return true;
        }

        IReadOnlyDictionary<string, string> data;
        try
        {
            data = row.ReadData();
        }
        catch (Exception)
        {
            return false;
        }

        if (!data.TryGetValue("conversationId", out string? conversationValue) ||
            !Guid.TryParse(conversationValue, out Guid conversationGuid) ||
            conversationGuid == Guid.Empty)
        {
            return false;
        }

        var conversationId =
            new Sanad.Modules.Chat.Domain.Conversations.ConversationId(
                conversationGuid);
        var recipientId =
            new Sanad.BuildingBlocks.Domain.Primitives.Ids.UserId(
                row.RecipientUserId);

        Sanad.Modules.Chat.Domain.Conversations.Conversation? conversation;

        try
        {
            conversation = await chat.Conversations
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    entity => entity.Id == conversationId,
                    cancellationToken);
        }
        catch (Exception)
        {
            return false;
        }

        if (conversation is null ||
            !conversation.IsParticipant(recipientId) ||
            conversation.IsHiddenFor(recipientId))
        {
            return false;
        }

        Sanad.BuildingBlocks.Domain.Primitives.Ids.UserId counterpart =
            conversation.ParticipantOne == recipientId
                ? conversation.ParticipantTwo
                : conversation.ParticipantOne;

        // Sender for the directional block check: prefer the stored message
        // sender when the message id is available; otherwise the counterpart.
        Sanad.BuildingBlocks.Domain.Primitives.Ids.UserId senderId = counterpart;

        if (data.TryGetValue("messageId", out string? messageValue) &&
            Guid.TryParse(messageValue, out Guid messageGuid) &&
            messageGuid != Guid.Empty)
        {
            try
            {
                var messageId =
                    new Sanad.Modules.Chat.Domain.Messages.MessageId(
                        messageGuid);

                Guid? storedSender = await chat.Messages
                    .AsNoTracking()
                    .Where(message => message.Id == messageId)
                    .Select(message => (Guid?)message.SenderUserId.Value)
                    .SingleOrDefaultAsync(cancellationToken);

                if (storedSender.HasValue && storedSender.Value != Guid.Empty)
                {
                    senderId =
                        new Sanad.BuildingBlocks.Domain.Primitives.Ids.UserId(
                            storedSender.Value);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Established directional rule only: suppress when the recipient
        // blocked the sender. A sender-side block does not suppress delivery
        // to the recipient (blocker may still send).
        bool blockedByRecipient;

        try
        {
            blockedByRecipient = await chat.Blocks
                .AsNoTracking()
                .AnyAsync(
                    block =>
                        block.BlockerUserId == recipientId &&
                        block.BlockedUserId == senderId,
                    cancellationToken);
        }
        catch (Exception)
        {
            return false;
        }

        return !blockedByRecipient;
    }

    private static bool IsEnabled(NotificationPreferences preferences, string key) => key switch
    {
        "BookingUpdates" => preferences.BookingUpdates,
        "MedicationReminders" => preferences.MedicationReminders,
        "CheckInAlerts" => preferences.CheckInAlerts,
        "CommunityNotifications" => preferences.CommunityNotifications,
        "MessagesFromFamilies" => preferences.MessagesFromFamilies,
        // Chat Plus canonical key; legacy rows were re-mapped to it by the
        // fan-out policy, so old opt-outs keep working through the
        // preference compatibility rule.
        "ChatMessages" => preferences.ChatMessages,
        "HelpRequestAlerts" => preferences.HelpRequestAlerts,
        "SystemNotifications" => preferences.SystemNotifications,
        _ => false
    };
}
