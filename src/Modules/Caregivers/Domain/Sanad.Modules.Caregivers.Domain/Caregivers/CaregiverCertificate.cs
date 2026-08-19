using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class CaregiverCertificate :
    Entity<CaregiverCertificateId>
{
    private CaregiverCertificate()
    {
    }

    private CaregiverCertificate(
        CaregiverCertificateId id,
        CaregiverCertificateType type,
        string filePath,
        DateOnly? expiryDate,
        DateTime createdOnUtc)
        : base(id)
    {
        Type = type;
        FilePath = filePath;
        ExpiryDate = expiryDate;

        VerificationStatus =
            CertificateVerificationStatus.Pending;

        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public CaregiverCertificateType Type { get; private set; }

    public string FilePath { get; private set; } = string.Empty;

    public DateOnly? ExpiryDate { get; private set; }

    public CertificateVerificationStatus VerificationStatus
    {
        get;
        private set;
    }

    public string? ReviewReason { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    internal static CaregiverCertificate Create(
        CaregiverCertificateType type,
        string filePath,
        DateOnly? expiryDate,
        DateOnly currentDate)
    {
        ValidateType(type);

        string normalizedFilePath =
            NormalizeFilePath(filePath);

        ValidateExpiryDate(
            expiryDate,
            currentDate);

        DateTime createdOnUtc = DateTime.UtcNow;

        return new CaregiverCertificate(
            CaregiverCertificateId.New(),
            type,
            normalizedFilePath,
            expiryDate,
            createdOnUtc);
    }

    internal void Verify()
    {
        if(VerificationStatus != CertificateVerificationStatus.Pending)
        {
            throw new DomainException(
            "Only a Pending Certificate can be Verified.");
        }

        VerificationStatus = CertificateVerificationStatus.Verified;

        ReviewReason = null;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    internal void Reject(string reason)
    {
        if(VerificationStatus != CertificateVerificationStatus.Pending)
        {
            throw new DomainException(
                "Only a Pending Certificate can be Rejected."
            );
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                "Rejection reason is required."
            );
        }

        VerificationStatus = CertificateVerificationStatus.Rejected;

        ReviewReason = reason.Trim();

        UpdatedOnUtc = DateTime.UtcNow;
    }

    internal void Revoke(string reason)
    {
        if(VerificationStatus != CertificateVerificationStatus.Verified)
        {
            throw new DomainException(
                "Only a Verified Certificate can be Revoked."
            );
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                "Revocation reason is required."
            );
        }

        VerificationStatus = CertificateVerificationStatus.Revoked;

        ReviewReason = reason.Trim();
        UpdatedOnUtc = DateTime.UtcNow;
    }

    internal void UpdateFile(
        string filePath,
        DateOnly? expiryDate,
        DateOnly currentDate)
    {
        string normalizedFilePath =
            NormalizeFilePath(filePath);

        ValidateExpiryDate(
            expiryDate,
            currentDate);

        FilePath = normalizedFilePath;
        ExpiryDate = expiryDate;

        VerificationStatus =
            CertificateVerificationStatus.Pending;

        ReviewReason = null;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    private static void ValidateType(
        CaregiverCertificateType type)
    {
        if (!Enum.IsDefined(type))
        {
            throw new DomainException(
                "Caregiver certificate type is invalid.");
        }
    }

    private static string NormalizeFilePath(
        string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new DomainException(
                "Certificate file is required.");
        }

        return filePath.Trim();
    }

    private static void ValidateExpiryDate(
        DateOnly? expiryDate,
        DateOnly currentDate)
    {
        if (expiryDate.HasValue &&
            expiryDate.Value < currentDate)
        {
            throw new DomainException(
                "Certificate has already expired.");
        }
    }
}