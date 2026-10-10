using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Notifications.Application.Abstractions.Data;
using Sanad.Modules.Notifications.Application.Notifications;
using Sanad.Modules.Notifications.Domain.Notifications;
namespace Sanad.Modules.Notifications.Infrastructure.Persistence;
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options), INotificationsDbContext
{
    public const string Schema = "notifications";
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<EmailOutboxMessage> EmailOutboxMessages => Set<EmailOutboxMessage>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<PushOutboxMessage> PushOutboxMessages => Set<PushOutboxMessage>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await EnqueuePushForNewNotificationsAsync(cancellationToken);
        return await base.SaveChangesAsync(cancellationToken);
    }

    private async Task EnqueuePushForNewNotificationsAsync(CancellationToken cancellationToken)
    {
        var eligible = ChangeTracker.Entries<Notification>()
            .Where(x => x.State == EntityState.Added && PushFanoutPolicy.TryGetPreferenceKey(x.Entity, out _))
            .Select(x => x.Entity)
            .ToArray();
        if (eligible.Length == 0) return;

        var recipients = eligible.Select(x => x.RecipientUserId).Distinct().ToArray();
        HashSet<Guid> ownersWithDevices = await DeviceTokens.AsNoTracking()
            .Where(x => recipients.Contains(x.UserId))
            .Select(x => x.UserId)
            .Distinct()
            .ToHashSetAsync(cancellationToken);
        ownersWithDevices.UnionWith(ChangeTracker.Entries<DeviceToken>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity.UserId));

        foreach (Notification notification in eligible)
        {
            if (!ownersWithDevices.Contains(notification.RecipientUserId)) continue;
            _ = PushFanoutPolicy.TryGetPreferenceKey(notification, out string preferenceKey);
            string key = $"push:{notification.IdempotencyKey ?? notification.Id.ToString("N")}";
            PushOutboxMessages.Add(PushOutboxMessage.Create(notification.RecipientUserId, notification.Title,
                notification.Body, PushFanoutPolicy.BuildData(notification), key, preferenceKey, notification.CreatedOnUtc));
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
