using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.CareHomes.Domain.Facilities;

namespace Ahlena.Modules.CareHomes.Domain.Bookings;

/// <summary>
/// The closed set of F7 moderation actions recorded against a Care Home rating.
/// A rating is hidden and restored any number of times; every transition
/// appends exactly one row here.
/// </summary>
public enum CareHomeRatingModerationActionType
{
    Hidden = 0,
    Restored = 1
}

/// <summary>
/// F7 append-only audit record for one attributed Care Home-rating moderation
/// action. Rows are never updated or deleted, so the full hide/restore history
/// survives even after a restore clears the rating's current hide fields.
/// </summary>
public sealed class CareHomeRatingModerationAction : Entity<Guid>
{
    private CareHomeRatingModerationAction() { }

    private CareHomeRatingModerationAction(Guid id, Guid ratingId, CareHomeId careHomeId,
        CareHomeRatingModerationActionType action, UserId actorUserId, string reason, DateTime occurredOnUtc)
        : base(id)
    {
        RatingId = ratingId;
        CareHomeId = careHomeId;
        Action = action;
        ActorUserId = actorUserId;
        Reason = reason;
        OccurredOnUtc = occurredOnUtc;
    }

    public Guid RatingId { get; private set; }
    public CareHomeId CareHomeId { get; private set; }
    public CareHomeRatingModerationActionType Action { get; private set; }
    public UserId ActorUserId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTime OccurredOnUtc { get; private set; }

    public static CareHomeRatingModerationAction Record(Guid ratingId, CareHomeId careHomeId,
        CareHomeRatingModerationActionType action, UserId actorUserId, string reason, DateTime occurredOnUtc)
    {
        if (ratingId == Guid.Empty)
            throw new ArgumentException("A moderation action requires a rating.", nameof(ratingId));
        if (careHomeId == CareHomeId.Empty)
            throw new ArgumentException("A moderation action requires a care home.", nameof(careHomeId));
        if (actorUserId == UserId.Empty)
            throw new ArgumentException("A moderation action requires an actor.", nameof(actorUserId));
        if (occurredOnUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Moderation action timestamps must be UTC.", nameof(occurredOnUtc));

        return new(Guid.CreateVersion7(), ratingId, careHomeId, action, actorUserId,
            NormalizeReason(reason), occurredOnUtc);
    }

    private static string NormalizeReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A moderation reason is required.", nameof(reason));

        string normalized = reason.Trim();
        if (normalized.Length > CareHomeRating.MaximumModerationReasonLength)
            throw new ArgumentException("Moderation reason exceeds the maximum length.", nameof(reason));

        return normalized;
    }
}
