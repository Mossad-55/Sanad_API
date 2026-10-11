using Sanad.Modules.Notifications.Application.Abstractions.Push;

namespace Sanad.Modules.Notifications.Infrastructure.Push;

public sealed class DevelopmentNoOpPushSender : IPushSender
{
    public bool IsConfigured => true;

    public Task<PushSendResult> SendAsync(string token, string title, string body,
        IReadOnlyDictionary<string, string> data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PushSendResult(PushSendOutcome.Sent, "development-noop"));
    }
}

public sealed class UnconfiguredPushSender : IPushSender
{
    public bool IsConfigured => false;

    public Task<PushSendResult> SendAsync(string token, string title, string body,
        IReadOnlyDictionary<string, string> data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PushSendResult(PushSendOutcome.NotConfigured, ErrorCode: "Push.NotConfigured"));
    }
}
