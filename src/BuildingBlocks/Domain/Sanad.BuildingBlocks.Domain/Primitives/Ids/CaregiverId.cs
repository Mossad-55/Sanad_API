namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct CaregiverId(Guid Value)
{
    public static CaregiverId New() => new(Guid.CreateVersion7());
    public static CaregiverId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}