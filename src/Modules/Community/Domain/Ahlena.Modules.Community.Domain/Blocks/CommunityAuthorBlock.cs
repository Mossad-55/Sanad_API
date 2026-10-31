using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Community.Domain.Blocks;

/// <summary>
/// Per-viewer Community block over the ordered pair (blocker, blocked author).
/// The pair is unique per blocker; removing the block restores the blocked
/// author's posts to the viewer's feed.
/// <para>
/// The relation is owned by the Community module: no Chat block and no Chat row
/// is read or written here, and blocking never denies a comment, a reply or
/// any other interaction.
/// </para>
/// </summary>
public sealed class CommunityAuthorBlock : AggregateRoot<CommunityAuthorBlockId>
{
    private CommunityAuthorBlock()
    {
    }

    private CommunityAuthorBlock(
        CommunityAuthorBlockId id,
        UserId blockerUserId,
        UserId blockedAuthorUserId,
        DateTime createdOnUtc)
        : base(id)
    {
        BlockerUserId = blockerUserId;
        BlockedAuthorUserId = blockedAuthorUserId;
        CreatedOnUtc = createdOnUtc;
    }

    public UserId BlockerUserId { get; private set; } = default!;

    public UserId BlockedAuthorUserId { get; private set; } = default!;

    public DateTime CreatedOnUtc { get; private set; }

    public static CommunityAuthorBlock Create(
        UserId blockerUserId,
        UserId blockedAuthorUserId,
        DateTime createdOnUtc)
    {
        if (blockerUserId == UserId.Empty || blockedAuthorUserId == UserId.Empty)
        {
            throw new DomainException(
                "Block users are required.");
        }

        if (blockerUserId == blockedAuthorUserId)
        {
            throw new DomainException(
                "Users cannot block themselves.");
        }

        if (createdOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Block timestamps must be UTC.");
        }

        return new CommunityAuthorBlock(
            CommunityAuthorBlockId.New(),
            blockerUserId,
            blockedAuthorUserId,
            createdOnUtc);
    }
}
