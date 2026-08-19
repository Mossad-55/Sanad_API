using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers.Events;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;
using Sanad.Modules.Caregivers.Domain.Caregivers.Selections;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class Caregiver : AggregateRoot<CaregiverId>
{
    public const int MaximumAreaSelections = 10;
    public const int MaximumAdditionalCertificates = 5;

    private readonly List<CaregiverServiceSelection> _serviceSelections = [];
    private readonly List<CaregiverLanguageSelection> _languageSelections = [];
    private readonly List<CaregiverAreaSelection> _areaSelections = [];

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
    public IReadOnlyCollection<CaregiverAreaSelection> AreaSelections => _areaSelections.AsReadOnly();  

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
        CaregiverCertificateType type,
        string filePath,
        DateOnly? expiryDate,
        DateOnly currentDate)
    {
        if (Type != CaregiverType.Medical)
        {
            throw new DomainException(
                "Only Medical caregivers can add professional certificates.");
        }

        if (type == CaregiverCertificateType.PracticeLicense)
        {
            bool alreadyHasPracticeLicense =
                _certificates.Any(
                    certificate =>
                        certificate.Type ==
                        CaregiverCertificateType.PracticeLicense);

            if (alreadyHasPracticeLicense)
            {
                throw new DomainException(
                    "The caregiver already has a Practice License.");
            }
        }

        if (type == CaregiverCertificateType.GraduationCertificate)
        {
            bool alreadyHasGraduationCertificate =
                _certificates.Any(
                    certificate =>
                        certificate.Type ==
                        CaregiverCertificateType.GraduationCertificate);

            if (alreadyHasGraduationCertificate)
            {
                throw new DomainException(
                    "The caregiver already has a Graduation Certificate.");
            }
        }

        if (type == CaregiverCertificateType.AdditionalCertificate)
        {
            int additionalCertificateCount =
                _certificates.Count(
                    certificate =>
                        certificate.Type ==
                        CaregiverCertificateType.AdditionalCertificate);

            if (additionalCertificateCount >=
                MaximumAdditionalCertificates)
            {
                throw new DomainException(
                    $"A caregiver cannot add more than " +
                    $"{MaximumAdditionalCertificates} " +
                    $"Additional Certificates.");
            }
        }

        _certificates.Add(
            CaregiverCertificate.Create(
                type,
                filePath,
                expiryDate,
                currentDate));

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void VerifyCertificate(CaregiverCertificateId certificateId)
    {
        CaregiverCertificate certificate = GetCertificate(certificateId);

        certificate.Verify();

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void RejectCertificate(CaregiverCertificateId certificateId, string reason)
    {
        CaregiverCertificate certificate = GetCertificate(certificateId);

        certificate.Reject(reason);

        if (IsMandatoryCertificate(certificate.Type))
        {
            Availability = CaregiverAvailability.Unavailable;
        }

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void RevokeCertificate(CaregiverCertificateId certificateId, string reason)
    {
        CaregiverCertificate certificate = GetCertificate(certificateId);

        certificate.Revoke(reason);

        if (IsMandatoryCertificate(certificate.Type))
        {
            Availability =
                CaregiverAvailability.Unavailable;
        }

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void UpdateCertificateFile(
        CaregiverCertificateId certificateId,
        string filePath,
        DateOnly? expiryDate,
        DateOnly currentDate)
    {
        CaregiverCertificate certificate =
            GetCertificate(certificateId);

        certificate.UpdateFile(
            filePath,
            expiryDate,
            currentDate);

        if (IsMandatoryCertificate(certificate.Type))
        {
            Availability = CaregiverAvailability.Unavailable;
        }

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void RemoveCertificate(CaregiverCertificateId certificateId)
    {
        CaregiverCertificate certificate = GetCertificate(certificateId);

        if (IsMandatoryCertificate(
            certificate.Type))
        {
            throw new DomainException(
                "A mandatory Certificate cannot be removed. " +
                "Replace its file instead.");
        }

        _certificates.Remove(certificate);
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

    public void SelectArea(Area area)
    {
        ArgumentNullException.ThrowIfNull(area);

        if (!area.IsActive)
        {
            throw new DomainException(
                "Cannot select an inactive area.");
        }

        bool alreadySelected =
            _areaSelections.Any(
                selection =>
                    selection.Id == area.Id);

        if (alreadySelected)
        {
            throw new DomainException(
                "The area is already selected.");
        }

        if (_areaSelections.Count >=
            MaximumAreaSelections)
        {
            throw new DomainException(
                $"A caregiver cannot select more than " + $"{MaximumAreaSelections} areas.");
        }

        _areaSelections.Add(
            CaregiverAreaSelection.Create(area.Id));

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void RemoveArea(AreaId areaId)
    {
        if (areaId == AreaId.Empty)
        {
            throw new DomainException(
                "Area ID is required.");
        }

        CaregiverAreaSelection? selection =
            _areaSelections.SingleOrDefault(
                selection =>
                    selection.Id == areaId);

        if (selection is null)
        {
            throw new DomainException(
                "The area is not selected.");
        }

        bool isRemovingFinalArea =
            _areaSelections.Count == 1;

        if (Status == CaregiverStatus.Active &&
            isRemovingFinalArea)
        {
            throw new DomainException(
                "An active caregiver must have at least one area.");
        }

        _areaSelections.Remove(selection);
        UpdatedOnUtc = DateTime.UtcNow;
    }

    private CaregiverCertificate GetCertificate(CaregiverCertificateId certificateId)
    {
         if (certificateId == CaregiverCertificateId.Empty)
        {
            throw new DomainException(
                "Certificate ID is required.");
        }

        CaregiverCertificate? certificate =
            _certificates.SingleOrDefault(
                certificate =>
                    certificate.Id ==
                    certificateId);

        if (certificate is null)
        {
            throw new DomainException(
                "Certificate was not found.");
        }

        return certificate;
    }

    private static bool IsMandatoryCertificate(CaregiverCertificateType type)
    {
        return type is
            CaregiverCertificateType.PracticeLicense or
            CaregiverCertificateType.GraduationCertificate;
    }
}