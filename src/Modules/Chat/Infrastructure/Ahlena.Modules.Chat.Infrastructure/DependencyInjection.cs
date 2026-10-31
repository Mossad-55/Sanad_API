using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Ahlena.Modules.Chat.Application.Abstractions.Data;
using Ahlena.Modules.Chat.Application.Attachments;
using Ahlena.Modules.Chat.Application.Events;
using Ahlena.Modules.Chat.Application.RateLimiting;
using Ahlena.Modules.Chat.Infrastructure.Attachments;
using Ahlena.Modules.Chat.Infrastructure.Persistence;
using Ahlena.Modules.Chat.Infrastructure.Storage;

namespace Ahlena.Modules.Chat.Infrastructure;

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

        services.AddOptions<ChatRateOptions>()
            .Bind(configuration.GetSection("ChatPlus:RateLimits"))
            .Validate(
                options => options.UnboundAttachmentLifetime > TimeSpan.Zero,
                "ChatPlus:RateLimits:UnboundAttachmentLifetime must be positive.")
            .Validate(
                options => options.UnboundQuotaBytesPerUser > 0,
                "ChatPlus:RateLimits:UnboundQuotaBytesPerUser must be positive.")
            .ValidateOnStart();

        services.AddSingleton<IChatRateGate>(serviceProvider =>
            new InMemoryChatRateGate(
                serviceProvider
                    .GetRequiredService<IOptions<ChatRateOptions>>()
                    .Value));

        services.AddScoped<IChatEventRecorder, ChatEventRecorder>();

        // Orphan sweep: private-storage cleanup port, bounded sweep service,
        // and sanitized metrics. The sweep clock is pinned to UtcNow (same
        // Func<DateTime> seam as the dispatcher).
        services.AddScoped<IChatAttachmentCleanupStorage,
            LocalDiskChatAttachmentCleanupStorage>();
        services.AddScoped<ChatAttachmentCleanupService>();
        services.AddScoped<IChatAttachmentCleanupMetrics,
            ChatAttachmentCleanupMetrics>();
        services.AddOptions<ChatAttachmentCleanupOptions>()
            .Bind(configuration.GetSection(ChatAttachmentCleanupOptions.SectionName))
            .Validate(
                options => options.SweepInterval > TimeSpan.Zero,
                "ChatPlus:AttachmentCleanup:SweepInterval must be positive.")
            .Validate(
                options => options.BatchSize is >= 1 and <= 100,
                "ChatPlus:AttachmentCleanup:BatchSize must be between 1 and 100.")
            .ValidateOnStart();

        // Required by the cleanup service's pinned-clock seam. Register the
        // production clock explicitly; Microsoft DI does not synthesize
        // Func<T> registrations for required constructor parameters.
        services.AddSingleton<Func<DateTime>>(() => DateTime.UtcNow);

        return services;
    }
}
