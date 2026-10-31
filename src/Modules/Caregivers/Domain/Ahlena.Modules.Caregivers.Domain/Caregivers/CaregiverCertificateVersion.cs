using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Caregivers.Domain.Caregivers;

/// <summary>
/// One uploaded scan (version) of a <see cref="CaregiverCertificate"/>
/// qualification. Versions are append-only history: a replacement or a review
/// never deletes an earlier version or its stored file. Exactly one version of
/// a qualification may be <see cref="IsCurrent"/> (the effective, backing the
/// certificate's effective columns); at most one further version may be
/// pending review. Everything else is retained history.
/// </summary>
/// <remarks>
/// A retained-history version may additionally be soft-removed from the
/// caregiver's history (<see cref="IsRemoved"/>). Removal is a logical
/// tombstone only: the row and its stored file stay retained for audit, but
/// the version is hidden from the normal read surfaces, its file is no longer
/// downloadable, and it can never be promoted, verified or reanimated. The
/// effective approved version (even an expired one) and the live pending
/// candidate can never be removed this way.
/// </remarks>
public sealed class CaregiverCertificateVersion :
    Entity<CaregiverCertificateVersionId>
{
    public const int MaximumReviewReasonLength = 500;

    private CaregiverCertificateVersion()
    {
    }

    private CaregiverCertificateVersion(
        CaregiverCertificateVersionId id,
        string filePath,
        DateOnly? expiryDate,
        CertificateVerificationStatus verificationStatus,
        string? reviewReason,
        bool isCurrent,
        DateTime? supersededOnUtc,
        DateTime createdOnUtc,
        DateTime updatedOnUtc)
        : base(id)
    {
        FilePath = filePath;
        ExpiryDate = expiryDate;
        VerificationStatus = verificationStatus;
        ReviewReason = reviewReason;
        IsCurrent = isCurrent;
        SupersededOnUtc = supersededOnUtc;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = updatedOnUtc;
    }

    public string FilePath { get; private set; } = string.Empty;

    public DateOnly? ExpiryDate { get; private set; }

    public CertificateVerificationStatus VerificationStatus
    {
        get;
        private set;
    }

    public string? ReviewReason { get; private set; }

    /// <summary>The effective version backing the certificate's columns.</summary>
    public bool IsCurrent { get; private set; }

    /// <summary>
    /// Set when a newer version replaced this one; a superseded version is
    /// pure history and can never become the current or pending version again.
    /// </summary>
    public DateTime? SupersededOnUtc { get; private set; }

    /// <summary>
    /// A logically removed history version. Retained for audit, hidden from the
    /// normal read surfaces and never promotable, verifiable or reanimatable.
    /// </summary>
    public bool IsRemoved { get; private set; }

    public DateTime? RemovedOnUtc { get; private set; }

    /// <summary>
    /// The actor who removed this version from history. A caregiver self
    /// removal carries the caregiver's own user id; an admin removal carries
    /// the acting admin's user id.
    /// </summary>
    public UserId? RemovedByUserId { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    /// <summary>
    /// The single version awaiting a review decision, if any. A removed,
    /// superseded or current version is never pending.
    /// </summary>
    public bool IsPending =>
        !IsRemoved &&
        !IsCurrent &&
        SupersededOnUtc is null &&
        VerificationStatus == CertificateVerificationStatus.Pending;

    /// <summary>
    /// A retained history version: neither the current effective version, nor
    /// the live pending candidate, nor already removed.
    /// </summary>
    public bool IsHistory =>
        !IsRemoved && !IsCurrent && !IsPending;

    internal static CaregiverCertificateVersion Create(
        string filePath,
        DateOnly? expiryDate,
        CertificateVerificationStatus verificationStatus,
        string? reviewReason,
        bool isCurrent,
        DateTime? supersededOnUtc,
        DateOnly currentDate,
        DateTime occurredOnUtc)
    {
        string normalizedFilePath = NormalizeFilePath(filePath);
        ValidateReviewReason(reviewReason);
        ValidateNewExpiryDate(expiryDate, currentDate);
        ValidateUtc(occurredOnUtc);

        return new CaregiverCertificateVersion(
            CaregiverCertificateVersionId.New(),
            normalizedFilePath,
            expiryDate,
            verificationStatus,
            reviewReason,
            isCurrent,
            supersededOnUtc,
            occurredOnUtc,
            occurredOnUtc);
    }

    internal void Promote(DateTime utcNow)
    {
        EnsureNotRemoved();

        if (SupersededOnUtc is not null)
        {
            throw new DomainException(
                "A superseded Certificate version cannot be verified.");
        }

        if (VerificationStatus != CertificateVerificationStatus.Pending)
        {
            throw new DomainException(
                "Only a Pending Certificate version can be verified.");
        }

        ValidateUtc(utcNow);

        VerificationStatus = CertificateVerificationStatus.Verified;
        ReviewReason = null;
        IsCurrent = true;
        SupersededOnUtc = null;
        UpdatedOnUtc = utcNow;
    }

    internal void Demote(DateTime utcNow)
    {
        EnsureNotRemoved();
        ValidateUtc(utcNow);

        IsCurrent = false;

        if (SupersededOnUtc is null)
        {
            SupersededOnUtc = utcNow;
        }

        UpdatedOnUtc = utcNow;
    }

    internal void Supersede(DateTime utcNow)
    {
        EnsureNotRemoved();
        ValidateUtc(utcNow);

        if (SupersededOnUtc is null)
        {
            SupersededOnUtc = utcNow;
        }

        UpdatedOnUtc = utcNow;
    }

    internal void Reject(string reason, DateTime utcNow)
    {
        EnsureNotRemoved();

        if (SupersededOnUtc is not null)
        {
            throw new DomainException(
                "A superseded Certificate version cannot be rejected.");
        }

        if (VerificationStatus != CertificateVerificationStatus.Pending)
        {
            throw new DomainException(
                "Only a Pending Certificate version can be rejected.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                "Rejection reason is required.");
        }

        ValidateUtc(utcNow);

        VerificationStatus = CertificateVerificationStatus.Rejected;
        ReviewReason = reason.Trim();
        UpdatedOnUtc = utcNow;
    }

    internal void Revoke(string reason, DateTime utcNow)
    {
        EnsureNotRemoved();

        if (!IsCurrent ||
            VerificationStatus != CertificateVerificationStatus.Verified)
        {
            throw new DomainException(
                "Only the current Verified Certificate version can be revoked.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                "Revocation reason is required.");
        }

        ValidateUtc(utcNow);

        VerificationStatus = CertificateVerificationStatus.Revoked;
        ReviewReason = reason.Trim();
        UpdatedOnUtc = utcNow;
    }

    /// <summary>
    /// Logically removes a retained-history version. The caller (the owning
    /// certificate) is responsible for proving the version is removable
    /// history; this guard only refuses the illegal current / pending /
    /// already-removed cases.
    /// </summary>
    internal void Remove(UserId removedByUserId, DateTime utcNow)
    {
        EnsureNotRemoved();

        if (IsCurrent)
        {
            throw new DomainException(
                "The current Certificate version cannot be removed from history.");
        }

        if (IsPending)
        {
            throw new DomainException(
                "A Certificate version under review cannot be removed from history.");
        }

        if (removedByUserId == UserId.Empty)
        {
            throw new DomainException(
                "Removing a Certificate version requires an actor.");
        }

        ValidateUtc(utcNow);

        IsRemoved = true;
        RemovedOnUtc = utcNow;
        RemovedByUserId = removedByUserId;
        UpdatedOnUtc = utcNow;
    }

    /// <summary>
    /// Derived expiry against the supplied Cairo calendar date. A
    /// <c>null</c> date is <see cref="CertificateExpiryStatus.None"/> ("no
    /// expiry recorded"), never an invented date or a non-expiring approval.
    /// </summary>
    public CertificateExpiryStatus ExpiryStatus(DateOnly currentDate)
    {
        if (!ExpiryDate.HasValue)
        {
            return CertificateExpiryStatus.None;
        }

        return ExpiryDate.Value < currentDate
            ? CertificateExpiryStatus.Expired
            : CertificateExpiryStatus.Valid;
    }

    public bool IsExpired(DateOnly currentDate) =>
        ExpiryDate.HasValue && ExpiryDate.Value < currentDate;

    private void EnsureNotRemoved()
    {
        if (IsRemoved)
        {
            throw new DomainException(
                "A removed Certificate version can no longer change.");
        }
    }

    private static string NormalizeFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new DomainException(
                "Certificate file is required.");
        }

        return filePath.Trim();
    }

    private static void ValidateReviewReason(string? reviewReason)
    {
        if (reviewReason is not null &&
            reviewReason.Length > MaximumReviewReasonLength)
        {
            throw new DomainException(
                $"Certificate review reason cannot exceed " +
                $"{MaximumReviewReasonLength} characters.");
        }
    }

    /// <summary>
    /// A newly uploaded version may not already be expired. An already-stored
    /// version (including a legacy snapshot) is never re-validated here, so a
    /// certificate can honestly become Verified-then-expired as the Cairo
    /// calendar advances.
    /// </summary>
    private static void ValidateNewExpiryDate(
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

    private static void ValidateUtc(DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Transition time must be in UTC.");
        }
    }
}
