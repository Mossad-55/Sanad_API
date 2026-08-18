namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct ServiceId(Guid Value)
{
    public static ServiceId New() => new(Guid.CreateVersion7());
    public static ServiceId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}