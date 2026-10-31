using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Families.Domain.CareRecords;

/// <summary>
/// One care-record entry about an elderly dependent, created by one of the
/// three locked sources (Family, CareHome, Caregiver).
/// <para>
/// Server attribution is authoritative: the caller can never choose the
/// source, the family, the caregiver, the facility or the dependent binding —
/// each is supplied by the application layer after it has authorized the
/// actor, and it is immutable afterwards. <see cref="AuthorFacilityId"/> is
/// the care-home facility id from the CareHomes module stored as an opaque
/// Guid, the same pattern Booking uses for its Caregivers-module
/// BookingAreaId/BookingServiceId.
/// </para>
/// <para>
/// <see cref="Version"/> is the optimistic-concurrency token for full
/// author CRUD (locked D2), following the house IsConcurrencyToken pattern
/// used by <c>FamilySubscriptionConfiguration.LifecycleVersion</c>.
/// <see cref="UpdatedOnUtc"/> is null until the first successful update.
/// </para>
/// </summary>
public sealed class CareRecord : AggregateRoot<Guid>
{
    public const int MaximumHospitalNameLength = 200;
    public const int MaximumDetailsLength = 4000;

    private CareRecord()
    {
    }

    private CareRecord(
        Guid id,
        ElderlyId elderlyId,
        CareRecordSource source,
        UserId authorUserId,
        FamilyId? authorFamilyId,
        Guid? authorFacilityId,
        CaregiverId? authorCaregiverId,
        string hospitalName,
        DateOnly visitDate,
        string details,
        DateTime createdOnUtc)
        : base(id)
    {
        if (elderlyId == ElderlyId.Empty)
            throw new DomainException("The elderly dependent is required.");
        if (authorUserId == UserId.Empty)
            throw new DomainException("The care-record author is required.");

        EnsureExactlyOneAuthor(source, authorFamilyId, authorFacilityId, authorCaregiverId);

        ElderlyId = elderlyId;
        Source = source;
        AuthorUserId = authorUserId;
        AuthorFamilyId = authorFamilyId;
        AuthorFacilityId = authorFacilityId;
        AuthorCaregiverId = authorCaregiverId;
        HospitalName = NormalizeRequired(hospitalName, MaximumHospitalNameLength, "Hospital name");
        VisitDate = visitDate;
        Details = NormalizeDetails(details);
        Version = 1;
        CreatedOnUtc = createdOnUtc.Kind == DateTimeKind.Utc ? createdOnUtc : throw new DomainException("Care-record timestamps must be UTC.");
        UpdatedOnUtc = null;
    }

    public ElderlyId ElderlyId { get; private set; }

    public CareRecordSource Source { get; private set; }

    public UserId AuthorUserId { get; private set; }

    public FamilyId? AuthorFamilyId { get; private set; }

    /// <summary>Care-homes facility id (opaque cross-module id), Family and Caregiver records only.</summary>
    public Guid? AuthorFacilityId { get; private set; }

    public CaregiverId? AuthorCaregiverId { get; private set; }

    public string HospitalName { get; private set; } = default!;

    /// <summary>Date-only visit day, never localized or converted.</summary>
    public DateOnly VisitDate { get; private set; }

    public string Details { get; private set; } = default!;

    public int Version { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime? UpdatedOnUtc { get; private set; }

    public static CareRecord CreateForFamily(
        ElderlyId elderlyId,
        UserId authorUserId,
        FamilyId authorFamilyId,
        string hospitalName,
        DateOnly visitDate,
        string details,
        DateTime utcNow)
        => new(
            Guid.CreateVersion7(),
            elderlyId,
            CareRecordSource.Family,
            authorUserId,
            authorFamilyId,
            authorFacilityId: null,
            authorCaregiverId: null,
            hospitalName,
            visitDate,
            details,
            utcNow);

    public static CareRecord CreateForCaregiver(
        ElderlyId elderlyId,
        UserId authorUserId,
        CaregiverId authorCaregiverId,
        string hospitalName,
        DateOnly visitDate,
        string details,
        DateTime utcNow)
        => new(
            Guid.CreateVersion7(),
            elderlyId,
            CareRecordSource.Caregiver,
            authorUserId,
            authorFamilyId: null,
            authorFacilityId: null,
            authorCaregiverId,
            hospitalName,
            visitDate,
            details,
            utcNow);

    public static CareRecord CreateForCareHome(
        ElderlyId elderlyId,
        UserId authorUserId,
        Guid authorFacilityId,
        string hospitalName,
        DateOnly visitDate,
        string details,
        DateTime utcNow)
    {
        if (authorFacilityId == Guid.Empty)
            throw new DomainException("The authoring care-home facility is required.");

        return new CareRecord(
            Guid.CreateVersion7(),
            elderlyId,
            CareRecordSource.CareHome,
            authorUserId,
            authorFamilyId: null,
            authorFacilityId,
            authorCaregiverId: null,
            hospitalName,
            visitDate,
            details,
            utcNow);
    }

    /// <summary>
    /// Applies an author-source edit. The source and every attribution field
    /// are deliberately immutable; only the visit content and the audit
    /// timestamps move.
    /// </summary>
    public void Update(
        string hospitalName,
        DateOnly visitDate,
        string details,
        DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new DomainException("Care-record timestamps must be UTC.");

        HospitalName = NormalizeRequired(hospitalName, MaximumHospitalNameLength, "Hospital name");
        VisitDate = visitDate;
        Details = NormalizeDetails(details);
        Version = checked(Version + 1);
        UpdatedOnUtc = utcNow;
    }

    private static void EnsureExactlyOneAuthor(
        CareRecordSource source,
        FamilyId? authorFamilyId,
        Guid? authorFacilityId,
        CaregiverId? authorCaregiverId)
    {
        switch (source)
        {
            case CareRecordSource.Family:
                if (authorFamilyId is null || authorFamilyId == FamilyId.Empty ||
                    authorFacilityId is not null || authorCaregiverId is not null)
                    throw new DomainException("A family care record must name exactly its authoring family.");
                return;
            case CareRecordSource.Caregiver:
                if (authorCaregiverId is null || authorCaregiverId == CaregiverId.Empty ||
                    authorFamilyId is not null || authorFacilityId is not null)
                    throw new DomainException("A caregiver care record must name exactly its authoring caregiver.");
                return;
            case CareRecordSource.CareHome:
                if (authorFacilityId is null || authorFacilityId == Guid.Empty ||
                    authorFamilyId is not null || authorCaregiverId is not null)
                    throw new DomainException("A care-home care record must name exactly its authoring facility.");
                return;
            default:
                throw new DomainException("The care-record source is invalid.");
        }
    }

    private static string NormalizeRequired(
        string value,
        int maxLength,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"{fieldName} is required.");

        string trimmed = value.Trim();

        if (trimmed.Length > maxLength)
            throw new DomainException($"{fieldName} cannot exceed {maxLength} characters.");

        return trimmed;
    }

    /// <summary>
    /// Required, trimmed, at most <see cref="MaximumDetailsLength"/> plain-text
    /// characters: control characters other than tab/newline/carriage return
    /// are rejected so the field cannot smuggle binary payloads.
    /// </summary>
    private static string NormalizeDetails(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("Care-record details are required.");

        string trimmed = value.Trim();

        if (trimmed.Length > MaximumDetailsLength)
            throw new DomainException($"Care-record details cannot exceed {MaximumDetailsLength} characters.");

        if (trimmed.Any(ch => char.IsControl(ch) && ch is not ('\t' or '\n' or '\r')))
            throw new DomainException("Care-record details must be plain text.");

        return trimmed;
    }
}
