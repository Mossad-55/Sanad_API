namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct CaregiverCertificateId(Guid Value)
{
    public static CaregiverCertificateId New() => new(Guid.CreateVersion7());
    public static CaregiverCertificateId Empty() => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}