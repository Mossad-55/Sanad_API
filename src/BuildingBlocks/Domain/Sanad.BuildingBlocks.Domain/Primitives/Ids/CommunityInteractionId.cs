namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct CommunityInteractionId(Guid Value)
{
    public static CommunityInteractionId Empty => new(Guid.Empty);
    public static CommunityInteractionId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}
