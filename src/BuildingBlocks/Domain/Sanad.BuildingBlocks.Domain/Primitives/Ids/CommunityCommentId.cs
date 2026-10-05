namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct CommunityCommentId(Guid Value)
{
    public static CommunityCommentId Empty => new(Guid.Empty);
    public static CommunityCommentId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}
