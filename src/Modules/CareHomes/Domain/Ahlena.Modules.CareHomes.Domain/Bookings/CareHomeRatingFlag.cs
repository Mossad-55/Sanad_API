using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.CareHomes.Domain.Facilities;

namespace Ahlena.Modules.CareHomes.Domain.Bookings;

/// <summary>
/// F7 owner-flag lifecycle. A flag starts <see cref="Open"/> and leaves that
/// state exactly once, when an admin hides or keeps the flagged rating. A
/// resolved flag no longer occupies the owner's single unresolved slot.
/// </summary>
public enum CareHomeRatingFlagStatus
{
    Open = 0,
    Resolved = 1
}

/// <summary>
/// How an admin closed a flag: the rating was hidden, or it was kept visible.
/// </summary>
public enum CareHomeRatingFlagResolution
{
    Hidden = 0,
    Kept = 1
}

/// <summary>
/// F7 owner flag over a rating about the owner's own facility. At most one
/// unresolved flag exists per (rating, owner), enforced by a filtered unique
/// index; resolution is one-shot with the resolving admin, UTC time and the
/// action taken, and a <see cref="Version"/> concurrency token makes a
/// concurrent resolution a 409 instead of a silent second write.
/// </summary>
public sealed class CareHomeRatingFlag : Entity<Guid>
{
    /// <summary>F7 bound on the owner's flag reason.</summary>
    public const int MaximumReasonLength = 500;

    private CareHomeRatingFlag() { }

    private CareHomeRatingFlag(Guid id, Guid ratingId, CareHomeId careHomeId, UserId ownerUserId,
        string reason, DateTime createdOnUtc) : base(id)
    {
        RatingId = ratingId;
        CareHomeId = careHomeId;
        OwnerUserId = ownerUserId;
        Reason = reason;
        CreatedOnUtc = createdOnUtc;
        Status = CareHomeRatingFlagStatus.Open;
    }

    public Guid RatingId { get; private set; }
    public CareHomeId CareHomeId { get; private set; }
    public UserId OwnerUserId { get; private set; }
    public CareHomeRatingFlagStatus Status { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? ResolvedOnUtc { get; private set; }
    public UserId? ResolvedByUserId { get; private set; }
    public CareHomeRatingFlagResolution? Resolution { get; private set; }

    /// <summary>
    /// Optimistic-resolution token: incremented by every
    /// <see cref="Resolve"/> transition and configured as an EF concurrency
    /// token, so two concurrent resolutions collide in persistence and the
    /// loser surfaces as already resolved instead of resolving twice.
    /// </summary>
    public long Version { get; private set; }

    public bool IsOpen => Status == CareHomeRatingFlagStatus.Open;

    public static CareHomeRatingFlag Open(Guid ratingId, CareHomeId careHomeId, UserId ownerUserId,
        string reason, DateTime createdOnUtc)
    {
        if (ratingId == Guid.Empty)
            throw new ArgumentException("A rating flag requires a rating.", nameof(ratingId));
        if (careHomeId == CareHomeId.Empty)
            throw new ArgumentException("A rating flag requires a care home.", nameof(careHomeId));
        if (ownerUserId == UserId.Empty)
            throw new ArgumentException("A rating flag requires an owner.", nameof(ownerUserId));
        if (createdOnUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A rating flag requires a UTC timestamp.", nameof(createdOnUtc));

        return new(Guid.CreateVersion7(), ratingId, careHomeId, ownerUserId,
            NormalizeReason(reason), createdOnUtc);
    }

    /// <summary>Resolves an open flag; any other state is terminal.</summary>
    public void Resolve(UserId actorUserId, CareHomeRatingFlagResolution resolution, DateTime utcNow)
    {
        if (!IsOpen)
            throw new InvalidOperationException("CareHomes.Rating.NoOpenFlag");
        if (actorUserId == UserId.Empty)
            throw new ArgumentException("A flag resolution requires an admin.", nameof(actorUserId));
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A flag resolution requires a UTC timestamp.", nameof(utcNow));
        if (resolution is not (CareHomeRatingFlagResolution.Hidden or CareHomeRatingFlagResolution.Kept))
            throw new ArgumentException("A flag resolution is invalid.", nameof(resolution));

        Status = CareHomeRatingFlagStatus.Resolved;
        ResolvedByUserId = actorUserId;
        ResolvedOnUtc = utcNow;
        Resolution = resolution;
        Version++;
    }

    private static string NormalizeReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A rating flag reason is required.", nameof(reason));

        string normalized = reason.Trim();
        if (normalized.Length > MaximumReasonLength)
            throw new ArgumentException("Rating flag reason exceeds the maximum length.", nameof(reason));

        return normalized;
    }
}
