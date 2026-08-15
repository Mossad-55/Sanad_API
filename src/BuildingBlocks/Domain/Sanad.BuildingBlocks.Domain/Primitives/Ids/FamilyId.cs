namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct FamilyId(Guid Value)
{
    public static FamilyId New() => new(Guid.CreateVersion7());
    public static FamilyId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}