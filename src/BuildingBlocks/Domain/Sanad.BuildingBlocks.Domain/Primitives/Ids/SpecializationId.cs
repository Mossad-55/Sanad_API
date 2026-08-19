namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct SpecializationId(Guid Value)
{
    public static SpecializationId New() => new(Guid.CreateVersion7());

    public static SpecializationId Empty => new(Guid.Empty);

    public override string ToString() => Value.ToString();
}