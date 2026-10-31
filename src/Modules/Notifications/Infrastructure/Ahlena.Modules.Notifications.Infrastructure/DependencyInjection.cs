using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ahlena.Modules.Notifications.Application.Abstractions.Data;
using Ahlena.Modules.Notifications.Infrastructure.Persistence;

namespace Ahlena.Modules.Notifications.Infrastructure;
public static class DependencyInjection
{
    public static IServiceCollection AddNotificationsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var cs = configuration.GetConnectionString("NotificationsDatabase") ?? configuration.GetConnectionString("IdentityDatabase");
        if (string.IsNullOrWhiteSpace(cs)) throw new InvalidOperationException("ConnectionStrings:NotificationsDatabase or ConnectionStrings:IdentityDatabase is required.");
        services.AddDbContext<NotificationsDbContext>(o => o.UseNpgsql(cs, n => n.MigrationsHistoryTable("__EFMigrationsHistory", NotificationsDbContext.Schema)));
        services.AddScoped<INotificationsDbContext>(sp => sp.GetRequiredService<NotificationsDbContext>());
        return services;
    }
}
