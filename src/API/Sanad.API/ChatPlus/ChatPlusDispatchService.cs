using MediatR;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Application.Dispatch;
using Sanad.Modules.Chat.Application.Realtime;

namespace Sanad.API.ChatPlus;

/// <summary>
/// Chat Plus after-commit dispatch loop (Phase 3): every 5s, in a fresh
/// scope, publishes durable change rows to realtime and delivers
/// notification intent to the Notifications seam. Crash-safe: writers stage
/// rows in the Chat transaction, so anything pending here is redelivered
/// after a restart with the dispatcher's idempotent claim/retry/backoff
/// semantics. Single-instance only (matches the Chat Plus topology guard).
/// </summary>
public sealed class ChatPlusDispatchService(
    IServiceScopeFactory scopeFactory,
    ILogger<ChatPlusDispatchService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    "Chat dispatch cycle failed with {ErrorType}.",
                    ex.GetType().Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task DispatchOnceAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<IChatDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var publisher = scope.ServiceProvider.GetService<IChatRealtimePublisher>();

        var dispatcher = new ChatOutboxDispatcher(db, sender, publisher);
        (int changes, int intents) = await dispatcher.DispatchAsync(cancellationToken);

        if (changes > 0 || intents > 0)
        {
            logger.LogInformation(
                "Chat dispatch published {Changes} changes and {Intents} intents.",
                changes,
                intents);
        }
    }
}
