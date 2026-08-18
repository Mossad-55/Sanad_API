namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct AreaId(Guid Value)
{
    public static AreaId New() => new(Guid.CreateVersion7());
    public static AreaId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}