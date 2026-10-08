using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Notifications.Domain.Notifications;
using Sanad.Modules.Notifications.Infrastructure.Persistence;

namespace Sanad.API.CareHomesIntegration;

/// <summary>Materializes committed booking events into idempotent in-app and email outboxes.</summary>
public sealed class CareHomeBookingNotificationProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<CareHomeBookingNotificationProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessBatchAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogError(ex, "Care-home booking notification processing failed."); }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    public async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var careHomes = scope.ServiceProvider.GetRequiredService<CareHomesDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        DateTime now = DateTime.UtcNow;
        var candidates = await careHomes.BookingNotificationOutbox.AsNoTracking()
            .Where(x => ((x.Status == CareHomeNotificationOutboxStatus.Pending || x.Status == CareHomeNotificationOutboxStatus.Failed)
                         && x.NextAttemptOnUtc <= now)
                        || (x.Status == CareHomeNotificationOutboxStatus.Processing && x.LastAttemptOnUtc < now.AddMinutes(-15)))
            .OrderBy(x => x.CreatedOnUtc).Take(20).Select(x => x.Id).ToListAsync(ct);

        foreach (Guid id in candidates)
        {
            notifications.ChangeTracker.Clear();
            Guid token = Guid.NewGuid();
            var row = await careHomes.BookingNotificationOutbox.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (row is null || !(((row.Status is CareHomeNotificationOutboxStatus.Pending or CareHomeNotificationOutboxStatus.Failed)
                                  && row.NextAttemptOnUtc <= now)
                                 || (row.Status == CareHomeNotificationOutboxStatus.Processing && row.LastAttemptOnUtc < now.AddMinutes(-15))))
                continue;
            row.MarkProcessing(now, token);
            try { await careHomes.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException)
            {
                careHomes.Entry(row).State = EntityState.Detached;
                continue;
            }
            try
            {
                var booking = await careHomes.Bookings.AsNoTracking().SingleAsync(x => x.Id == row.BookingId, ct);
                var facility = await careHomes.Facilities.AsNoTracking().SingleAsync(x => x.Id == booking.FacilityId, ct);
                var recipients = await ResolveRecipientsAsync(identity, row.EventType, booking.FamilyUserId.Value,
                    facility.OwnerUserId.Value, ct);
                var copy = Copy(row.EventType, booking.Id);
                Guid destinationId = row.DisputeId ?? booking.Id;
                string destinationKind = row.DisputeId.HasValue ? "CareHomeCheckInDispute" : "CareHomeBooking";

                foreach (var recipient in recipients)
                {
                    bool arabic = recipient.UiLanguage == UiLanguage.Arabic;
                    string title = arabic ? copy.TitleAr : copy.TitleEn;
                    string body = arabic ? copy.BodyAr : copy.BodyEn;
                    string key = $"care-home-event:{row.Id:N}:{recipient.Id:N}";
                    await WriteInAppAsync(notifications, recipient.Id, copy.Type, title, body,
                        destinationKind, destinationId, key + ":in-app", row.CreatedOnUtc, ct);
                    if (!string.IsNullOrWhiteSpace(recipient.Email))
                        await WriteEmailAsync(notifications, recipient.Email, title, body, key + ":email", row.CreatedOnUtc, ct);
                }

                row.MarkCompleted(token);
                await careHomes.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                notifications.ChangeTracker.Clear();
                careHomes.Entry(row).State = EntityState.Detached;
                var retry = await careHomes.BookingNotificationOutbox.SingleAsync(x => x.Id == id, ct);
                if (retry.ClaimToken != token) continue;
                retry.MarkFailed(ex.Message, DateTime.UtcNow, token);
                await careHomes.SaveChangesAsync(ct);
                logger.LogWarning(ex, "Care-home booking notification outbox item {OutboxId} will retry.", id);
            }
        }
    }

    private static async Task<List<Recipient>> ResolveRecipientsAsync(IdentityDbContext identity,
        CareHomeNotificationEvent eventType, Guid familyId, Guid ownerId, CancellationToken ct)
    {
        bool adminsOnly = eventType is CareHomeNotificationEvent.RefundFailed or CareHomeNotificationEvent.DisputeOpened;
        bool ownerOnly = eventType is CareHomeNotificationEvent.OwnerDecisionNeeded or CareHomeNotificationEvent.FamilyCancelledPaidBooking;
        if (adminsOnly)
            return await identity.Users.AsNoTracking().Where(x => x.Status == UserStatus.Active &&
                    x.Accounts.Any(a => a.AccountType == AccountType.SuperAdmin || a.AccountType == AccountType.SupportAdmin))
                .Select(x => new Recipient(x.Id.Value, x.Email == null ? null : x.Email.Value, x.UiLanguage)).ToListAsync(ct);

        Guid[] targets = eventType == CareHomeNotificationEvent.DisputeResolved
            ? [familyId, ownerId]
            : [ownerOnly ? ownerId : familyId];
        return await identity.Users.AsNoTracking().Where(x => targets.Contains(x.Id.Value))
            .Select(x => new Recipient(x.Id.Value, x.Email == null ? null : x.Email.Value, x.UiLanguage)).ToListAsync(ct);
    }

    private static (string Type, string TitleEn, string TitleAr, string BodyEn, string BodyAr) Copy(CareHomeNotificationEvent eventType, Guid bookingId) => eventType switch
    {
        CareHomeNotificationEvent.PaymentSucceeded => ("BookingPaymentSucceeded", "Payment received", "تم استلام الدفع", $"Payment for care-home booking {bookingId} was received.", $"تم استلام الدفع لحجز دار الرعاية {bookingId}."),
        CareHomeNotificationEvent.OwnerDecisionNeeded => ("BookingDecisionRequired", "Booking needs your decision", "حجز بانتظار قرارك", $"Care-home booking {bookingId} is waiting for your decision.", $"حجز دار الرعاية {bookingId} بانتظار قرارك."),
        CareHomeNotificationEvent.DecisionAccepted => ("BookingAccepted", "Booking accepted", "تم قبول الحجز", $"Your care-home booking {bookingId} was accepted.", $"تم قبول حجز دار الرعاية {bookingId}."),
        CareHomeNotificationEvent.DecisionRejected => ("BookingRejected", "Booking not accepted", "لم يتم قبول الحجز", $"Your care-home booking {bookingId} was not accepted. Refund processing is handled separately.", $"لم يتم قبول حجز دار الرعاية {bookingId}. تتم معالجة الاسترداد بشكل منفصل."),
        CareHomeNotificationEvent.DecisionExpired => ("BookingDecisionExpired", "Booking decision window ended", "انتهت مهلة اتخاذ القرار", $"The decision window for care-home booking {bookingId} ended. Refund processing is handled separately.", $"انتهت مهلة اتخاذ القرار لحجز دار الرعاية {bookingId}. تتم معالجة الاسترداد بشكل منفصل."),
        CareHomeNotificationEvent.FamilyCancelledPaidBooking => ("BookingCancelledByFamily", "Paid booking cancelled", "تم إلغاء حجز مدفوع", $"The Family cancelled paid care-home booking {bookingId}. Refund processing is handled separately.", $"ألغت الأسرة حجز دار الرعاية المدفوع {bookingId}. تتم معالجة الاسترداد بشكل منفصل."),
        CareHomeNotificationEvent.OwnerCancelledAcceptedBooking => ("BookingCancelledByFacility", "Care-home booking cancelled", "تم إلغاء حجز دار الرعاية", $"The facility cancelled accepted care-home booking {bookingId}. Refund processing is handled separately.", $"ألغت المنشأة حجز دار الرعاية المقبول {bookingId}. تتم معالجة الاسترداد بشكل منفصل."),
        CareHomeNotificationEvent.RefundCompleted => ("BookingRefundCompleted", "Refund completed", "اكتمل استرداد المبلغ", $"The refund for care-home booking {bookingId} is complete.", $"اكتمل استرداد المبلغ لحجز دار الرعاية {bookingId}."),
        CareHomeNotificationEvent.RefundFailed => ("BookingRefundFailed", "Care-home refund needs follow-up", "استرداد دار الرعاية يتطلب متابعة", $"A refund attempt for care-home booking {bookingId} failed and needs operational follow-up.", $"فشلت محاولة استرداد لحجز دار الرعاية {bookingId} وتتطلب متابعة تشغيلية."),
        CareHomeNotificationEvent.DisputeOpened => ("CheckInDisputeOpened", "Check-in dispute opened", "تم فتح نزاع تسجيل الوصول", $"A check-in dispute was opened for care-home booking {bookingId}.", $"تم فتح نزاع بشأن تسجيل الوصول لحجز دار الرعاية {bookingId}."),
        CareHomeNotificationEvent.DisputeResolved => ("CheckInDisputeResolved", "Check-in dispute resolved", "تم حل نزاع تسجيل الوصول", $"The check-in dispute for care-home booking {bookingId} was resolved.", $"تم حل نزاع تسجيل الوصول لحجز دار الرعاية {bookingId}."),
        _ => throw new ArgumentOutOfRangeException(nameof(eventType))
    };

    private static async Task WriteInAppAsync(NotificationsDbContext db, Guid userId, string type,
        string title, string body, string destinationKind, Guid destinationId, string key, DateTime created, CancellationToken ct)
    {
        if (await db.Notifications.AnyAsync(x => x.IdempotencyKey == key, ct)) return;
        var notification = Notification.Create(userId, "CareHomes", type, title, body, destinationKind, destinationId, created, key);
        db.Notifications.Add(notification);
        await SaveIdempotentlyAsync(db, notification, ct);
    }

    private static async Task WriteEmailAsync(NotificationsDbContext db, string email, string title,
        string body, string key, DateTime created, CancellationToken ct)
    {
        if (await db.EmailOutboxMessages.AnyAsync(x => x.IdempotencyKey == key, ct)) return;
        var message = EmailOutboxMessage.Create(email, title, body, key, created);
        db.EmailOutboxMessages.Add(message);
        await SaveIdempotentlyAsync(db, message, ct);
    }

    private static async Task SaveIdempotentlyAsync(NotificationsDbContext db, object item, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { db.Entry(item).State = EntityState.Detached; }
    }

    private sealed record Recipient(Guid Id, string? Email, UiLanguage UiLanguage);
}
