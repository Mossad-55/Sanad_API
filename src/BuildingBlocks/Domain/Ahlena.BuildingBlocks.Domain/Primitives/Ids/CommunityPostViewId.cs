namespace Ahlena.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct CommunityPostViewId(Guid Value)
{
    public static CommunityPostViewId Empty => new(Guid.Empty);
    public static CommunityPostViewId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}
