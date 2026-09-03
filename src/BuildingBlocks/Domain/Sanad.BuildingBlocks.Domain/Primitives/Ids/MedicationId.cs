namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct MedicationId(Guid Value)
{
    public static MedicationId New() => new(Guid.CreateVersion7());
    public static MedicationId Empty => new(Guid.Empty);

    public override string ToString() => Value.ToString();
}