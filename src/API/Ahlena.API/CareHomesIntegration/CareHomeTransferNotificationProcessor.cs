using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.Modules.CareHomes.Domain.Bookings;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;
using Ahlena.Modules.Identity.Infrastructure.Persistence;
using Ahlena.Modules.Identity.Infrastructure.Messaging.EmailTemplates;
using Ahlena.Modules.Notifications.Domain.Notifications;
using Ahlena.Modules.Notifications.Infrastructure.Persistence;

namespace Ahlena.API.CareHomesIntegration;

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

                (string Arabic, string English) from = await ResourceLabelsAsync(careHomes, transfer.FromRoomId, transfer.FromBedId, ct);
                (string Arabic, string English) to = await ResourceLabelsAsync(careHomes, transfer.ToRoomId, transfer.ToBedId, ct);
                bool arabic = recipient.UiLanguage == UiLanguage.Arabic;
                var copy = Copy(from, to, transfer.EffectiveDate);
                string title = arabic ? copy.TitleAr : copy.TitleEn;
                string body = arabic ? copy.BodyAr : copy.BodyEn;
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
                    var email = EmailTemplateCatalog.RenderBilingual(copy.TitleAr, copy.TitleEn,
                        copy.TitleAr, copy.TitleEn, copy.BodyAr, copy.BodyEn);
                    notifications.EmailOutboxMessages.Add(EmailOutboxMessage.Create(recipient.Email.Value,
                        email.Subject, email.PlainTextBody, email.HtmlBody, emailKey, now));
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

    /// <summary>
    /// F8 copy for one transfer: the labelled move in both languages with its
    /// effective date, with the booking kept on the structured destination
    /// instead of being repeated as a raw identifier inside the text.
    /// </summary>
    public static (string TitleAr, string TitleEn, string BodyAr, string BodyEn) Copy(
        (string Arabic, string English) from, (string Arabic, string English) to, DateOnly effectiveDate) =>
        ("تم تحديث تخصيص الغرفة",
            "Care-home room assignment updated",
            $"سيتغير تخصيص إقامتك من {from.Arabic} إلى {to.Arabic} بتاريخ {effectiveDate:yyyy-MM-dd}.",
            $"Your care-home stay assignment changes from {from.English} to {to.English} on {effectiveDate:yyyy-MM-dd}.");

    /// <summary>
    /// F8: the labelled accommodation of a transfer segment, in both
    /// languages, so an Arabic recipient reads Arabic room/bed labels.
    /// </summary>
    public static async Task<(string Arabic, string English)> ResourceLabelsAsync(CareHomesDbContext db, Guid? roomId, Guid? bedId, CancellationToken ct)
    {
        if (roomId is null) return ("غير محدد", "unassigned");
        var room = await db.Rooms.AsNoTracking().SingleAsync(x => x.Id == roomId, ct);
        if (bedId is not Guid id) return ($"غرفة {room.RoomNumber}", $"room {room.RoomNumber}");
        var bed = await db.Beds.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        return ($"غرفة {room.RoomNumber}، سرير {bed.Label}", $"room {room.RoomNumber}, bed {bed.Label}");
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
