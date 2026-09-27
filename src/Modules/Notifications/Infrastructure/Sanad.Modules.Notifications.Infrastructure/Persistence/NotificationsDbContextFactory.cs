using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace Sanad.Modules.Notifications.Infrastructure.Persistence;
public sealed class NotificationsDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__NotificationsDatabase")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__IdentityDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings__NotificationsDatabase or ConnectionStrings__IdentityDatabase is required.");
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", NotificationsDbContext.Schema)).Options;
        return new NotificationsDbContext(options);
    }
}
