using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers.Events;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;
using Sanad.Modules.Caregivers.Domain.Caregivers.Selections;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class Caregiver : AggregateRoot<CaregiverId>
{
    private readonly List<CaregiverServiceSelection> _serviceSelections = [];
    private readonly List<CaregiverLanguageSelection> _languageSelections = [];

    private Caregiver()
    {
    }

    private Caregiver(
        CaregiverId id,
        UserId userId,
        CaregiverType type)
        : base(id)
    {
        UserId = userId;
        Type = type;
        Status = CaregiverStatus.PendingVerification;
        Availability = CaregiverAvailability.Unavailable;
        CreatedOnUtc = DateTime.UtcNow;
        UpdatedOnUtc = DateTime.UtcNow;

        RaiseDomainEvent(
            new CaregiverCreatedDomainEvent(
                Id,
                UserId));
    }

    public UserId UserId { get; private set; }
    public CaregiverProfile? Profile { get; private set; }
    public CaregiverType Type { get; private set; }
    public CaregiverStatus Status { get; private set; }
    public CaregiverAvailability Availability { get; private set; }
    public CaregiverPricing? Pricing { get; private set; }
    public CaregiverSchedule Schedule { get; private set; } = CaregiverSchedule.Create();
    public decimal AverageRating { get; private set; } = 0;
    public int ReviewsCount { get; private set; } = 0;
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    private readonly List<CaregiverCertificate> _certificates = [];
    public IReadOnlyCollection<CaregiverCertificate> Certificates => _certificates.AsReadOnly();
    public IReadOnlyCollection<CaregiverServiceSelection> ServiceSelections => _serviceSelections.AsReadOnly();
    public IReadOnlyCollection<CaregiverLanguageSelection> LanguageSelections => _languageSelections.AsReadOnly();    

    public static Caregiver Create(
        UserId userId,
        CaregiverType type)
    {
        return new Caregiver(
            CaregiverId.New(),
            userId,
            type);
    }

    public void UpdateProfile(
    string bio,
    int yearsOfExperience)
    {
        Profile = CaregiverProfile.Create(
            bio,
            yearsOfExperience);

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Activate()
    {
        Status = CaregiverStatus.Active;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Suspend()
    {
        Status = CaregiverStatus.Suspended;
        UpdatedOnUtc = DateTime.UtcNow;
    }
    public void BecomeAvailable()
    {
        Availability = CaregiverAvailability.Available;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void BecomeUnavailable()
    {
        Availability = CaregiverAvailability.Unavailable;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void UpdatePricing(CaregiverPricing pricing)
    {
        Pricing = pricing;

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void AddCertificate(
        string name,
        string filePath,
        DateOnly? expiryDate = null)
    {
        _certificates.Add(
            CaregiverCertificate.Create(
                name,
                filePath,
                expiryDate));

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void SelectService(Service service)
    {
        ArgumentNullException.ThrowIfNull(service);

        if (!service.IsActive)
        {
            throw new DomainException(
                "Cannot select an inactive service."
            );
        }

        if(service.CaregiverType != Type)
        {
            throw new DomainException(
                "The service does not support this caregiver type."
            );
        }

        bool alreadySelected = _serviceSelections.Any(
            selection => selection.Id == service.Id
        );

        if (alreadySelected)
        {
            throw new DomainException(
                "The service is already selected."
            );
        }

        _serviceSelections.Add(CaregiverServiceSelection.Create(service.Id));

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void RemoveService(ServiceId serviceId)
    {
        if(serviceId == ServiceId.Empty)
        {
            throw new DomainException(
                "Service ID is required."
            );
        }

        CaregiverServiceSelection? selection = _serviceSelections.SingleOrDefault(
            selection => selection.Id == serviceId
        );

        if(selection is null)
        {
            throw new DomainException(
                "The service is not selected."
            );
        }

        bool isRemovingFinalService = _serviceSelections.Count == 1;

        if(Status == CaregiverStatus.Active &&
            isRemovingFinalService)
        {
            throw new DomainException(
                "An active caregiver must have at least one service."
            );
        }

        _serviceSelections.Remove(selection);

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void SelectLanguage(Language language)
    {
        ArgumentNullException.ThrowIfNull(language);

        if (!language.IsActive)
        {
            throw new DomainException(
                "Cannot select an inactive language."
            );
        }

        bool alreadySelected = _languageSelections.Any(
            selection => selection.Id == language.Id
        );

        if (alreadySelected)
        {
            throw new DomainException(
                "The language is already selected."
            );
        }

        _languageSelections.Add(CaregiverLanguageSelection.Create(language.Id));

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void RemoveLanguage(LanguageId languageId)
    {
        if(languageId == LanguageId.Empty)
        {
            throw new DomainException(
                "Language ID is required."
            );
        }

        CaregiverLanguageSelection? selection =
        _languageSelections.SingleOrDefault(
            selection =>
                selection.Id == languageId);

        if (selection is null)
        {
            throw new DomainException(
                "The language is not selected.");
        }

        bool isRemovingFinalLanguage =
            _languageSelections.Count == 1;

        if (Status == CaregiverStatus.Active &&
            isRemovingFinalLanguage)
        {
            throw new DomainException(
                "An active caregiver must have at least one language.");
        }

        _languageSelections.Remove(selection);
        
        UpdatedOnUtc = DateTime.UtcNow;
    }
}