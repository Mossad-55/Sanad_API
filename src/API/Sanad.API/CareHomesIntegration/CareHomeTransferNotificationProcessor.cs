using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Notifications.Domain.Notifications;
using Sanad.Modules.Notifications.Infrastructure.Persistence;

namespace Sanad.API.CareHomesIntegration;

/// <summary>Materializes durable transfer events into the shared in-app and email outboxes.</summary>
public sealed class CareHomeTransferNotificationProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<CareHomeTransferNotificationProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessBatchAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogError(ex, "Care-home transfer notification processing failed."); }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var careHomes = scope.ServiceProvider.GetRequiredService<CareHomesDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var now = DateTime.UtcNow;
        var candidates = await careHomes.TransferNotificationOutbox.AsNoTracking()
            .Where(x => (x.Status == CareHomeTransferNotificationStatus.Pending || x.Status == CareHomeTransferNotificationStatus.Failed) && x.NextAttemptOnUtc <= now
                || x.Status == CareHomeTransferNotificationStatus.Processing && x.LastAttemptOnUtc < now.AddMinutes(-15))
            .OrderBy(x => x.CreatedOnUtc).Take(20).Select(x => x.Id).ToListAsync(ct);

        foreach (var id in candidates)
        {
            notifications.ChangeTracker.Clear();
            var token = Guid.NewGuid();
            int claimed = await careHomes.TransferNotificationOutbox.Where(x => x.Id == id &&
                    ((x.Status == CareHomeTransferNotificationStatus.Pending || x.Status == CareHomeTransferNotificationStatus.Failed) && x.NextAttemptOnUtc <= now
                     || x.Status == CareHomeTransferNotificationStatus.Processing && x.LastAttemptOnUtc < now.AddMinutes(-15)))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, CareHomeTransferNotificationStatus.Processing)
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                    .SetProperty(x => x.LastAttemptOnUtc, now)
                    .SetProperty(x => x.ClaimToken, token), ct);
            if (claimed != 1) continue;

            var row = await careHomes.TransferNotificationOutbox.SingleAsync(x => x.Id == id, ct);
            try
            {
                var transfer = await careHomes.BookingAssignmentHistory.AsNoTracking().SingleAsync(x => x.Id == row.TransferId, ct);
                var booking = await careHomes.Bookings.AsNoTracking().SingleAsync(x => x.Id == row.BookingId, ct);
                var recipient = await identity.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == booking.FamilyUserId, ct);
                if (recipient is null)
                    throw new InvalidOperationException("The booking Family recipient is unavailable.");

                string from = await ResourceLabelAsync(careHomes, transfer.FromRoomId, transfer.FromBedId, ct);
                string to = await ResourceLabelAsync(careHomes, transfer.ToRoomId, transfer.ToBedId, ct);
                bool arabic = recipient.UiLanguage == UiLanguage.Arabic;
                string title = arabic ? "تم تحديث تخصيص الغرفة" : "Care-home room assignment updated";
                string body = arabic
                    ? $"سيتغير تخصيص إقامتك من {from} إلى {to} بتاريخ {transfer.EffectiveDate:yyyy-MM-dd}. رقم الحجز: {booking.Id}."
                    : $"Your care-home stay assignment changes from {from} to {to} on {transfer.EffectiveDate:yyyy-MM-dd}. Booking: {booking.Id}.";
                string prefix = $"care-home-transfer:{transfer.Id:N}:{booking.FamilyUserId.Value:N}";
                var inAppKey = prefix + ":in-app";
                if (!await notifications.Notifications.AnyAsync(x => x.IdempotencyKey == inAppKey, ct))
                {
                    notifications.Notifications.Add(Notification.Create(booking.FamilyUserId.Value, "CareHomes", "RoomTransferred",
                        title, body, "CareHomeBooking", booking.Id, now, inAppKey));
                    await SaveIdempotentlyAsync(notifications, ct);
                }

                if (recipient.Email is null)
                    throw new InvalidOperationException("The booking Family has no email address for the required email notification.");
                var emailKey = prefix + ":email";
                if (!await notifications.EmailOutboxMessages.AnyAsync(x => x.IdempotencyKey == emailKey, ct))
                {
                    notifications.EmailOutboxMessages.Add(EmailOutboxMessage.Create(recipient.Email.Value, title, body, emailKey, now));
                    await SaveIdempotentlyAsync(notifications, ct);
                }
                row.MarkCompleted();
                await careHomes.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                notifications.ChangeTracker.Clear();
                careHomes.Entry(row).State = EntityState.Detached;
                var retry = await careHomes.TransferNotificationOutbox.SingleAsync(x => x.Id == id, ct);
                retry.MarkFailed(ex.Message, DateTime.UtcNow, token);
                await careHomes.SaveChangesAsync(ct);
                logger.LogWarning(ex, "Transfer notification outbox item {OutboxId} will retry.", id);
            }
        }
    }

    private static async Task<string> ResourceLabelAsync(CareHomesDbContext db, Guid? roomId, Guid? bedId, CancellationToken ct)
    {
        if (roomId is null) return "unassigned accommodation";
        var room = await db.Rooms.AsNoTracking().SingleAsync(x => x.Id == roomId, ct);
        if (bedId is not Guid id) return $"room {room.RoomNumber}";
        var bed = await db.Beds.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        return $"room {room.RoomNumber}, bed {bed.Label}";
    }

    private static async Task SaveIdempotentlyAsync(NotificationsDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A previous attempt may have committed this channel before crashing.
            db.ChangeTracker.Clear();
        }
    }
}
