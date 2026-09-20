namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct MedicalReportId(Guid Value)
{
    public static MedicalReportId New() => new(Guid.CreateVersion7());
    public static MedicalReportId Empty => new(Guid.Empty);
}
