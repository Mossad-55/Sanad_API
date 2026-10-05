namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct CommunityPostId(Guid Value)
{
    public static CommunityPostId Empty => new(Guid.Empty);
    public static CommunityPostId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}
