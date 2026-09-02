namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct AssessmentOptionId(Guid Value)
{
    public static AssessmentOptionId New() =>
        new(Guid.CreateVersion7());

    public static AssessmentOptionId Empty =>
        new(Guid.Empty);

    public override string ToString() => Value.ToString();
}