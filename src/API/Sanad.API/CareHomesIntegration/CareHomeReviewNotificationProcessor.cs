using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;
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
        var owners = await identity.Users.AsNoTracking().Where(x => x.Id.Value == ownerId)
            .Select(x => new { Id = x.Id.Value, Email = x.Email == null ? null : x.Email.Value, x.UiLanguage }).ToListAsync(ct);
        foreach (var owner in owners)
            await WriteRecipientAsync(notifications, owner.Id, owner.Email, owner.UiLanguage,
                facilityId, eventKey, action, occurredOnUtc, ct);

        if (!notifyAdmins) return;
        var admins = await identity.Users.AsNoTracking().Where(x => x.Status == UserStatus.Active &&
                x.Accounts.Any(a => a.AccountType == AccountType.SuperAdmin || a.AccountType == AccountType.SupportAdmin))
            .Select(x => new { Id = x.Id.Value, Email = x.Email == null ? null : x.Email.Value, x.UiLanguage }).ToListAsync(ct);
        foreach (var admin in admins)
            await WriteRecipientAsync(notifications, admin.Id, admin.Email, admin.UiLanguage,
                facilityId, eventKey, action, occurredOnUtc, ct);
    }

    private static async Task WriteRecipientAsync(NotificationsDbContext db, Guid userId, string? email,
        UiLanguage language, Guid facilityId, string eventKey, string action, DateTime occurredOnUtc, CancellationToken ct)
    {
        bool arabic = language == UiLanguage.Arabic;
        string title = action switch
        {
            "Submitted" => arabic ? "تم إرسال طلب دار الرعاية" : "Care-home application submitted",
            nameof(CareHomeReviewAction.CorrectionsRequested) => arabic ? "مطلوب استكمال بيانات دار الرعاية" : "Care-home corrections requested",
            nameof(CareHomeReviewAction.Approved) => arabic ? "تمت الموافقة على دار الرعاية" : "Care-home application approved",
            nameof(CareHomeReviewAction.Rejected) => arabic ? "تم رفض طلب دار الرعاية" : "Care-home application rejected",
            nameof(CareHomeReviewAction.Suspended) => arabic ? "تم إيقاف دار الرعاية" : "Care home suspended",
            nameof(CareHomeReviewAction.Reactivated) => arabic ? "تمت إعادة تفعيل دار الرعاية" : "Care home reactivated",
            _ => arabic ? "تم تحديث حالة دار الرعاية" : "Care-home status updated"
        };
        string body = arabic ? $"المنشأة: {facilityId}. الحالة: {title}." : $"Facility: {facilityId}. Status: {title}.";
        string prefix = $"care-home-onboarding:{eventKey}:{userId:N}";
        await SaveNotificationAsync(db, userId, title, body, facilityId, prefix + ":in-app", occurredOnUtc, ct);
        if (string.IsNullOrWhiteSpace(email)) return;
        string key = prefix + ":email";
        if (await db.EmailOutboxMessages.AnyAsync(x => x.IdempotencyKey == key, ct)) return;
        var message = EmailOutboxMessage.Create(email, title, body, key, occurredOnUtc);
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
