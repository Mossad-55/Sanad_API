namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct CaregiverCertificateVersionId(Guid Value)
{
    public static CaregiverCertificateVersionId New() => new(Guid.CreateVersion7());
    public static CaregiverCertificateVersionId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
