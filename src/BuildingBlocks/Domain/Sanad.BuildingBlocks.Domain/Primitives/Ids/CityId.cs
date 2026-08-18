namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct CityId(Guid Value)
{
    public static CityId New() => new(Guid.CreateVersion7());
    public static CityId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}