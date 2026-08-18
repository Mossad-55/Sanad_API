using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class CaregiverCertificate : Entity<CaregiverCertificateId>
{
    private CaregiverCertificate()
    {
    }

    private CaregiverCertificate(
        CaregiverCertificateId id,
        string name,
        string filePath,
        DateOnly? expiryDate)
        : base(id)
    {
        Name = name;
        FilePath = filePath;
        ExpiryDate = expiryDate;

        VerificationStatus =
            CertificateVerificationStatus.Pending;

        CreatedOnUtc = DateTime.UtcNow;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public string Name { get; private set; } = string.Empty;

    public string FilePath { get; private set; } = string.Empty;

    public DateOnly? ExpiryDate { get; private set; }

    public CertificateVerificationStatus VerificationStatus { get; private set; }

    public string? RejectionReason { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    public static CaregiverCertificate Create(
        string name,
        string filePath,
        DateOnly? expiryDate = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException(
                "Certificate name is required.");
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new DomainException(
                "Certificate file is required.");
        }

        return new CaregiverCertificate(
            CaregiverCertificateId.New(),
            name.Trim(),
            filePath.Trim(),
            expiryDate);
    }

    public void Verify()
    {
        VerificationStatus =
            CertificateVerificationStatus.Verified;

        RejectionReason = null;

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Reject(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                "Rejection reason is required.");
        }

        VerificationStatus =
            CertificateVerificationStatus.Rejected;

        RejectionReason = reason.Trim();

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void UpdateFile(
        string filePath,
        DateOnly? expiryDate)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new DomainException(
                "Certificate file is required.");
        }

        FilePath = filePath.Trim();
        ExpiryDate = expiryDate;

        VerificationStatus =
            CertificateVerificationStatus.Pending;

        RejectionReason = null;

        UpdatedOnUtc = DateTime.UtcNow;
    }
}