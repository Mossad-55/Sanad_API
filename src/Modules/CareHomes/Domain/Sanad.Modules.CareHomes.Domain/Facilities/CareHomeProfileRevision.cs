using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.CareHomes.Domain.Facilities;

public sealed record BilingualCareHomeItem(string Arabic, string English);

public sealed record CareHomeProfileDraft(
    string ArabicName,
    string EnglishName,
    string ArabicDescription,
    string EnglishDescription,
    string ContactName,
    string ContactPhone,
    string? ContactEmail,
    string Governorate,
    string City,
    string Area,
    string Address,
    string ArabicAdmissionConditions,
    string EnglishAdmissionConditions,
    IReadOnlyList<BilingualCareHomeItem> Amenities,
    IReadOnlyList<BilingualCareHomeItem> MedicalServices);

public sealed class CareHomeProfileRevision : Entity<Guid>
{
    private CareHomeProfileRevision()
    {
    }

    private CareHomeProfileRevision(
        Guid id,
        int revisionNumber,
        UserId createdByUserId,
        CareHomeProfileDraft draft,
        DateTime utcNow)
        : base(id)
    {
        RevisionNumber = revisionNumber;
        CreatedByUserId = createdByUserId;
        CreatedOnUtc = utcNow;
        UpdateDraft(draft, utcNow);
    }

    public int RevisionNumber { get; private set; }
    public UserId CreatedByUserId { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    public DateTime? SubmittedOnUtc { get; private set; }
    public DateTime? ApprovedOnUtc { get; private set; }
    public string ArabicName { get; private set; } = string.Empty;
    public string EnglishName { get; private set; } = string.Empty;
    public string ArabicDescription { get; private set; } = string.Empty;
    public string EnglishDescription { get; private set; } = string.Empty;
    public string ContactName { get; private set; } = string.Empty;
    public string ContactPhone { get; private set; } = string.Empty;
    public string? ContactEmail { get; private set; }
    public string Governorate { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string Area { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public string ArabicAdmissionConditions { get; private set; } = string.Empty;
    public string EnglishAdmissionConditions { get; private set; } = string.Empty;
    public IReadOnlyList<BilingualCareHomeItem> Amenities { get; private set; } = [];
    public IReadOnlyList<BilingualCareHomeItem> MedicalServices { get; private set; } = [];
    public bool IsFrozen => SubmittedOnUtc is not null;

    internal CareHomeProfileDraft ToDraft() => new(
        ArabicName, EnglishName, ArabicDescription, EnglishDescription, ContactName, ContactPhone,
        ContactEmail, Governorate, City, Area, Address, ArabicAdmissionConditions,
        EnglishAdmissionConditions, Amenities, MedicalServices);

    internal static CareHomeProfileRevision Create(
        int revisionNumber,
        UserId actorUserId,
        CareHomeProfileDraft draft,
        DateTime utcNow)
    {
        if (revisionNumber < 1)
            throw new DomainException("Care-home revision number must be positive.");
        if (actorUserId == UserId.Empty)
            throw new DomainException("Care-home profile actor is required.");
        EnsureUtc(utcNow);

        return new CareHomeProfileRevision(
            Guid.CreateVersion7(), revisionNumber, actorUserId, draft, utcNow);
    }

    internal void UpdateDraft(CareHomeProfileDraft draft, DateTime utcNow)
    {
        if (IsFrozen)
            throw new DomainException("A submitted care-home profile revision is frozen.");
        EnsureUtc(utcNow);
        // Drafts are intentionally allowed to be incomplete; submission is the
        // validation boundary for required public content.
        ArabicName = Optional(draft.ArabicName, 200, nameof(draft.ArabicName));
        EnglishName = Optional(draft.EnglishName, 200, nameof(draft.EnglishName));
        ArabicDescription = Optional(draft.ArabicDescription, 4000, nameof(draft.ArabicDescription));
        EnglishDescription = Optional(draft.EnglishDescription, 4000, nameof(draft.EnglishDescription));
        ContactName = Optional(draft.ContactName, 200, nameof(draft.ContactName));
        ContactPhone = Optional(draft.ContactPhone, 32, nameof(draft.ContactPhone));
        ContactEmail = Optional(draft.ContactEmail, 256, nameof(draft.ContactEmail));
        Governorate = Optional(draft.Governorate, 200, nameof(draft.Governorate));
        City = Optional(draft.City, 200, nameof(draft.City));
        Area = Optional(draft.Area, 200, nameof(draft.Area));
        Address = Optional(draft.Address, 1000, nameof(draft.Address));
        ArabicAdmissionConditions = Optional(draft.ArabicAdmissionConditions, 4000, nameof(draft.ArabicAdmissionConditions));
        EnglishAdmissionConditions = Optional(draft.EnglishAdmissionConditions, 4000, nameof(draft.EnglishAdmissionConditions));
        Amenities = NormalizeItems(draft.Amenities);
        MedicalServices = NormalizeItems(draft.MedicalServices);
        UpdatedOnUtc = utcNow;
    }

    internal bool IsReadyForSubmission() =>
        ArabicName.Length > 0 && EnglishName.Length > 0 &&
        ArabicDescription.Length > 0 && EnglishDescription.Length > 0 &&
        ArabicAdmissionConditions.Length > 0 && EnglishAdmissionConditions.Length > 0 &&
        ContactName.Length > 0 && ContactPhone.Length > 0 &&
        Governorate.Length > 0 && City.Length > 0 &&
        Area.Length > 0 && Address.Length > 0;

    internal void Freeze(DateTime utcNow)
    {
        if (!IsReadyForSubmission())
            throw new DomainException("Required bilingual facility profile and contact/address details are missing.");
        if (IsFrozen)
            throw new DomainException("This care-home profile revision has already been submitted.");
        EnsureUtc(utcNow);
        SubmittedOnUtc = utcNow;
    }

    internal void MarkApproved(DateTime utcNow)
    {
        if (!IsFrozen)
            throw new DomainException("Only a submitted revision can be approved.");
        EnsureUtc(utcNow);
        ApprovedOnUtc = utcNow;
    }

    private static IReadOnlyList<BilingualCareHomeItem> NormalizeItems(
        IReadOnlyList<BilingualCareHomeItem> items)
    {
        if (items.Count > 100)
            throw new DomainException("Care-home amenities and medical services are limited to 100 entries each.");
        if (items.Any(item => item is null))
            throw new DomainException("Care-home amenities and medical services cannot contain null entries.");
        return items.Select(item => new BilingualCareHomeItem(
                Required(item.Arabic, 200, nameof(item.Arabic)),
                Required(item.English, 200, nameof(item.English))))
            .ToArray();
    }

    private static string Required(string? value, int maximumLength, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new DomainException($"{field} is required.")
            : value.Trim().Length > maximumLength
                ? throw new DomainException($"{field} exceeds {maximumLength} characters.")
                : value.Trim();

    private static string Optional(string? value, int maximumLength, string field)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length > maximumLength
            ? throw new DomainException($"{field} exceeds {maximumLength} characters.")
            : normalized;
    }

    private static void EnsureUtc(DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new DomainException("Care-home profile timestamps must be UTC.");
    }
}
