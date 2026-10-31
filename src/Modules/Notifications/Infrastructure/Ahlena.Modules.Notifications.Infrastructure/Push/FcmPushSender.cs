using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

using Ahlena.Modules.Notifications.Application.Abstractions.Push;

namespace Ahlena.Modules.Notifications.Infrastructure.Push;

public sealed class PushFirebaseOptions
{
    public const string SectionName = "Push:Firebase";
    public string? ServiceAccountJson { get; set; }
}


public sealed class FcmPushSender : IPushSender, IDisposable
{
    private readonly FirebaseApp? _app;
    private readonly FirebaseMessaging? _messaging;

    public FcmPushSender(IOptions<PushFirebaseOptions> options)
    {
        string? json = options.Value.ServiceAccountJson;
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            GoogleCredential credential = CredentialFactory.FromJson<ServiceAccountCredential>(json).ToGoogleCredential();
            _app = FirebaseApp.Create(new AppOptions { Credential = credential }, $"ahlena-push-{Guid.NewGuid():N}");
            _messaging = FirebaseMessaging.GetMessaging(_app);
        }
        catch (Exception)
        {
            _app?.Delete();
            _app = null;
            _messaging = null;
        }
    }

    public bool IsConfigured => _messaging is not null;

    public async Task<PushSendResult> SendAsync(string token, string title, string body,
        IReadOnlyDictionary<string, string> data, CancellationToken cancellationToken)
    {
        if (_messaging is null) return new PushSendResult(PushSendOutcome.NotConfigured, ErrorCode: "Push.NotConfigured");
        try
        {
            string id = await _messaging.SendAsync(new Message
            {
#pragma warning disable CS0618 // The plan requires registration tokens; Firebase Admin currently deprecates Token in favor of FID.
                Token = token,
#pragma warning restore CS0618
                Notification = new FirebaseAdmin.Messaging.Notification { Title = title, Body = body },
                Data = new Dictionary<string, string>(data)
            }, cancellationToken);
            return new PushSendResult(PushSendOutcome.Sent, id);
        }
        catch (FirebaseMessagingException ex) when (ex.MessagingErrorCode is MessagingErrorCode.Unregistered)
        {
            // Proven token-specific failure: the token is dead, retire it.
            return new PushSendResult(PushSendOutcome.InvalidToken, ErrorCode: ex.MessagingErrorCode?.ToString() ?? "Push.InvalidToken");
        }
        catch (FirebaseMessagingException ex) when (ex.MessagingErrorCode is MessagingErrorCode.InvalidArgument)
        {
            // InvalidArgument is NOT proven token-specific (it also covers
            // rejected payloads): never retire tokens for it. Retry bounded
            // with a payload-classified code; the outbox parks the item
            // after 8 attempts for operator inspection instead.
            return new PushSendResult(PushSendOutcome.RetryableFailure, ErrorCode: "Push.PayloadRejected");
        }
        catch (FirebaseMessagingException ex) when (ex.MessagingErrorCode is MessagingErrorCode.SenderIdMismatch or MessagingErrorCode.ThirdPartyAuthError)
        {
            // Configuration failure (wrong Firebase project/credentials):
            // retryable so a corrected secret recovers without row surgery,
            // surfaced to operators via the outbox error code.
            return new PushSendResult(PushSendOutcome.RetryableFailure, ErrorCode: ex.MessagingErrorCode?.ToString() ?? "Push.ConfigurationFailure");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new PushSendResult(PushSendOutcome.RetryableFailure, ErrorCode: ex is FirebaseMessagingException ? "Push.ProviderFailure" : "Push.SendFailed");
        }
    }

    public void Dispose() => _app?.Delete();
}
