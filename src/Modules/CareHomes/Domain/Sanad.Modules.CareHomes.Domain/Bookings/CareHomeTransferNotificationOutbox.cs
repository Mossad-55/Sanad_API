using Sanad.BuildingBlocks.Domain.Abstractions;

namespace Sanad.Modules.CareHomes.Domain.Bookings;

public enum CareHomeTransferNotificationStatus { Pending = 1, Processing = 2, Completed = 3, Failed = 4 }

public sealed class CareHomeTransferNotificationOutbox : Entity<Guid>
{
    private CareHomeTransferNotificationOutbox() { }
    private CareHomeTransferNotificationOutbox(Guid id, Guid bookingId, Guid transferId, DateTime createdOnUtc) : base(id)
    {
        BookingId = bookingId; TransferId = transferId; CreatedOnUtc = createdOnUtc; NextAttemptOnUtc = createdOnUtc;
    }
    public Guid BookingId { get; private set; }
    public Guid TransferId { get; private set; }
    public CareHomeTransferNotificationStatus Status { get; private set; } = CareHomeTransferNotificationStatus.Pending;
    public int AttemptCount { get; private set; }
    public DateTime NextAttemptOnUtc { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? LastAttemptOnUtc { get; private set; }
    public string? LastError { get; private set; }
    public Guid? ClaimToken { get; private set; }

    public static CareHomeTransferNotificationOutbox Create(Guid bookingId, Guid transferId, DateTime utcNow)
    {
        if (bookingId == Guid.Empty || transferId == Guid.Empty || utcNow.Kind != DateTimeKind.Utc) throw new ArgumentException("Transfer notification identifiers are required.");
        return new(Guid.CreateVersion7(), bookingId, transferId, utcNow);
    }
    public void MarkProcessing(DateTime utcNow, Guid claimToken) { Status = CareHomeTransferNotificationStatus.Processing; AttemptCount++; LastAttemptOnUtc = utcNow; ClaimToken = claimToken; }
    public void MarkCompleted() { Status = CareHomeTransferNotificationStatus.Completed; ClaimToken = null; LastError = null; }
    public void MarkFailed(string error, DateTime utcNow, Guid claimToken) { if (ClaimToken != claimToken) throw new InvalidOperationException("Transfer notification claim conflict."); Status = CareHomeTransferNotificationStatus.Failed; LastError = error[..Math.Min(2000, error.Length)]; NextAttemptOnUtc = utcNow.AddMinutes(Math.Min(60, Math.Max(1, AttemptCount * 5))); ClaimToken = null; }
}
