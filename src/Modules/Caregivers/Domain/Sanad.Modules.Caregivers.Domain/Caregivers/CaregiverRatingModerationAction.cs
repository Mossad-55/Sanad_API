using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

/// <summary>
/// The closed set of F7 moderation actions recorded against a caregiver
/// rating. A rating is hidden and restored any number of times; every
/// transition appends exactly one row here.
/// </summary>
public enum CaregiverRatingModerationActionType
{
    Hidden = 0,
    Restored = 1
}

/// <summary>
/// F7 append-only audit record for one attributed caregiver-rating moderation
/// action. Rows are never updated or deleted, so the full hide/restore history
/// survives even though the rating itself only carries the current state.
/// </summary>
public sealed class CaregiverRatingModerationAction : Entity<Guid>
{
    private CaregiverRatingModerationAction()
    {
    }

    private CaregiverRatingModerationAction(
        Guid id,
        Guid ratingId,
        CaregiverId caregiverId,
        CaregiverRatingModerationActionType action,
        UserId actorUserId,
        string reason,
        DateTime occurredOnUtc)
        : base(id)
    {
        RatingId = ratingId;
        CaregiverId = caregiverId;
        Action = action;
        ActorUserId = actorUserId;
        Reason = reason;
        OccurredOnUtc = occurredOnUtc;
    }

    public Guid RatingId { get; private set; }
    public CaregiverId CaregiverId { get; private set; }
    public CaregiverRatingModerationActionType Action { get; private set; }
    public UserId ActorUserId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTime OccurredOnUtc { get; private set; }

    public static CaregiverRatingModerationAction Record(
        Guid ratingId,
        CaregiverId caregiverId,
        CaregiverRatingModerationActionType action,
        UserId actorUserId,
        string reason,
        DateTime occurredOnUtc)
    {
        if (ratingId == Guid.Empty)
            throw new DomainException("A moderation action requires a rating.");
        if (caregiverId == CaregiverId.Empty)
            throw new DomainException("A moderation action requires a caregiver.");
        if (actorUserId == UserId.Empty)
            throw new DomainException("A moderation action requires an actor.");
        if (occurredOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Moderation action timestamps must be UTC.");

        return new CaregiverRatingModerationAction(
            Guid.CreateVersion7(),
            ratingId,
            caregiverId,
            action,
            actorUserId,
            NormalizeReason(reason),
            occurredOnUtc);
    }

    private static string NormalizeReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A moderation reason is required.");

        string normalized = reason.Trim();
        if (normalized.Length > CaregiverRating.MaximumModerationReasonLength)
            throw new DomainException(
                $"Moderation reason cannot exceed {CaregiverRating.MaximumModerationReasonLength} characters.");

        return normalized;
    }
}
