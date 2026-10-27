using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

/// <summary>
/// A caregiver qualification. The entity is the stable entry point: its id
/// never changes across a file replacement, an admin review or a logical
/// removal, and several qualifications of the same
/// <see cref="CaregiverCertificateType"/> may coexist (the mandatory types are
/// still capped at one each by the aggregate; Additional may reach the cap).
///
/// The uploads live in an append-only <see cref="Versions"/> history. The row's
/// own <see cref="FilePath"/>/<see cref="ExpiryDate"/>/<see cref="VerificationStatus"/>/
/// <see cref="ReviewReason"/> remain the effective snapshot for backward
/// compatibility and always mirror the current version (or, when nothing has
/// been approved yet, the newest pending version; or, after an initial
/// rejection, the rejected version so the mandatory-safety consequence still
/// applies).
/// </summary>
public sealed class CaregiverCertificate :
    Entity<CaregiverCertificateId>
{
    private readonly List<CaregiverCertificateVersion> _versions = [];

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
        VerificationStatus = CertificateVerificationStatus.Pending;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public CaregiverCertificateType Type { get; private set; }

    /// <summary>Effective private-storage key (never served publicly).</summary>
    public string FilePath { get; private set; } = string.Empty;

    /// <summary>Effective expiry date; <c>null</c> = "no expiry recorded".</summary>
    public DateOnly? ExpiryDate { get; private set; }

    public CertificateVerificationStatus VerificationStatus
    {
        get;
        private set;
    }

    public string? ReviewReason { get; private set; }

    /// <summary>
    /// A logically removed (tombstoned) qualification. Its versions and files
    /// stay retained for authorized history, but it is excluded from the
    /// active profile, the additional-certificate cap and public discovery.
    /// </summary>
    public bool IsRemoved { get; private set; }

    public DateTime? RemovedOnUtc { get; private set; }

    /// <summary>
    /// Optimistic concurrency token for the qualification row. Every mutation
    /// (a new version, a review decision, a history removal or the whole
    /// qualification tombstone) bumps it, so a concurrent editor that loaded an
    /// older classification loses the race instead of silently overwriting it.
    /// </summary>
    public int Revision { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    /// <summary>Every version, including logically removed history.</summary>
    public IReadOnlyCollection<CaregiverCertificateVersion> Versions =>
        _versions.AsReadOnly();

    /// <summary>The single effective version, if one exists.</summary>
    public CaregiverCertificateVersion? CurrentVersion =>
        _versions.SingleOrDefault(version => version.IsCurrent);

    /// <summary>The single version awaiting a review decision, if any.</summary>
    public CaregiverCertificateVersion? PendingVersion =>
        _versions
            .Where(version => version.IsPending)
            .OrderByDescending(version => version.CreatedOnUtc)
            .ThenByDescending(version => version.Id.Value)
            .FirstOrDefault();

    public bool HasPendingReplacement => PendingVersion is not null;

    public bool HasCurrentVersion => CurrentVersion is not null;

    /// <summary>The non-removed versions in read order (newest first).</summary>
    public IReadOnlyList<CaregiverCertificateVersion> VisibleVersions =>
        _versions
            .Where(version => !version.IsRemoved)
            .OrderByDescending(version => version.CreatedOnUtc)
            .ThenByDescending(version => version.Id.Value)
            .ToList();

    /// <summary>
    /// The version whose values currently back the certificate's effective
    /// snapshot columns (see <see cref="SyncEffectiveSnapshot"/>): the approved
    /// current version, else the live pending candidate, else the newest
    /// retained version — the rejected candidate that still supplies the
    /// displayed status when nothing is approved. This is a pure read: it never
    /// materializes a legacy row, and a removed version is never returned.
    /// </summary>
    public CaregiverCertificateVersion? EffectiveSnapshotVersion
    {
        get
        {
            CaregiverCertificateVersion? current = CurrentVersion;

            if (current is not null)
            {
                return current;
            }

            CaregiverCertificateVersion? pending = PendingVersion;

            if (pending is not null)
            {
                return pending;
            }

            return _versions
                .Where(version => !version.IsRemoved)
                .OrderByDescending(version => version.CreatedOnUtc)
                .ThenByDescending(version => version.Id.Value)
                .FirstOrDefault();
        }
    }

    /// <summary>
    /// Whether one retained-history version may be logically removed. A version
    /// that backs the effective snapshot is protected even though it classifies
    /// as history: the live pending candidate, the approved current version, and
    /// the newest retained candidate after an initial rejection all keep the
    /// displayed status (and the legacy snapshot download) honest until a later
    /// upload supersedes them. Only genuinely superseded history can be removed.
    /// </summary>
    public bool CanRemoveVersion(CaregiverCertificateVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return version.IsHistory &&
            version.Id != EffectiveSnapshotVersion?.Id;
    }

    internal static CaregiverCertificate Create(
        CaregiverCertificateType type,
        string filePath,
        DateOnly? expiryDate,
        DateOnly currentDate)
    {
        ValidateType(type);

        string normalizedFilePath = NormalizeFilePath(filePath);
        ValidateNewExpiryDate(expiryDate, currentDate);

        DateTime createdOnUtc = DateTime.UtcNow;

        CaregiverCertificate certificate =
            new(
                CaregiverCertificateId.New(),
                type,
                normalizedFilePath,
                expiryDate,
                createdOnUtc);

        // A brand-new qualification starts with one pending version and no
        // approved current: it must still pass the admin review gate.
        CaregiverCertificateVersion firstVersion =
            CaregiverCertificateVersion.Create(
                normalizedFilePath,
                expiryDate,
                CertificateVerificationStatus.Pending,
                reviewReason: null,
                isCurrent: false,
                supersededOnUtc: null,
                currentDate,
                createdOnUtc);

        certificate._versions.Add(firstVersion);
        certificate.SyncEffectiveSnapshot();

        return certificate;
    }

    /// <summary>
    /// Lazily records the honest current snapshot for a legacy row that
    /// predates versioning: the values already stored on the row, timestamped
    /// with the row's own creation instant. No approval timestamp is invented
    /// and no old (possibly deleted) file is fabricated — an absent legacy file
    /// is simply not recoverable.
    /// </summary>
    internal void EnsureCurrentVersionMaterialized()
    {
        if (_versions.Count > 0)
        {
            return;
        }

        // The materialized legacy version backs the row's effective columns
        // when it represents an approved/effective state; a legacy Pending row
        // materializes as the live pending candidate instead; a legacy Rejected
        // row materializes as retained history (the row's snapshot is frozen
        // and stays the effective label until something replaces it).
        bool isCurrent =
            VerificationStatus is
                CertificateVerificationStatus.Verified or
                CertificateVerificationStatus.Revoked;

        CaregiverCertificateVersion legacy =
            CaregiverCertificateVersion.Create(
                FilePath,
                ExpiryDate,
                VerificationStatus,
                ReviewReason,
                isCurrent,
                supersededOnUtc: null,
                // DateOnly.MinValue passes the new-expiry guard because the
                // stored values are already accepted; CreatedOnUtc is the
                // honest time.
                DateOnly.MinValue,
                CreatedOnUtc);

        _versions.Add(legacy);
    }

    /// <summary>
    /// Appends a replacement upload as a new pending version. Any earlier
    /// pending version becomes retained history; the current approved version
    /// (and therefore the caregiver's effective eligibility) is untouched.
    /// </summary>
    internal void AddReplacementVersion(
        string filePath,
        DateOnly? expiryDate,
        DateOnly currentDate,
        DateTime utcNow)
    {
        EnsureCurrentVersionMaterialized();

        PendingVersion?.Supersede(utcNow);

        CaregiverCertificateVersion replacement =
            CaregiverCertificateVersion.Create(
                filePath,
                expiryDate,
                CertificateVerificationStatus.Pending,
                reviewReason: null,
                isCurrent: false,
                supersededOnUtc: null,
                currentDate,
                utcNow);

        _versions.Add(replacement);
        SyncEffectiveSnapshot();
        Touch(utcNow);
    }

    /// <summary>
    /// First half of an approval promotion: materialize a legacy current,
    /// validate the target is the reviewable pending version, and retire the
    /// previous current. The caller persists this before
    /// <see cref="CompletePromotion"/> so the partial-unique "one current"
    /// index never sees two current rows in a single statement batch.
    /// </summary>
    internal void BeginPromotion(
        CaregiverCertificateVersionId versionId,
        DateTime utcNow)
    {
        EnsureCurrentVersionMaterialized();

        CaregiverCertificateVersion target = GetVersion(versionId);

        if (target.IsRemoved || target.IsHistory)
        {
            throw new DomainException(
                "The Certificate version is not pending review.");
        }

        if (target.VerificationStatus != CertificateVerificationStatus.Pending)
        {
            throw new DomainException(
                "Only a Pending Certificate version can be verified.");
        }

        CaregiverCertificateVersion? current = CurrentVersion;

        if (current is not null && current.Id != target.Id)
        {
            current.Demote(utcNow);
        }

        Touch(utcNow);
    }

    /// <summary>
    /// Second half of an approval promotion: mark the target as the single
    /// current version and mirror it onto the effective columns.
    /// </summary>
    internal void CompletePromotion(
        CaregiverCertificateVersionId versionId,
        DateTime utcNow)
    {
        EnsureCurrentVersionMaterialized();

        CaregiverCertificateVersion target = GetVersion(versionId);
        target.Promote(utcNow);

        SyncEffectiveSnapshot();
        Touch(utcNow);
    }

    /// <summary>
    /// Rejects the newest pending version (or a specific pending version when
    /// one is supplied). The previous approved content is preserved; only when
    /// nothing valid is approved does the effective snapshot fall back to the
    /// rejected version so the mandatory-safety consequence still applies.
    /// Returns the rejected version.
    /// </summary>
    internal CaregiverCertificateVersion RejectPendingVersion(
        CaregiverCertificateVersionId? versionId,
        string reason,
        DateTime utcNow)
    {
        EnsureCurrentVersionMaterialized();

        CaregiverCertificateVersion? target = versionId.HasValue
            ? _versions.SingleOrDefault(
                version => version.Id == versionId.Value)
            : PendingVersion;

        if (target is null || target.IsHistory || target.IsRemoved ||
            target.VerificationStatus != CertificateVerificationStatus.Pending)
        {
            throw new DomainException(
                "The Certificate version is not pending review.");
        }

        target.Reject(reason, utcNow);

        SyncEffectiveSnapshot(preferredFallback: target);
        Touch(utcNow);

        return target;
    }

    /// <summary>
    /// Revokes the current approved version. Revocation is terminal for the
    /// qualification (no resurrection) and leaves every version as retained
    /// history.
    /// </summary>
    internal void RevokeCurrentVersion(
        string reason,
        DateTime utcNow)
    {
        EnsureCurrentVersionMaterialized();

        CaregiverCertificateVersion? current = CurrentVersion;

        if (current is null)
        {
            throw new DomainException(
                "The Certificate has no approved version to revoke.");
        }

        current.Revoke(reason, utcNow);

        SyncEffectiveSnapshot();
        Touch(utcNow);
    }

    /// <summary>
    /// Logically removes one retained-history version. Returns <c>false</c>
    /// (a no-op) when the version was already removed, so a repeated request is
    /// idempotent. The current effective version, the live pending candidate and
    /// the version backing the effective snapshot (see
    /// <see cref="CanRemoveVersion"/>) are refused: only genuinely superseded
    /// history can be removed.
    /// </summary>
    internal bool RemoveHistoryVersion(
        CaregiverCertificateVersionId versionId,
        UserId removedByUserId,
        DateTime utcNow)
    {
        EnsureCurrentVersionMaterialized();

        CaregiverCertificateVersion target = GetVersion(versionId);

        if (target.IsRemoved)
        {
            return false;
        }

        if (!CanRemoveVersion(target))
        {
            throw new DomainException(
                "Only a superseded Certificate history version that does not " +
                "back the effective snapshot can be removed.");
        }

        target.Remove(removedByUserId, utcNow);
        Touch(utcNow);

        return true;
    }

    internal void MarkRemoved(DateTime utcNow)
    {
        ValidateUtc(utcNow);

        if (IsRemoved)
        {
            throw new DomainException(
                "The Certificate is already removed.");
        }

        IsRemoved = true;
        RemovedOnUtc = utcNow;
        Touch(utcNow);
    }

    /// <summary>
    /// Whether a valid, approved, unexpired current version still backs the
    /// caregiver's mandatory qualification. Pending replacements never count.
    /// </summary>
    internal bool HasValidCurrentVersion(DateOnly currentDate)
    {
        EnsureCurrentVersionMaterialized();

        CaregiverCertificateVersion? current = CurrentVersion;

        return current is not null &&
            !current.IsRemoved &&
            current.VerificationStatus ==
                CertificateVerificationStatus.Verified &&
            !current.IsExpired(currentDate);
    }

    public CertificateExpiryStatus ExpiryStatus(DateOnly currentDate)
    {
        CaregiverCertificateVersion? effective =
            CurrentVersion ?? PendingVersion;

        if (effective is not null)
        {
            return effective.ExpiryStatus(currentDate);
        }

        // Legacy row with no materialized version: derive from the columns.
        if (!ExpiryDate.HasValue)
        {
            return CertificateExpiryStatus.None;
        }

        return ExpiryDate.Value < currentDate
            ? CertificateExpiryStatus.Expired
            : CertificateExpiryStatus.Valid;
    }

    /// <summary>
    /// Whether this qualification may be shown on a public discovery surface:
    /// it is live (not tombstoned) and still backed by a Verified, unexpired
    /// effective version. This is a pure read — a legacy row with no
    /// materialized version is judged from its own effective columns and is
    /// never mutated by a public read.
    /// </summary>
    public bool IsPubliclyUsable(DateOnly currentDate)
    {
        if (IsRemoved)
        {
            return false;
        }

        CaregiverCertificateVersion? current = CurrentVersion;

        if (current is not null)
        {
            return current.VerificationStatus ==
                    CertificateVerificationStatus.Verified &&
                !current.IsExpired(currentDate);
        }

        return VerificationStatus ==
                CertificateVerificationStatus.Verified &&
            !(ExpiryDate.HasValue && ExpiryDate.Value < currentDate);
    }

    internal CaregiverCertificateVersion GetVersion(
        CaregiverCertificateVersionId versionId)
    {
        if (versionId == CaregiverCertificateVersionId.Empty)
        {
            throw new DomainException(
                "Certificate version ID is required.");
        }

        CaregiverCertificateVersion? version =
            _versions.SingleOrDefault(
                candidate => candidate.Id == versionId);

        if (version is null)
        {
            throw new DomainException(
                "Certificate version was not found.");
        }

        return version;
    }

    private void SyncEffectiveSnapshot(
        CaregiverCertificateVersion? preferredFallback = null)
    {
        CaregiverCertificateVersion? effective =
            CurrentVersion ?? preferredFallback ?? PendingVersion;

        if (effective is null)
        {
            return;
        }

        FilePath = effective.FilePath;
        ExpiryDate = effective.ExpiryDate;
        VerificationStatus = effective.VerificationStatus;
        ReviewReason = effective.ReviewReason;
    }

    private void Touch(DateTime utcNow)
    {
        Revision++;
        UpdatedOnUtc = utcNow;
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
