namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct CareAssessmentId(Guid Value)
{
    public static CareAssessmentId New() =>
        new(Guid.CreateVersion7());

    public static CareAssessmentId Empty =>
        new(Guid.Empty);

    public override string ToString() => Value.ToString();
}