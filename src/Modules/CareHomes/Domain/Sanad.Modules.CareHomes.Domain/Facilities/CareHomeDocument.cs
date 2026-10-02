using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.CareHomes.Domain.Facilities;

public sealed class CareHomeDocument : Entity<Guid>
{
    private CareHomeDocument()
    {
    }

    private CareHomeDocument(
        Guid id,
        CareHomeDocumentType type,
        Guid profileRevisionId,
        string privateStorageKey,
        string contentType,
        long length,
        DateOnly? expiryDate,
        DateTime utcNow)
        : base(id)
    {
        Type = type;
        ProfileRevisionId = profileRevisionId;
        PrivateStorageKey = privateStorageKey;
        ContentType = contentType;
        Length = length;
        ExpiryDate = expiryDate;
        Status = CareHomeDocumentStatus.PendingReview;
        CreatedOnUtc = utcNow;
        UpdatedOnUtc = utcNow;
    }

    public CareHomeDocumentType Type { get; private set; }
    public Guid ProfileRevisionId { get; private set; }
    public string PrivateStorageKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long Length { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }
    public bool VerifiedNonExpiring { get; private set; }
    public CareHomeDocumentStatus Status { get; private set; }
    public UserId? VerifiedByUserId { get; private set; }
    public DateTime? VerifiedOnUtc { get; private set; }
    public string? ReviewReason { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }

    internal static CareHomeDocument Upload(
        CareHomeDocumentType type,
        Guid profileRevisionId,
        string privateStorageKey,
        string contentType,
        long length,
        DateOnly? expiryDate,
        DateTime utcNow)
    {
        if (!Enum.IsDefined(type))
            throw new DomainException("Care-home document type is invalid.");
        if (profileRevisionId == Guid.Empty)
            throw new DomainException("Care-home document revision is required.");
        if (string.IsNullOrWhiteSpace(privateStorageKey))
            throw new DomainException("Care-home document storage key is required.");
        if (contentType is not ("application/pdf" or "image/jpeg" or "image/png"))
            throw new DomainException("Care-home documents must be PDF, JPG, or PNG.");
        if (length is <= 0 or > 10_485_760)
            throw new DomainException("Care-home documents must be between 1 byte and 10 MB.");
        EnsureUtc(utcNow);

        return new CareHomeDocument(
            Guid.CreateVersion7(), type, profileRevisionId,
            privateStorageKey.Trim(), contentType, length, expiryDate, utcNow);
    }

    internal void Verify(
        UserId adminUserId,
        DateOnly? expiryDate,
        bool confirmNonExpiring,
        DateTime utcNow)
    {
        if (Status != CareHomeDocumentStatus.PendingReview)
            throw new DomainException("Only pending care-home documents can be verified.");
        if (adminUserId == UserId.Empty)
            throw new DomainException("Care-home document verifier is required.");
        if (expiryDate is null && !confirmNonExpiring)
            throw new DomainException("An Admin must explicitly confirm a document is non-expiring when no expiry date is recorded.");
        if (expiryDate is not null && confirmNonExpiring)
            throw new DomainException("A dated document cannot be marked non-expiring.");
        EnsureUtc(utcNow);

        ExpiryDate = expiryDate;
        VerifiedNonExpiring = expiryDate is null;
        VerifiedByUserId = adminUserId;
        VerifiedOnUtc = utcNow;
        Status = CareHomeDocumentStatus.Verified;
        ReviewReason = null;
        UpdatedOnUtc = utcNow;
    }

    internal void Reject(UserId adminUserId, string reason, DateTime utcNow)
    {
        if (Status != CareHomeDocumentStatus.PendingReview)
            throw new DomainException("Only pending care-home documents can be rejected.");
        if (adminUserId == UserId.Empty)
            throw new DomainException("Care-home document reviewer is required.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000)
            throw new DomainException("A rejection reason of at most 1000 characters is required.");
        EnsureUtc(utcNow);

        VerifiedByUserId = adminUserId;
        VerifiedOnUtc = utcNow;
        Status = CareHomeDocumentStatus.Rejected;
        ReviewReason = reason.Trim();
        UpdatedOnUtc = utcNow;
    }

    public bool IsUsableOn(DateOnly date) => Status == CareHomeDocumentStatus.Verified &&
        (VerifiedNonExpiring || (ExpiryDate is not null && ExpiryDate >= date));

    private static void EnsureUtc(DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new DomainException("Care-home document timestamps must be UTC.");
    }
}
