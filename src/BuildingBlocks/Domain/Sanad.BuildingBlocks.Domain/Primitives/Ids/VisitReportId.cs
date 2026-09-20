namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct VisitReportId(Guid Value)
{
    public static VisitReportId New() => new(Guid.CreateVersion7());
    public static VisitReportId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
