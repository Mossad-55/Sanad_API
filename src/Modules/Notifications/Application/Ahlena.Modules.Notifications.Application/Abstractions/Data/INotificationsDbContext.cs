using Microsoft.EntityFrameworkCore;
using Ahlena.Modules.Notifications.Domain.Notifications;

namespace Ahlena.Modules.Notifications.Application.Abstractions.Data;

public interface INotificationsDbContext
{
    DbSet<Notification> Notifications { get; }
    DbSet<EmailOutboxMessage> EmailOutboxMessages { get; }
    DbSet<DeviceToken> DeviceTokens { get; }
    DbSet<PushOutboxMessage> PushOutboxMessages { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
