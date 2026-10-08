using Sanad.BuildingBlocks.Domain.Abstractions;

namespace Sanad.Modules.CareHomes.Domain.Bookings;

public enum CareHomeNotificationEvent
{
    PaymentSucceeded = 1,
    DecisionAccepted = 2,
    DecisionRejected = 3,
    DecisionExpired = 4,
    FamilyCancelledPaidBooking = 5,
    OwnerCancelledAcceptedBooking = 6,
    RefundCompleted = 7,
    RefundFailed = 8,
    DisputeOpened = 9,
    DisputeResolved = 10,
    OwnerDecisionNeeded = 11
}

public enum CareHomeNotificationOutboxStatus { Pending = 1, Processing = 2, Completed = 3, Failed = 4 }

public sealed class CareHomeNotificationOutbox : Entity<Guid>
{
    private CareHomeNotificationOutbox() { }

    private CareHomeNotificationOutbox(Guid id, string eventKey, CareHomeNotificationEvent eventType,
        Guid bookingId, Guid? disputeId, DateTime createdOnUtc) : base(id)
    {
        EventKey = eventKey;
        EventType = eventType;
        BookingId = bookingId;
        DisputeId = disputeId;
        CreatedOnUtc = createdOnUtc;
        NextAttemptOnUtc = createdOnUtc;
    }

    public string EventKey { get; private set; } = string.Empty;
    public CareHomeNotificationEvent EventType { get; private set; }
    public Guid BookingId { get; private set; }
    public Guid? DisputeId { get; private set; }
    public CareHomeNotificationOutboxStatus Status { get; private set; } = CareHomeNotificationOutboxStatus.Pending;
    public int Version { get; private set; } = 1;
    public int AttemptCount { get; private set; }
    public DateTime NextAttemptOnUtc { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? LastAttemptOnUtc { get; private set; }
    public string? LastError { get; private set; }
    public Guid? ClaimToken { get; private set; }

    public static CareHomeNotificationOutbox Create(string eventKey, CareHomeNotificationEvent eventType,
        Guid bookingId, Guid? disputeId, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(eventKey) || eventKey.Length > 180 || bookingId == Guid.Empty
            || disputeId == Guid.Empty || utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A valid care-home notification event is required.");
        return new(Guid.CreateVersion7(), eventKey, eventType, bookingId, disputeId, utcNow);
    }

    public void MarkProcessing(DateTime utcNow, Guid claimToken)
    {
        if (utcNow.Kind != DateTimeKind.Utc || claimToken == Guid.Empty)
            throw new ArgumentException("A valid UTC claim is required.");
        Status = CareHomeNotificationOutboxStatus.Processing;
        AttemptCount++;
        LastAttemptOnUtc = utcNow;
        ClaimToken = claimToken;
        Version++;
    }

    public void MarkCompleted(Guid claimToken)
    {
        EnsureClaim(claimToken);
        Status = CareHomeNotificationOutboxStatus.Completed;
        ClaimToken = null;
        LastError = null;
        Version++;
    }

    public void MarkFailed(string error, DateTime utcNow, Guid claimToken)
    {
        EnsureClaim(claimToken);
        Status = CareHomeNotificationOutboxStatus.Failed;
        LastError = error[..Math.Min(2000, error.Length)];
        NextAttemptOnUtc = utcNow.AddMinutes(Math.Min(60, Math.Max(1, AttemptCount * 5)));
        ClaimToken = null;
        Version++;
    }

    private void EnsureClaim(Guid claimToken)
    {
        if (ClaimToken != claimToken) throw new InvalidOperationException("Care-home notification outbox claim conflict.");
    }
}
