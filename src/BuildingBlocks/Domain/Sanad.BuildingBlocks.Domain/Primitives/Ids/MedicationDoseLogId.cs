namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct MedicationDoseLogId(Guid Value)
{
    public static MedicationDoseLogId New() => new(Guid.CreateVersion7());
    public static MedicationDoseLogId Empty => new(Guid.Empty);

    public override string ToString() => Value.ToString();
}