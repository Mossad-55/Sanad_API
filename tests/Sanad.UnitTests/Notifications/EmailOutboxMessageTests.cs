using Sanad.Modules.Notifications.Domain.Notifications;

namespace Sanad.UnitTests.Notifications;

public sealed class EmailOutboxMessageTests
{
    [Fact]
    public void Create_RequiresRecipientKeyAndUtcTimestamp()
    {
        DateTime now = UtcNow();

        Assert.Throws<ArgumentException>(() => EmailOutboxMessage.Create("", "subject", "body", "key", now));
        Assert.Throws<ArgumentException>(() => EmailOutboxMessage.Create("admin@example.test", "subject", "body", "", now));
        Assert.Throws<ArgumentException>(() => EmailOutboxMessage.Create("admin@example.test", "subject", "body", "key", DateTime.SpecifyKind(now, DateTimeKind.Local)));
    }

    [Fact]
    public void Lifecycle_TracksProcessingSuccessAndClearsPreviousFailure()
    {
        DateTime created = UtcNow();
        EmailOutboxMessage message = EmailOutboxMessage.Create(" admin@example.test ", " subject ", " body ", " key ", created);

        message.MarkProcessing(created.AddMinutes(1));
        message.MarkFailed("temporary provider failure", created.AddMinutes(1));
        Assert.Equal(EmailOutboxStatus.Failed, message.Status);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal("temporary provider failure", message.LastError);
        Assert.Equal(created.AddMinutes(6), message.NextAttemptOnUtc);

        message.MarkProcessing(created.AddMinutes(7));
        message.MarkSent(created.AddMinutes(8));

        Assert.Equal(EmailOutboxStatus.Sent, message.Status);
        Assert.Equal(2, message.AttemptCount);
        Assert.Equal(created.AddMinutes(8), message.SentOnUtc);
        Assert.Null(message.LastError);
    }

    [Fact]
    public void FailureRetryDelay_IsCappedAndErrorIsBounded()
    {
        DateTime now = UtcNow();
        EmailOutboxMessage message = EmailOutboxMessage.Create("admin@example.test", "subject", "body", "key", now);
        string longError = new('x', 2_500);

        for (int attempt = 1; attempt <= 20; attempt++)
        {
            DateTime attempted = now.AddMinutes(attempt);
            message.MarkProcessing(attempted);
            message.MarkFailed(longError, attempted);
        }

        Assert.Equal(20, message.AttemptCount);
        Assert.Equal(2_000, message.LastError!.Length);
        Assert.Equal(now.AddMinutes(20 + 60), message.NextAttemptOnUtc);
    }

    [Fact]
    public void ClaimToken_PreventsWrongWorkerFromCompletingMessage()
    {
        DateTime now = UtcNow();
        EmailOutboxMessage message = EmailOutboxMessage.Create("admin@example.test", "subject", "body", "key", now);
        Guid ownerClaim = Guid.NewGuid();
        Guid otherClaim = Guid.NewGuid();

        message.MarkProcessing(now, ownerClaim);

        Assert.Throws<InvalidOperationException>(() => message.MarkSent(now.AddMinutes(1), otherClaim));
        Assert.Throws<InvalidOperationException>(() => message.MarkFailed("wrong worker", now.AddMinutes(1), otherClaim));

        message.MarkFailed("temporary failure", now.AddMinutes(1), ownerClaim);
        Assert.Equal(EmailOutboxStatus.Failed, message.Status);
        Assert.Null(message.ClaimToken);
    }

    private static DateTime UtcNow() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
}
