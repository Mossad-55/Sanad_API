using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Identity.Infrastructure.Messaging.EmailTemplates;
using Sanad.Modules.Notifications.Domain.Notifications;
using Sanad.Modules.Notifications.Infrastructure.Persistence;

namespace Sanad.API.CareHomesIntegration;

/// <summary>Materializes durable submission and review-history events into owner/Admin alerts.</summary>
public sealed class CareHomeReviewNotificationProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<CareHomeReviewNotificationProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessBatchAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogError(ex, "Care-home review notification processing failed."); }
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var careHomes = scope.ServiceProvider.GetRequiredService<CareHomesDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var facilities = await careHomes.Facilities.AsNoTracking().Include(x => x.Revisions)
            .Include(x => x.ReviewHistory).AsSplitQuery().OrderByDescending(x => x.UpdatedOnUtc).Take(500).ToListAsync(ct);
        foreach (var facility in facilities)
        {
            foreach (var revision in facility.Revisions.Where(x => x.SubmittedOnUtc != null))
                await NotifyAsync(notifications, identity, facility.Id.Value, facility.OwnerUserId.Value,
                    $"submission:{revision.Id:N}", "Submitted", revision.SubmittedOnUtc!.Value,
                    notifyAdmins: true, ct);

            foreach (var history in facility.ReviewHistory)
            {
                bool adminAlert = history.Action == CareHomeReviewAction.CorrectionsRequested;
                string action = history.Action.ToString();
                await NotifyAsync(notifications, identity, facility.Id.Value, facility.OwnerUserId.Value,
                    $"review:{history.Id:N}", action, history.OccurredOnUtc, adminAlert, ct);
            }
        }
    }

    private static async Task NotifyAsync(NotificationsDbContext notifications, IdentityDbContext identity,
        Guid facilityId, Guid ownerId, string eventKey, string action, DateTime occurredOnUtc,
        bool notifyAdmins, CancellationToken ct)
    {
        foreach (var owner in await FindOwnerRecipientsAsync(identity, new UserId(ownerId), ct))
            await WriteRecipientAsync(notifications, owner.Id, owner.Email, owner.UiLanguage,
                facilityId, eventKey, action, occurredOnUtc, ct);

        if (!notifyAdmins) return;
        foreach (var admin in await FindOperationalAdminRecipientsAsync(identity, ct))
            await WriteRecipientAsync(notifications, admin.Id, admin.Email, admin.UiLanguage,
                facilityId, eventKey, action, occurredOnUtc, ct);
    }

    /// <summary>Recipient resolved for review notifications.</summary>
    public sealed record ReviewRecipient(Guid Id, string? Email, UiLanguage UiLanguage);

    /// <summary>Server-side recipient row with strongly-typed IDs.</summary>
    public sealed record ReviewRecipientRow(UserId Id, Email? Email, UiLanguage UiLanguage);

    /// <summary>
    /// Provider-safe recipient lookups. Strongly-typed IDs are compared and
    /// projected directly so the EF value-converter translates on PostgreSQL;
    /// the <c>.Value</c> unwrap happens client-side after materialization.
    /// Member access such as <c>x.Id.Value</c> inside the query predicate is
    /// not translatable and must not be reintroduced here.
    /// </summary>
    public static IQueryable<ReviewRecipientRow> OwnerRecipientRows(IdentityDbContext identity, UserId ownerId) =>
        identity.Users.AsNoTracking().Where(x => x.Id == ownerId)
            .Select(x => new ReviewRecipientRow(x.Id, x.Email, x.UiLanguage));

    public static IQueryable<ReviewRecipientRow> OperationalAdminRecipientRows(IdentityDbContext identity) =>
        identity.Users.AsNoTracking().Where(x => x.Status == UserStatus.Active &&
                x.Accounts.Any(a => a.AccountType == AccountType.SuperAdmin || a.AccountType == AccountType.SupportAdmin))
            .Select(x => new ReviewRecipientRow(x.Id, x.Email, x.UiLanguage));

    public static async Task<List<ReviewRecipient>> FindOwnerRecipientsAsync(IdentityDbContext identity, UserId ownerId, CancellationToken ct)
    {
        var rows = await OwnerRecipientRows(identity, ownerId).ToListAsync(ct);
        return rows.Select(x => new ReviewRecipient(x.Id.Value, x.Email == null ? null : x.Email.Value, x.UiLanguage)).ToList();
    }

    public static async Task<List<ReviewRecipient>> FindOperationalAdminRecipientsAsync(IdentityDbContext identity, CancellationToken ct)
    {
        var rows = await OperationalAdminRecipientRows(identity).ToListAsync(ct);
        return rows.Select(x => new ReviewRecipient(x.Id.Value, x.Email == null ? null : x.Email.Value, x.UiLanguage)).ToList();
    }

    /// <summary>
    /// F8 copy for one review action: a readable status sentence in both
    /// languages, with the facility kept on the structured destination instead
    /// of being repeated as a raw identifier inside the text.
    /// </summary>
    public static (string TitleAr, string TitleEn, string BodyAr, string BodyEn) Copy(string action)
    {
        (string TitleAr, string TitleEn) titles = action switch
        {
            "Submitted" => ("تم إرسال طلب دار الرعاية", "Care-home application submitted"),
            nameof(CareHomeReviewAction.CorrectionsRequested) => ("مطلوب استكمال بيانات دار الرعاية", "Care-home corrections requested"),
            nameof(CareHomeReviewAction.Approved) => ("تمت الموافقة على دار الرعاية", "Care-home application approved"),
            nameof(CareHomeReviewAction.Rejected) => ("تم رفض طلب دار الرعاية", "Care-home application rejected"),
            nameof(CareHomeReviewAction.Suspended) => ("تم إيقاف دار الرعاية", "Care home suspended"),
            nameof(CareHomeReviewAction.Reactivated) => ("تمت إعادة تفعيل دار الرعاية", "Care home reactivated"),
            _ => ("تم تحديث حالة دار الرعاية", "Care-home status updated")
        };
        return (titles.TitleAr, titles.TitleEn, $"الحالة: {titles.TitleAr}.", $"Status: {titles.TitleEn}.");
    }

    private static async Task WriteRecipientAsync(NotificationsDbContext db, Guid userId, string? email,
        UiLanguage language, Guid facilityId, string eventKey, string action, DateTime occurredOnUtc, CancellationToken ct)
    {
        var copy = Copy(action);
        bool arabic = language == UiLanguage.Arabic;
        string title = arabic ? copy.TitleAr : copy.TitleEn;
        string body = arabic ? copy.BodyAr : copy.BodyEn;
        string prefix = $"care-home-onboarding:{eventKey}:{userId:N}";
        await SaveNotificationAsync(db, userId, title, body, facilityId, prefix + ":in-app", occurredOnUtc, ct);
        if (string.IsNullOrWhiteSpace(email)) return;
        string key = prefix + ":email";
        if (await db.EmailOutboxMessages.AnyAsync(x => x.IdempotencyKey == key, ct)) return;
        var emailCopy = EmailTemplateCatalog.RenderBilingual(copy.TitleAr, copy.TitleEn,
            copy.TitleAr, copy.TitleEn, copy.BodyAr, copy.BodyEn);
        var message = EmailOutboxMessage.Create(email, emailCopy.Subject, emailCopy.PlainTextBody,
            emailCopy.HtmlBody, key, occurredOnUtc);
        db.EmailOutboxMessages.Add(message);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { db.Entry(message).State = EntityState.Detached; }
    }

    private static async Task SaveNotificationAsync(NotificationsDbContext db, Guid userId, string title,
        string body, Guid facilityId, string key, DateTime occurredOnUtc, CancellationToken ct)
    {
        if (await db.Notifications.AnyAsync(x => x.IdempotencyKey == key, ct)) return;
        var notification = Notification.Create(userId, "CareHomes", "OnboardingStatus", title, body,
            "CareHome", facilityId, occurredOnUtc, key);
        db.Notifications.Add(notification);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { db.Entry(notification).State = EntityState.Detached; }
    }
}
