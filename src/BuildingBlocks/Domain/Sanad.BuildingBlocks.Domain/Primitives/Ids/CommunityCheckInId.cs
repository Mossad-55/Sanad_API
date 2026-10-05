namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct CommunityCheckInId(Guid Value)
{
    public static CommunityCheckInId Empty => new(Guid.Empty);
    public static CommunityCheckInId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}
