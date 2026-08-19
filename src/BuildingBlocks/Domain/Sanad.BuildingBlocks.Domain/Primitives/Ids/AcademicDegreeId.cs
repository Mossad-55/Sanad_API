namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct AcademicDegreeId(Guid Value)
{
    public static AcademicDegreeId New() => new(Guid.CreateVersion7());

    public static AcademicDegreeId Empty => new(Guid.Empty);

    public override string ToString() => Value.ToString();
}