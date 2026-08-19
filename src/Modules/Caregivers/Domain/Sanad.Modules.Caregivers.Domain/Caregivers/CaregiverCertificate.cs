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

    public string? RejectionReason { get; private set; }

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

        RejectionReason = null;
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