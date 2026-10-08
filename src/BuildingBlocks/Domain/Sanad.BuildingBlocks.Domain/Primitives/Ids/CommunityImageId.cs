namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct CommunityImageId(Guid Value)
{
    public static CommunityImageId New() => new(Guid.CreateVersion7());
    public static CommunityImageId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
