using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Identity.Infrastructure.Messaging.EmailTemplates;
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
                var copy = Copy(row.EventType);
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
                    {
                        var email = EmailTemplateCatalog.RenderBilingual(
                            copy.TitleAr, copy.TitleEn, copy.TitleAr, copy.TitleEn, copy.BodyAr, copy.BodyEn);
                        await WriteEmailAsync(notifications, recipient.Email, email.Subject, email.PlainTextBody,
                            email.HtmlBody, key + ":email", row.CreatedOnUtc, ct);
                    }
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

    public static async Task<List<BookingRecipient>> ResolveRecipientsAsync(IdentityDbContext identity,
        CareHomeNotificationEvent eventType, Guid familyId, Guid ownerId, CancellationToken ct)
    {
        bool adminsOnly = eventType is CareHomeNotificationEvent.RefundFailed or CareHomeNotificationEvent.DisputeOpened;
        bool ownerOnly = eventType is CareHomeNotificationEvent.OwnerDecisionNeeded or CareHomeNotificationEvent.FamilyCancelledPaidBooking;
        if (adminsOnly)
        {
            var adminRows = await OperationalAdminRecipientRows(identity).ToListAsync(ct);
            return adminRows.Select(x => new BookingRecipient(x.Id.Value, x.Email == null ? null : x.Email.Value, x.UiLanguage)).ToList();
        }

        UserId[] targets = eventType == CareHomeNotificationEvent.DisputeResolved
            ? [new UserId(familyId), new UserId(ownerId)]
            : [new UserId(ownerOnly ? ownerId : familyId)];
        var rows = await TargetRecipientRows(identity, targets).ToListAsync(ct);
        return rows.Select(x => new BookingRecipient(x.Id.Value, x.Email == null ? null : x.Email.Value, x.UiLanguage)).ToList();
    }

    /// <summary>Recipient resolved for booking notifications.</summary>
    public sealed record BookingRecipient(Guid Id, string? Email, UiLanguage UiLanguage);

    /// <summary>Server-side recipient row with strongly-typed IDs.</summary>
    public sealed record BookingRecipientRow(UserId Id, Email? Email, UiLanguage UiLanguage);

    /// <summary>
    /// Provider-safe recipient lookups. Strongly-typed IDs are compared and
    /// projected directly so the EF value-converter translates on PostgreSQL;
    /// the <c>.Value</c> unwrap happens client-side after materialization.
    /// Member access such as <c>x.Id.Value</c> inside the query predicate is
    /// not translatable and must not be reintroduced here.
    /// </summary>
    public static IQueryable<BookingRecipientRow> TargetRecipientRows(IdentityDbContext identity, UserId[] targets) =>
        identity.Users.AsNoTracking().Where(x => targets.Contains(x.Id))
            .Select(x => new BookingRecipientRow(x.Id, x.Email, x.UiLanguage));

    public static IQueryable<BookingRecipientRow> OperationalAdminRecipientRows(IdentityDbContext identity) =>
        identity.Users.AsNoTracking().Where(x => x.Status == UserStatus.Active &&
                x.Accounts.Any(a => a.AccountType == AccountType.SuperAdmin || a.AccountType == AccountType.SupportAdmin))
            .Select(x => new BookingRecipientRow(x.Id, x.Email, x.UiLanguage));

    /// <summary>
    /// F8 copy for one booking event: a sentence a recipient can read, in both
    /// languages, with the booking kept only on the structured destination
    /// (kind + id) instead of repeated as a raw identifier in the text.
    /// </summary>
    public static (string Type, string TitleEn, string TitleAr, string BodyEn, string BodyAr) Copy(CareHomeNotificationEvent eventType) => eventType switch
    {
        CareHomeNotificationEvent.PaymentSucceeded => ("BookingPaymentSucceeded", "Payment received", "تم استلام الدفع", "Payment for your care-home booking was received.", "تم استلام الدفع لحجز دار الرعاية."),
        CareHomeNotificationEvent.OwnerDecisionNeeded => ("BookingDecisionRequired", "Booking needs your decision", "حجز بانتظار قرارك", "Your care-home booking is waiting for your decision.", "حجز دار الرعاية بانتظار قرارك."),
        CareHomeNotificationEvent.DecisionAccepted => ("BookingAccepted", "Booking accepted", "تم قبول الحجز", "Your care-home booking was accepted.", "تم قبول حجز دار الرعاية."),
        CareHomeNotificationEvent.DecisionRejected => ("BookingRejected", "Booking not accepted", "لم يتم قبول الحجز", "Your care-home booking was not accepted. Refund processing is handled separately.", "لم يتم قبول حجز دار الرعاية. تتم معالجة الاسترداد بشكل منفصل."),
        CareHomeNotificationEvent.DecisionExpired => ("BookingDecisionExpired", "Booking decision window ended", "انتهت مهلة اتخاذ القرار", "The decision window for your care-home booking ended. Refund processing is handled separately.", "انتهت مهلة اتخاذ القرار لحجز دار الرعاية. تتم معالجة الاسترداد بشكل منفصل."),
        CareHomeNotificationEvent.FamilyCancelledPaidBooking => ("BookingCancelledByFamily", "Paid booking cancelled", "تم إلغاء حجز مدفوع", "The Family cancelled a paid care-home booking. Refund processing is handled separately.", "ألغت الأسرة حجز دار الرعاية المدفوع. تتم معالجة الاسترداد بشكل منفصل."),
        CareHomeNotificationEvent.OwnerCancelledAcceptedBooking => ("BookingCancelledByFacility", "Care-home booking cancelled", "تم إلغاء حجز دار الرعاية", "The facility cancelled an accepted care-home booking. Refund processing is handled separately.", "ألغت المنشأة حجز دار الرعاية المقبول. تتم معالجة الاسترداد بشكل منفصل."),
        CareHomeNotificationEvent.RefundCompleted => ("BookingRefundCompleted", "Refund completed", "اكتمل استرداد المبلغ", "The refund for your care-home booking is complete.", "اكتمل استرداد المبلغ لحجز دار الرعاية."),
        CareHomeNotificationEvent.RefundFailed => ("BookingRefundFailed", "Care-home refund needs follow-up", "استرداد دار الرعاية يتطلب متابعة", "A refund attempt for a care-home booking failed and needs operational follow-up.", "فشلت محاولة استرداد لحجز دار الرعاية وتتطلب متابعة تشغيلية."),
        CareHomeNotificationEvent.DisputeOpened => ("CheckInDisputeOpened", "Check-in dispute opened", "تم فتح نزاع تسجيل الوصول", "A check-in dispute was opened for a care-home booking.", "تم فتح نزاع بشأن تسجيل الوصول لحجز دار الرعاية."),
        CareHomeNotificationEvent.DisputeResolved => ("CheckInDisputeResolved", "Check-in dispute resolved", "تم حل نزاع تسجيل الوصول", "The check-in dispute for a care-home booking was resolved.", "تم حل نزاع تسجيل الوصول لحجز دار الرعاية."),
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
        string plainTextBody, string htmlBody, string key, DateTime created, CancellationToken ct)
    {
        if (await db.EmailOutboxMessages.AnyAsync(x => x.IdempotencyKey == key, ct)) return;
        var message = EmailOutboxMessage.Create(email, title, plainTextBody, htmlBody, key, created);
        db.EmailOutboxMessages.Add(message);
        await SaveIdempotentlyAsync(db, message, ct);
    }

    private static async Task SaveIdempotentlyAsync(NotificationsDbContext db, object item, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { db.Entry(item).State = EntityState.Detached; }
    }
}
