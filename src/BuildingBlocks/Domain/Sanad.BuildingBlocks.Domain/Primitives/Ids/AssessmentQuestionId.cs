namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct AssessmentQuestionId(Guid Value)
{
    public static AssessmentQuestionId New() =>
        new(Guid.CreateVersion7());

    public static AssessmentQuestionId Empty =>
        new(Guid.Empty);

    public override string ToString() => Value.ToString();
}