namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct MedicalAccessGrantId(Guid Value)
{
    public static MedicalAccessGrantId Empty => new(Guid.Empty);
    public static MedicalAccessGrantId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
