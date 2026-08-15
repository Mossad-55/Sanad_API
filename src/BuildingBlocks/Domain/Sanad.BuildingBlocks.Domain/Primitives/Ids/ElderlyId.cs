namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct ElderlyId(Guid Value)
{
    public static ElderlyId New() => new(Guid.CreateVersion7());
    public static ElderlyId Empty => new(Guid.Empty);

    public override string ToString() => Value.ToString();
}