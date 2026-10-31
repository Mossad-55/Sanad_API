using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ahlena.Modules.Chat.Application.Attachments;
using Ahlena.Modules.Chat.Application.Abstractions.Data;
using Ahlena.Modules.Chat.Infrastructure.Attachments;

namespace Ahlena.API.ChatPlus;

/// <summary>
/// Hourly bounded orphan sweep for Chat unbound attachments. Each tick runs
/// one <see cref="ChatAttachmentCleanupService.SweepAsync"/> pass in a fresh
/// scope (at most <c>ChatPlus:AttachmentCleanup:BatchSize</c> objects), then
/// waits <c>ChatPlus:AttachmentCleanup:SweepInterval</c>. Crash-safe: the
/// sweep's claim/row coordination recovers any blob/row mismatch on the next
/// pass, so a restart mid-sweep loses nothing. Single-instance only (matches
/// the Chat Plus topology guard).
/// </summary>
public sealed class ChatAttachmentCleanupHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<ChatAttachmentCleanupOptions> cleanupOptions,
    ILogger<ChatAttachmentCleanupHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    "Chat attachment orphan sweep failed with {ErrorType}.",
                    ex.GetType().Name);
            }

            try
            {
                await Task.Delay(cleanupOptions.Value.SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        var cleanup = scope.ServiceProvider
            .GetRequiredService<ChatAttachmentCleanupService>();
        var metrics = scope.ServiceProvider
            .GetRequiredService<IChatAttachmentCleanupMetrics>();

        ChatAttachmentCleanupBacklog backlog =
            await metrics.GetBacklogAsync(cancellationToken);

        if (backlog.ExpiredUnboundCount > 0)
        {
            logger.LogInformation(
                "Chat attachment orphan sweep starting with a backlog of {Count} objects ({Bytes} bytes).",
                backlog.ExpiredUnboundCount,
                backlog.ExpiredUnboundBytes);
        }

        ChatAttachmentCleanupSweepResult result =
            await cleanup.SweepAsync(cancellationToken);

        if (result.DeletedCount == 0 && result.FailureCount == 0)
        {
            return;
        }

        ChatAttachmentCleanupBacklog remaining =
            await metrics.GetBacklogAsync(cancellationToken);

        logger.LogInformation(
            "Chat attachment orphan sweep finished: deleted {DeletedCount} objects ({DeletedBytes} bytes), {FailureCount} failures, backlog now {RemainingCount} objects ({RemainingBytes} bytes).",
            result.DeletedCount,
            result.DeletedBytes,
            result.FailureCount,
            remaining.ExpiredUnboundCount,
            remaining.ExpiredUnboundBytes);
    }
}
