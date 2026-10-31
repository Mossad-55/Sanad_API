using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Chat.Domain.Blocks;

/// <summary>
/// Per-user chat block over the ordered pair (blocker, blocked). The pair is
/// unique per blocker; removing the block restores sending.
/// </summary>
public sealed class Block : AggregateRoot<BlockId>
{
    private Block()
    {
    }

    private Block(
        BlockId id,
        UserId blockerUserId,
        UserId blockedUserId,
        DateTime createdOnUtc)
        : base(id)
    {
        BlockerUserId = blockerUserId;
        BlockedUserId = blockedUserId;
        CreatedOnUtc = createdOnUtc;
    }

    public UserId BlockerUserId { get; private set; } = default!;

    public UserId BlockedUserId { get; private set; } = default!;

    public DateTime CreatedOnUtc { get; private set; }

    public static Block Create(
        UserId blockerUserId,
        UserId blockedUserId,
        DateTime createdOnUtc)
    {
        if (blockerUserId == UserId.Empty || blockedUserId == UserId.Empty)
        {
            throw new DomainException(
                "Block users are required.");
        }

        if (blockerUserId == blockedUserId)
        {
            throw new DomainException(
                "Users cannot block themselves.");
        }

        if (createdOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Block timestamps must be UTC.");
        }

        return new Block(
            BlockId.New(),
            blockerUserId,
            blockedUserId,
            createdOnUtc);
    }
}
