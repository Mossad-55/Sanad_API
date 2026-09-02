namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct AssessmentTierId(Guid Value)
{
    public static AssessmentTierId New() =>
        new(Guid.CreateVersion7());

    public static AssessmentTierId Empty =>
        new(Guid.Empty);

    public override string ToString() => Value.ToString();
}