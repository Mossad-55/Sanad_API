using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Notifications.Application.Abstractions.Data;
using Sanad.Modules.Notifications.Domain.Notifications;
namespace Sanad.Modules.Notifications.Infrastructure.Persistence;
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options), INotificationsDbContext
{
    public const string Schema = "notifications";
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<EmailOutboxMessage> EmailOutboxMessages => Set<EmailOutboxMessage>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) { modelBuilder.HasDefaultSchema(Schema); modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsDbContext).Assembly); base.OnModelCreating(modelBuilder); }
}
