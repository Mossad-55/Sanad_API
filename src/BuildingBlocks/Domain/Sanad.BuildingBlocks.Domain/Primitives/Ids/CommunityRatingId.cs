namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct CommunityRatingId(Guid Value)
{
    public static CommunityRatingId Empty => new(Guid.Empty);
    public static CommunityRatingId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}
