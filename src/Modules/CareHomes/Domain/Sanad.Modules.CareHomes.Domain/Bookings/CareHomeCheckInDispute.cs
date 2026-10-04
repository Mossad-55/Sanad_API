using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Domain.Bookings;

public sealed class CareHomeCheckInDispute : Entity<Guid>
{
    private CareHomeCheckInDispute() { }

    private CareHomeCheckInDispute(Guid id, Guid bookingId, CareHomeId facilityId, DateTime? checkoutOnUtc, UserId actor, DateTime utcNow, string? reason)
        : base(id)
    {
        BookingId = bookingId; FacilityId = facilityId; CheckoutOnUtc = checkoutOnUtc;
        OpenedBy = actor; OpenedOnUtc = utcNow; Status = CareHomeCheckInDisputeStatus.Open; FamilyReason = reason;
    }

    public Guid BookingId { get; private set; }
    public CareHomeId FacilityId { get; private set; }
    public DateTime? CheckoutOnUtc { get; private set; }
    public UserId OpenedBy { get; private set; }
    public DateTime OpenedOnUtc { get; private set; }
    public CareHomeCheckInDisputeStatus Status { get; private set; }
    public UserId? ResolvedBy { get; private set; }
    public DateTime? ResolvedOnUtc { get; private set; }
    public DateTime? EffectiveCheckInOnUtc { get; private set; }
    public string? Evidence { get; private set; }
    public string? Reason { get; private set; }
    public string? FamilyReason { get; private set; }

    public static CareHomeCheckInDispute Open(Guid bookingId, CareHomeId facilityId, DateTime? checkoutOnUtc, UserId actor, DateTime utcNow, string? reason = null)
    {
        if (bookingId == Guid.Empty || facilityId == CareHomeId.Empty || (checkoutOnUtc is DateTime checkout && checkout.Kind != DateTimeKind.Utc) || utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A valid booking and UTC timestamps are required.");
        if (reason is not null && (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000))
            throw new ArgumentException("A dispute reason between 1 and 2000 characters is required.");
        return new(Guid.CreateVersion7(), bookingId, facilityId, checkoutOnUtc, actor, utcNow, reason?.Trim());
    }

    public void AttachFamilyReason(string reason)
    {
        if (FamilyReason is not null) return;
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000)
            throw new ArgumentException("A dispute reason between 1 and 2000 characters is required.");
        FamilyReason = reason.Trim();
    }

    public void Resolve(UserId actor, DateTime effectiveCheckInOnUtc, string evidence, string reason, DateTime utcNow)
    {
        if (Status != CareHomeCheckInDisputeStatus.Open) throw new InvalidOperationException("CareHomes.CheckInDispute.InvalidState");
        if (effectiveCheckInOnUtc.Kind != DateTimeKind.Utc || utcNow.Kind != DateTimeKind.Utc || string.IsNullOrWhiteSpace(evidence) || string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Evidence, reason and a UTC effective check-in time are required.");
        Status = CareHomeCheckInDisputeStatus.Resolved; ResolvedBy = actor; ResolvedOnUtc = utcNow;
        EffectiveCheckInOnUtc = effectiveCheckInOnUtc; Evidence = evidence.Trim(); Reason = reason.Trim();
    }
}
