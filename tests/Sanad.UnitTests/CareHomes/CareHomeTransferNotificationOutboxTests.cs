using Sanad.Modules.CareHomes.Domain.Bookings;

namespace Sanad.UnitTests.CareHomes;

public sealed class CareHomeTransferNotificationOutboxTests
{
    [Fact]
    public void Failed_dispatch_retries_then_completes_under_the_current_claim()
    {
        var created = new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc);
        var outbox = CareHomeTransferNotificationOutbox.Create(Guid.NewGuid(), Guid.NewGuid(), created);
        var firstClaim = Guid.NewGuid();

        outbox.MarkProcessing(created, firstClaim);
        outbox.MarkFailed("Temporary notification database outage", created, firstClaim);

        Assert.Equal(CareHomeTransferNotificationStatus.Failed, outbox.Status);
        Assert.Equal(1, outbox.AttemptCount);
        Assert.Equal(created.AddMinutes(5), outbox.NextAttemptOnUtc);

        var retryClaim = Guid.NewGuid();
        outbox.MarkProcessing(outbox.NextAttemptOnUtc, retryClaim);
        outbox.MarkCompleted();

        Assert.Equal(CareHomeTransferNotificationStatus.Completed, outbox.Status);
        Assert.Equal(2, outbox.AttemptCount);
        Assert.Null(outbox.ClaimToken);
        Assert.Null(outbox.LastError);
    }

    [Fact]
    public void A_superseded_claim_cannot_mark_a_reclaimed_notification_failed()
    {
        var created = new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc);
        var outbox = CareHomeTransferNotificationOutbox.Create(Guid.NewGuid(), Guid.NewGuid(), created);
        var originalClaim = Guid.NewGuid();
        outbox.MarkProcessing(created, originalClaim);

        var retryClaim = Guid.NewGuid();
        outbox.MarkProcessing(created.AddMinutes(16), retryClaim);

        Assert.Throws<InvalidOperationException>(() => outbox.MarkFailed("Late failure from stale worker", created.AddMinutes(17), originalClaim));
        Assert.Equal(CareHomeTransferNotificationStatus.Processing, outbox.Status);
        Assert.Equal(retryClaim, outbox.ClaimToken);
        Assert.Equal(2, outbox.AttemptCount);
    }

    [Fact]
    public void Retry_backoff_is_capped_at_one_hour()
    {
        var created = new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc);
        var outbox = CareHomeTransferNotificationOutbox.Create(Guid.NewGuid(), Guid.NewGuid(), created);
        for (int attempt = 1; attempt <= 15; attempt++)
        {
            var claim = Guid.NewGuid();
            outbox.MarkProcessing(created.AddMinutes(attempt * 65), claim);
            outbox.MarkFailed("Temporary outage", created.AddMinutes(attempt * 65), claim);
        }

        Assert.Equal(15, outbox.AttemptCount);
        Assert.Equal(created.AddMinutes(15 * 65 + 60), outbox.NextAttemptOnUtc);
    }
}
