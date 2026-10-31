namespace Ahlena.Modules.Notifications.Application.Abstractions.Push;

public enum PushSendOutcome
{
    Sent,
    InvalidToken,
    RetryableFailure,
    NotConfigured
}

public sealed record PushSendResult(PushSendOutcome Outcome, string? MessageId = null, string ErrorCode = "Push.SendFailed");

public interface IPushSender
{
    bool IsConfigured { get; }
    Task<PushSendResult> SendAsync(string token, string title, string body,
        IReadOnlyDictionary<string, string> data, CancellationToken cancellationToken);
}
