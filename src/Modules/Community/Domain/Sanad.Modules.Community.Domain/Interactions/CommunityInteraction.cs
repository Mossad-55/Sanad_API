using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Community.Domain.Interactions;

public sealed class CommunityInteraction : Entity<CommunityInteractionId>
{
    private CommunityInteraction() { }

    private CommunityInteraction(
        CommunityInteractionId id,
        CommunityInteractionTarget targetType,
        Guid targetId,
        UserId userId,
        CommunityInteractionKind kind,
        DateTime createdOnUtc)
    {
        Id = id;
        TargetType = targetType;
        TargetId = targetId;
        UserId = userId;
        Kind = kind;
        CreatedOnUtc = createdOnUtc;
    }

    public CommunityInteractionTarget TargetType { get; private set; }
    public Guid TargetId { get; private set; }
    public UserId UserId { get; private set; }
    public CommunityInteractionKind Kind { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    public static CommunityInteraction Create(
        CommunityInteractionTarget targetType,
        Guid targetId,
        UserId userId,
        CommunityInteractionKind kind,
        DateTime createdOnUtc)
    {
        if (!Enum.IsDefined(targetType)) throw new ArgumentOutOfRangeException(nameof(targetType));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (targetId == Guid.Empty) throw new ArgumentException("Target ID is required.", nameof(targetId));
        if (userId == UserId.Empty) throw new ArgumentException("User ID is required.", nameof(userId));
        if (createdOnUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Timestamp must be UTC.", nameof(createdOnUtc));
        if (targetType == CommunityInteractionTarget.Comment && kind == CommunityInteractionKind.Favorite)
            throw new ArgumentException("Comments cannot be favorited.", nameof(kind));

        return new(CommunityInteractionId.New(), targetType, targetId, userId, kind, createdOnUtc);
    }
}

public enum CommunityInteractionTarget { Post = 1, Comment = 2 }
public enum CommunityInteractionKind { Like = 1, Favorite = 2 }
