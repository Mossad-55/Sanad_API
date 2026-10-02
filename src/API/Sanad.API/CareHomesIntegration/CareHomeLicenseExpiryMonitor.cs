using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Notifications.Domain.Notifications;
using Sanad.Modules.Notifications.Infrastructure.Persistence;

namespace Sanad.API.CareHomesIntegration;

public sealed class CareHomeLicenseExpiryMonitor(
    IServiceScopeFactory scopeFactory,
    ILogger<CareHomeLicenseExpiryMonitor> logger) : BackgroundService
{
    private static readonly SemaphoreSlim RunGate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            DateTime now = DateTime.UtcNow;
            DateTime nextRun = now.Date.AddDays(1);
            await Task.Delay(nextRun - now, stoppingToken);
            if (stoppingToken.IsCancellationRequested) break;
            await RunOnceAsync(DateOnly.FromDateTime(DateTime.UtcNow), stoppingToken);
        }
    }

    private async Task RunOnceAsync(DateOnly currentDate, CancellationToken ct)
    {
        if (!await RunGate.WaitAsync(0, ct)) return;
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            var careHomes = scope.ServiceProvider.GetRequiredService<CareHomesDbContext>();
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var notifications = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

            var expired = await careHomes.Facilities.AsNoTracking()
                .Where(x => x.Status == CareHomeStatus.Approved && x.ApprovedRevisionId != null)
                .Include(x => x.Documents)
                .Select(x => new
                {
                    FacilityId = x.Id.Value,
                    RevisionId = x.ApprovedRevisionId!.Value,
                    License = x.Documents.OrderByDescending(d => d.CreatedOnUtc)
                        .FirstOrDefault(d => d.ProfileRevisionId == x.ApprovedRevisionId.Value && d.Type == CareHomeDocumentType.OperatingLicense)
                })
                .Where(x => x.License != null && x.License.Status == CareHomeDocumentStatus.Verified &&
                            !x.License.VerifiedNonExpiring && x.License.ExpiryDate < currentDate)
                .ToListAsync(ct);

            if (expired.Count == 0) return;

            var admins = await identity.Users.AsNoTracking()
                .Where(x => x.Status == UserStatus.Active &&
                    x.Accounts.Any(a => a.AccountType == AccountType.SuperAdmin || a.AccountType == AccountType.SupportAdmin))
                .Select(x => new { UserId = x.Id.Value, Email = x.Email == null ? null : x.Email.Value })
                .ToListAsync(ct);

            DateTime createdOnUtc = DateTime.UtcNow;
            foreach (var facility in expired)
            foreach (var admin in admins)
            {
                string baseKey = $"care-home-license-expiry:{currentDate:yyyy-MM-dd}:{facility.FacilityId:N}:{admin.UserId:N}";
                string body = $"Care home {facility.FacilityId} has an expired operating license and requires review.";
                string inAppKey = baseKey + ":in-app";
                if (!await notifications.Notifications.AnyAsync(x => x.IdempotencyKey == inAppKey, ct))
                {
                    var notification = Notification.Create(admin.UserId, "CareHomes", "LicenseExpired",
                        "Care-home license expired", body, "CareHome", facility.FacilityId, createdOnUtc, inAppKey);
                    notifications.Notifications.Add(notification);
                    try { await notifications.SaveChangesAsync(ct); }
                    catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                    { notifications.Entry(notification).State = EntityState.Detached; }
                }

                if (!string.IsNullOrWhiteSpace(admin.Email))
                {
                    string emailKey = baseKey + ":email";
                    if (!await notifications.EmailOutboxMessages.AnyAsync(x => x.IdempotencyKey == emailKey, ct))
                    {
                        var email = EmailOutboxMessage.Create(admin.Email, "Care-home operating license expired", body, emailKey, createdOnUtc);
                        notifications.EmailOutboxMessages.Add(email);
                        try { await notifications.SaveChangesAsync(ct); }
                        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                        { notifications.Entry(email).State = EntityState.Detached; }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Care-home license expiry scan failed.");
        }
        finally { RunGate.Release(); }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

public sealed class EmailOutboxProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<EmailOutboxProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessBatchAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Email outbox processing failed."); }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        DateTime now = DateTime.UtcNow;
        var rows = await db.EmailOutboxMessages
            .AsNoTracking()
            .Where(x => (x.Status == EmailOutboxStatus.Pending || x.Status == EmailOutboxStatus.Failed) && x.NextAttemptOnUtc <= now ||
                        x.Status == EmailOutboxStatus.Processing && x.LastAttemptOnUtc < now.AddMinutes(-15))
            .OrderBy(x => x.CreatedOnUtc).Take(20).ToListAsync(ct);
        foreach (var row in rows)
        {
            var claimedRow = row;
            Guid claimToken = Guid.NewGuid();
            int claimed = await db.EmailOutboxMessages
                .Where(x => x.Id == row.Id &&
                    ((x.Status == EmailOutboxStatus.Pending || x.Status == EmailOutboxStatus.Failed) && x.NextAttemptOnUtc <= now ||
                     x.Status == EmailOutboxStatus.Processing && x.LastAttemptOnUtc < now.AddMinutes(-15)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, EmailOutboxStatus.Processing)
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                    .SetProperty(x => x.LastAttemptOnUtc, now)
                    .SetProperty(x => x.ClaimToken, claimToken), ct);
            if (claimed != 1) continue;
            claimedRow = await db.EmailOutboxMessages.SingleAsync(x => x.Id == row.Id, ct);
            try
            {
                await sender.SendEmailAsync(claimedRow.RecipientEmail, claimedRow.Subject, claimedRow.Body, claimedRow.IdempotencyKey, ct);
                claimedRow.MarkSent(DateTime.UtcNow, claimToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                claimedRow.MarkFailed(ex.Message, DateTime.UtcNow, claimToken);
                logger.LogWarning(ex, "Email outbox message {MessageId} failed; retry scheduled.", claimedRow.Id);
            }
            await db.SaveChangesAsync(ct);
        }
    }
}
