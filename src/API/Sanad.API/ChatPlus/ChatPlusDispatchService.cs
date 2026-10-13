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

        // Typing-expiry sweep: server-expired hints broadcast stops while
        // the pair is still eligible (the publisher re-resolves per
        // dispatch; ineligible pairs simply receive nothing).
        int stops = 0;

        if (publisher is not null)
        {
            var typing = scope.ServiceProvider
                .GetRequiredService<
                    Sanad.Modules.Chat.Application.Realtime.ITypingTracker>();
            var audience = scope.ServiceProvider
                .GetRequiredService<
                    Sanad.Modules.Chat.Application.Realtime.IChatHubAudience>();

            foreach (var expired in typing.SweepExpired(DateTime.UtcNow))
            {
                IReadOnlyList<Guid> recipients = (await audience.GetEventRecipientsAsync(
                        expired.ConversationId, expired.UserId, cancellationToken))
                    .Select(user => user.Value)
                    .ToArray();

                if (recipients.Count == 0)
                {
                    continue;
                }

                await publisher.PublishTypingChangedAsync(
                    expired.ConversationId.Value,
                    expired.UserId.Value,
                    false,
                    DateTime.UtcNow,
                    cancellationToken);
                stops++;
            }
        }

        if (changes > 0 || intents > 0 || stops > 0)
        {
            logger.LogInformation(
                "Chat dispatch published {Changes} changes, {Intents} intents and {Stops} typing stops.",
                changes,
                intents,
                stops);
        }
    }
}
