using System.Text.Json;
using Sanad.BuildingBlocks.Domain.Abstractions;

namespace Sanad.Modules.Notifications.Domain.Notifications;

public enum PushOutboxStatus
{
    Pending = 1,
    Processing = 2,
    Sent = 3,
    Suppressed = 4,
    Failed = 5
}

public sealed class PushOutboxMessage : Entity<Guid>
{
    public const int MaximumDataLength = 4000;
    private PushOutboxMessage() { }

    private PushOutboxMessage(Guid id, Guid recipientUserId, string title, string body, string dataJson,
        string idempotencyKey, string preferenceKey, DateTime nowUtc) : base(id)
    {
        RecipientUserId = recipientUserId;
        Title = title;
        Body = body;
        DataJson = dataJson;
        IdempotencyKey = idempotencyKey;
        PreferenceKey = preferenceKey;
        CreatedOnUtc = nowUtc;
        NextAttemptOnUtc = nowUtc;
        Status = PushOutboxStatus.Pending;
    }

    public Guid RecipientUserId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public string DataJson { get; private set; } = "{}";
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string PreferenceKey { get; private set; } = string.Empty;
    public PushOutboxStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime NextAttemptOnUtc { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? SentOnUtc { get; private set; }
    public DateTime? LastAttemptOnUtc { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? FcmMessageId { get; private set; }
    public Guid? ClaimToken { get; private set; }

    public static PushOutboxMessage Create(Guid recipientUserId, string title, string body,
        IReadOnlyDictionary<string, string> data, string idempotencyKey, string preferenceKey, DateTime nowUtc)
    {
        if (recipientUserId == Guid.Empty || string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("Push recipient and idempotency key are required.");
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Outbox time must be UTC.");
        string serializedData = JsonSerializer.Serialize(data);
        if (serializedData.Length > MaximumDataLength) throw new ArgumentException("Push data is too long.", nameof(data));
        return new PushOutboxMessage(Guid.CreateVersion7(), recipientUserId, title, body, serializedData, idempotencyKey.Trim(), preferenceKey.Trim(), nowUtc);
    }

    public IReadOnlyDictionary<string, string> ReadData() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(DataJson) ?? new Dictionary<string, string>();

    public void MarkProcessing(DateTime nowUtc, Guid claimToken)
    {
        if (claimToken == Guid.Empty) throw new ArgumentException("Outbox claim token is required.");
        Status = PushOutboxStatus.Processing;
        AttemptCount++;
        LastAttemptOnUtc = nowUtc;
        ClaimToken = claimToken;
    }

    public void MarkSent(DateTime nowUtc, Guid claimToken, string? fcmMessageId)
    {
        EnsureClaim(claimToken);
        Status = PushOutboxStatus.Sent;
        SentOnUtc = nowUtc;
        FcmMessageId = fcmMessageId;
        LastErrorCode = null;
        ClaimToken = null;
    }

    public void MarkSuppressed(Guid claimToken)
    {
        EnsureClaim(claimToken);
        Status = PushOutboxStatus.Suppressed;
        LastErrorCode = null;
        ClaimToken = null;
    }

    public void MarkFailed(string errorCode, DateTime nowUtc, Guid claimToken, bool retryable)
    {
        EnsureClaim(claimToken);
        LastErrorCode = string.IsNullOrWhiteSpace(errorCode) ? "Push.SendFailed" : errorCode[..Math.Min(errorCode.Length, 80)];
        ClaimToken = null;
        if (!retryable || AttemptCount >= 8)
        {
            Status = PushOutboxStatus.Failed;
            return;
        }
        Status = PushOutboxStatus.Pending;
        NextAttemptOnUtc = nowUtc.AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(AttemptCount - 1, 6))));
    }

    private void EnsureClaim(Guid claimToken)
    {
        if (claimToken == Guid.Empty || ClaimToken != claimToken)
            throw new InvalidOperationException("The outbox message is claimed by another worker.");
    }
}
