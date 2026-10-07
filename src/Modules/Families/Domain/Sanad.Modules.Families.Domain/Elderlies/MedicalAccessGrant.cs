using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Elderlies;

public sealed class MedicalAccessGrant : Entity<MedicalAccessGrantId>
{
    public const int MaximumNotesLength = 500;

    private MedicalAccessGrant() { }

    private MedicalAccessGrant(
        MedicalAccessGrantId id,
        ElderlyId dependentId,
        UserId grantedByUserId,
        UserId granteeUserId,
        MedicalAccessGrantType grantType,
        bool canViewRecords,
        bool canEditRecords,
        bool canShareWithOthers,
        DateTime grantedOnUtc,
        DateTime? expiresOnUtc,
        string? notes)
        : base(id)
    {
        if (dependentId == ElderlyId.Empty) throw new DomainException("Dependent ID is required.");
        if (grantedByUserId == UserId.Empty) throw new DomainException("Granting user is required.");
        if (granteeUserId == UserId.Empty) throw new DomainException("Grantee user is required.");
        if (!Enum.IsDefined(grantType)) throw new DomainException("Grant type is invalid.");
        if (!canViewRecords && (canEditRecords || canShareWithOthers))
            throw new DomainException("Editing or sharing requires view access.");
        if (grantedOnUtc.Kind != DateTimeKind.Utc || (expiresOnUtc.HasValue && expiresOnUtc.Value.Kind != DateTimeKind.Utc))
            throw new DomainException("Grant timestamps must be UTC.");
        if (expiresOnUtc.HasValue && expiresOnUtc.Value <= grantedOnUtc)
            throw new DomainException("Grant expiry must be after its creation time.");

        DependentId = dependentId;
        GrantedByUserId = grantedByUserId;
        GranteeUserId = granteeUserId;
        GrantType = grantType;
        CanViewRecords = canViewRecords;
        CanEditRecords = canEditRecords;
        CanShareWithOthers = canShareWithOthers;
        GrantedOnUtc = grantedOnUtc;
        ExpiresOnUtc = expiresOnUtc;
        Notes = NormalizeNotes(notes);
    }

    public ElderlyId DependentId { get; private set; }
    public UserId GrantedByUserId { get; private set; }
    public UserId GranteeUserId { get; private set; }
    public MedicalAccessGrantType GrantType { get; private set; }
    public bool CanViewRecords { get; private set; }
    public bool CanEditRecords { get; private set; }
    public bool CanShareWithOthers { get; private set; }
    public DateTime GrantedOnUtc { get; private set; }
    public DateTime? ExpiresOnUtc { get; private set; }
    public string? Notes { get; private set; }
    public DateTime? RevokedOnUtc { get; private set; }
    public UserId? RevokedByUserId { get; private set; }
    public bool IsActive => RevokedOnUtc is null && (!ExpiresOnUtc.HasValue || ExpiresOnUtc.Value > DateTime.UtcNow);

    public static MedicalAccessGrant Create(
        ElderlyId dependentId,
        UserId grantedByUserId,
        UserId granteeUserId,
        MedicalAccessGrantType grantType,
        bool canViewRecords,
        bool canEditRecords,
        bool canShareWithOthers,
        DateTime grantedOnUtc,
        DateTime? expiresOnUtc = null,
        string? notes = null) => new(
            MedicalAccessGrantId.New(), dependentId, grantedByUserId, granteeUserId, grantType,
            canViewRecords, canEditRecords, canShareWithOthers,
            grantedOnUtc, expiresOnUtc, notes);

    [Obsolete("Specify the named grantee explicitly.")]
    public static MedicalAccessGrant Create(
        ElderlyId dependentId,
        UserId grantedByUserId,
        MedicalAccessGrantType grantType,
        bool canViewRecords,
        bool canEditRecords,
        bool canShareWithOthers,
        DateTime grantedOnUtc,
        DateTime? expiresOnUtc = null,
        string? notes = null) =>
        Create(dependentId, grantedByUserId, grantedByUserId, grantType,
            canViewRecords, canEditRecords, canShareWithOthers,
            grantedOnUtc, expiresOnUtc, notes);

    public void Revoke(UserId revokedByUserId, DateTime revokedOnUtc)
    {
        if (revokedByUserId == UserId.Empty) throw new DomainException("Revoking user is required.");
        if (revokedOnUtc.Kind != DateTimeKind.Utc) throw new DomainException("Revocation time must be UTC.");
        if (RevokedOnUtc.HasValue) throw new DomainException("This grant has already been revoked.");
        RevokedByUserId = revokedByUserId;
        RevokedOnUtc = revokedOnUtc;
    }

    private static string? NormalizeNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return null;
        string normalized = notes.Trim();
        if (normalized.Length > MaximumNotesLength)
            throw new DomainException($"Grant notes cannot exceed {MaximumNotesLength} characters.");
        return normalized;
    }
}

public enum MedicalAccessGrantType
{
    Full = 1,
    Limited = 2,
    Emergency = 3
}
