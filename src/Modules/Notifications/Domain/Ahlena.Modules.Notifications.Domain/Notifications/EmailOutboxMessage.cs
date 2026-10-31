using Ahlena.BuildingBlocks.Domain.Abstractions;

namespace Ahlena.Modules.Notifications.Domain.Notifications;

public enum EmailOutboxStatus
{
    Pending = 1,
    Processing = 2,
    Sent = 3,
    Failed = 4
}

public sealed class EmailOutboxMessage : Entity<Guid>
{
    private EmailOutboxMessage() { }

    private EmailOutboxMessage(Guid id, string recipientEmail, string subject, string body, string? htmlBody,
        string idempotencyKey, DateTime createdOnUtc) : base(id)
    {
        RecipientEmail = recipientEmail;
        Subject = subject;
        Body = body;
        HtmlBody = htmlBody;
        IdempotencyKey = idempotencyKey;
        CreatedOnUtc = createdOnUtc;
        NextAttemptOnUtc = createdOnUtc;
        Status = EmailOutboxStatus.Pending;
    }

    public string RecipientEmail { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public string? HtmlBody { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public EmailOutboxStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime NextAttemptOnUtc { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? SentOnUtc { get; private set; }
    public DateTime? LastAttemptOnUtc { get; private set; }
    public string? LastError { get; private set; }
    public Guid? ClaimToken { get; private set; }

    public static EmailOutboxMessage Create(string recipientEmail, string subject, string body,
        string idempotencyKey, DateTime createdOnUtc)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail) || string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("Email recipient and idempotency key are required.");
        if (createdOnUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Outbox time must be UTC.");
        return new EmailOutboxMessage(Guid.CreateVersion7(), recipientEmail.Trim(), subject.Trim(), body.Trim(), null, idempotencyKey.Trim(), createdOnUtc);
    }

    public static EmailOutboxMessage Create(string recipientEmail, string subject, string plainTextBody,
        string htmlBody, string idempotencyKey, DateTime createdOnUtc)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail) || string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("Email recipient and idempotency key are required.");
        if (string.IsNullOrWhiteSpace(plainTextBody) || string.IsNullOrWhiteSpace(htmlBody))
            throw new ArgumentException("Both plain-text and HTML email bodies are required.");
        if (createdOnUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Outbox time must be UTC.");
        return new EmailOutboxMessage(Guid.CreateVersion7(), recipientEmail.Trim(), subject.Trim(), plainTextBody.Trim(), htmlBody.Trim(), idempotencyKey.Trim(), createdOnUtc);
    }

    public void MarkProcessing(DateTime utcNow, Guid claimToken)
    {
        if (claimToken == Guid.Empty) throw new ArgumentException("Outbox claim token is required.");
        Status = EmailOutboxStatus.Processing;
        AttemptCount++;
        LastAttemptOnUtc = utcNow;
        ClaimToken = claimToken;
    }

    // Compatibility seam for in-process callers; distributed workers use the token overload.
    public void MarkProcessing(DateTime utcNow) => MarkProcessing(utcNow, Guid.NewGuid());

    public void MarkSent(DateTime utcNow, Guid claimToken)
    {
        EnsureClaim(claimToken);
        Status = EmailOutboxStatus.Sent;
        SentOnUtc = utcNow;
        LastError = null;
        ClaimToken = null;
    }

    public void MarkSent(DateTime utcNow) => MarkSent(utcNow, ClaimToken ?? throw new InvalidOperationException("The outbox message is not claimed."));

    public void MarkFailed(string error, DateTime utcNow, Guid claimToken)
    {
        EnsureClaim(claimToken);
        Status = EmailOutboxStatus.Failed;
        LastError = error.Length > 2000 ? error[..2000] : error;
        NextAttemptOnUtc = utcNow.AddMinutes(Math.Min(60, Math.Max(1, AttemptCount * 5)));
        ClaimToken = null;
    }

    public void MarkFailed(string error, DateTime utcNow) => MarkFailed(error, utcNow, ClaimToken ?? throw new InvalidOperationException("The outbox message is not claimed."));

    private void EnsureClaim(Guid claimToken)
    {
        if (claimToken == Guid.Empty || ClaimToken != claimToken)
            throw new InvalidOperationException("The outbox message is claimed by another worker.");
    }
}
