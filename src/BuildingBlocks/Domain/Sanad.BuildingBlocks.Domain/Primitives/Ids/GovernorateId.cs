namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct GovernorateId(Guid Value)
{
    public static GovernorateId New() => new(Guid.CreateVersion7());
    public static GovernorateId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}