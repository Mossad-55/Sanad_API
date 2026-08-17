using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class CaregiverCertificate : ValueObject
{
    private CaregiverCertificate()
    {
    }

    private CaregiverCertificate(
        string name,
        DateOnly? expiryDate)
    {
        Name = name;
        ExpiryDate = expiryDate;
    }

    public string Name { get; private set; } = string.Empty;

    public DateOnly? ExpiryDate { get; private set; }

    public static CaregiverCertificate Create(
        string name,
        DateOnly? expiryDate = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Certificate name is required.");
        }

        return new CaregiverCertificate(
            name.Trim(),
            expiryDate);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Name;
        yield return ExpiryDate;
    }
}