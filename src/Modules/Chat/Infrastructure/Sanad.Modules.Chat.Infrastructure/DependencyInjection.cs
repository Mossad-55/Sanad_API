using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Infrastructure.Persistence;

namespace Sanad.Modules.Chat.Infrastructure;

public static class ChatInfrastructure
{
    public static IServiceCollection AddChatInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("ChatDatabase")
            ?? configuration.GetConnectionString("IdentityDatabase");

        if (string.IsNullOrWhiteSpace(
            connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:ChatDatabase or " +
                "ConnectionStrings:IdentityDatabase is required.");
        }

        services.AddDbContext<ChatDbContext>(
            options =>
                options.UseNpgsql(
                    connectionString,
                    npgsqlOptions =>
                        npgsqlOptions.MigrationsHistoryTable(
                            "__EFMigrationsHistory",
                            ChatDbContext.Schema)));

        services.AddScoped<IChatDbContext>(
            serviceProvider =>
                serviceProvider.GetRequiredService<
                    ChatDbContext>());

        return services;
    }
}
