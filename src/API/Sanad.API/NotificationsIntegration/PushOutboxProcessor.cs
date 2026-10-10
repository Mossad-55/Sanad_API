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
                await DeliverAsync(notifications, identity, sender, id, claimToken, cancellationToken);
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
        IPushSender sender, Guid id, Guid claimToken, CancellationToken cancellationToken)
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

    private static bool IsEnabled(NotificationPreferences preferences, string key) => key switch
    {
        "BookingUpdates" => preferences.BookingUpdates,
        "MedicationReminders" => preferences.MedicationReminders,
        "CheckInAlerts" => preferences.CheckInAlerts,
        "CommunityNotifications" => preferences.CommunityNotifications,
        "MessagesFromFamilies" => preferences.MessagesFromFamilies,
        "HelpRequestAlerts" => preferences.HelpRequestAlerts,
        "SystemNotifications" => preferences.SystemNotifications,
        _ => false
    };
}
