namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct CommunityReplyId(Guid Value)
{
    public static CommunityReplyId Empty => new(Guid.Empty);
    public static CommunityReplyId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}
