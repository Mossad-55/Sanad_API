using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Notifications.Domain.Notifications;

namespace Sanad.Modules.Notifications.Application.Abstractions.Data;

public interface INotificationsDbContext
{
    DbSet<Notification> Notifications { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
