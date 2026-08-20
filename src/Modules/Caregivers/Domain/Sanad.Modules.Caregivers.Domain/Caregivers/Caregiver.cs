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
    public const int MaximumDetailedAddressLength = 500;

    public string? DetailedAddress { get; private set; }

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
        Status = CaregiverStatus.Onboarding;
        Availability = CaregiverAvailability.Unavailable;

        if (type == CaregiverType.Companion)
        {
            CompanionSchedule = CompanionWeeklySchedule.Create();
        }

        if (type == CaregiverType.Medical)
        {
            MedicalSchedule = MedicalWeeklySchedule.Create();
        }

        CreatedOnUtc = DateTime.UtcNow;
        UpdatedOnUtc = DateTime.UtcNow;

        RaiseDomainEvent(
            new CaregiverCreatedDomainEvent(
                Id,
                UserId));
    }

    public UserId UserId { get; private set; }
    public MedicalCaregiverProfile? MedicalProfile { get; private set; }
    public CompanionCaregiverProfile? CompanionProfile { get; private set; }
    public CaregiverType Type { get; private set; }
    public CaregiverStatus Status { get; private set; }
    public string? StatusReason { get; private set; }
    public CaregiverAvailability Availability { get; private set; }
    public MedicalCaregiverPricing? MedicalPricing { get; private set; }
    public MedicalWeeklySchedule? MedicalSchedule { get; private set; }
    public CompanionCaregiverPricing? CompanionPricing { get; private set; }
    public CompanionWeeklySchedule? CompanionSchedule { get; private set; }
    public decimal AverageRating { get; private set; } = 0;
    public int ReviewsCount { get; private set; } = 0;
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    private readonly List<CaregiverCertificate> _certificates = [];
    public IReadOnlyCollection<CaregiverCertificate> Certificates => _certificates.AsReadOnly();
    public IReadOnlyCollection<CaregiverServiceSelection> ServiceSelections => _serviceSelections.AsReadOnly();
    public IReadOnlyCollection<CaregiverLanguageSelection> LanguageSelections => _languageSelections.AsReadOnly();
    public IReadOnlyCollection<CaregiverAreaSelection> AreaSelections => _areaSelections.AsReadOnly();

    internal void ValidateSubmissionReadiness(
        DateOnly currentDate)
    {
        EnsureRequiredSelections();

        switch (Type)
        {
            case CaregiverType.Medical:
                EnsureMedicalSubmissionReadiness(
                    currentDate);
                return;

            case CaregiverType.Companion:
                EnsureCompanionSubmissionReadiness();
                return;

            default:
                throw new DomainException(
                    "Caregiver type is invalid.");
        }
    }

    public static Caregiver Create(
        UserId userId,
        CaregiverType type)
    {
        return new Caregiver(
            CaregiverId.New(),
            userId,
            type);
    }

    public void UpdateMedicalProfile(
        ProfessionalTitle professionalTitle,
        int yearsOfExperience,
        Specialization specialization,
        AcademicDegree academicDegree,
        string? currentWorkplace,
        string? biography)
    {
        ArgumentNullException.ThrowIfNull(
            professionalTitle);

        ArgumentNullException.ThrowIfNull(
            specialization);

        ArgumentNullException.ThrowIfNull(
            academicDegree);

        if (Type != CaregiverType.Medical)
        {
            throw new DomainException(
                "Only a Medical caregiver can have " +
                "a Medical professional profile.");
        }

        if (!professionalTitle.IsActive)
        {
            throw new DomainException(
                "Professional Title is inactive.");
        }

        if (!specialization.IsActive)
        {
            throw new DomainException(
                "Specialization is inactive.");
        }

        if (specialization.CaregiverType !=
            CaregiverType.Medical)
        {
            throw new DomainException(
                "Specialization does not support " +
                "Medical caregivers.");
        }

        if (!academicDegree.IsActive)
        {
            throw new DomainException(
                "Academic Degree is inactive.");
        }

        MedicalCaregiverProfile profile =
            MedicalCaregiverProfile.Create(
                professionalTitle.Id,
                yearsOfExperience,
                specialization.Id,
                academicDegree.Id,
                currentWorkplace,
                biography);

        MedicalProfile = profile;

        if (Status == CaregiverStatus.Active)
        {
            Availability =
                CaregiverAvailability.Unavailable;
        }

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void UpdateCompanionProfile(
        int yearsOfExperience,
        Specialization specialization,
        string? biography)
    {
        ArgumentNullException.ThrowIfNull(
            specialization);

        if (Type != CaregiverType.Companion)
        {
            throw new DomainException(
                "Only a Companion caregiver can have " +
                "a Companion professional profile.");
        }

        if (!specialization.IsActive)
        {
            throw new DomainException(
                "Specialization is inactive.");
        }

        if (specialization.CaregiverType !=
            CaregiverType.Companion)
        {
            throw new DomainException(
                "Specialization does not support " +
                "Companion caregivers.");
        }

        CompanionCaregiverProfile profile =
            CompanionCaregiverProfile.Create(
                yearsOfExperience,
                specialization.Id,
                biography);

        CompanionProfile = profile;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void BecomeAvailable(DateOnly currentDate)
    {
        if (Status != CaregiverStatus.Active)
        {
            throw new DomainException(
                "Only an Active caregiver can become Available."
            );
        }

        if (Type == CaregiverType.Medical)
        {
            EnsureMandatoryCertificatesAreCompliant(currentDate);
        }

        Availability = CaregiverAvailability.Available;

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void BecomeUnavailable()
    {
        Availability = CaregiverAvailability.Unavailable;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void UpdateMedicalPricing(
        decimal homeVisitPrice,
        decimal eightHourShiftPrice,
        decimal twelveHourShiftPrice,
        decimal twentyFourHourShiftPrice)
    {
        if (Type != CaregiverType.Medical)
        {
            throw new DomainException(
                "Only a Medical caregiver can have " +
                "Medical pricing.");
        }

        MedicalCaregiverPricing pricing =
            MedicalCaregiverPricing.Create(
                homeVisitPrice,
                eightHourShiftPrice,
                twelveHourShiftPrice,
                twentyFourHourShiftPrice);

        MedicalPricing = pricing;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void UpdateCompanionPricing(
        decimal hourlyPrice,
        decimal eightHourDayPrice,
        decimal overnightPrice)
    {
        if (Type != CaregiverType.Companion)
        {
            throw new DomainException(
                "Only a Companion caregiver can have " +
                "Companion pricing.");
        }

        CompanionCaregiverPricing pricing =
            CompanionCaregiverPricing.Create(
                hourlyPrice,
                eightHourDayPrice,
                overnightPrice);

        CompanionPricing = pricing;
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

        if (service.CaregiverType != Type)
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
        if (serviceId == ServiceId.Empty)
        {
            throw new DomainException(
                "Service ID is required."
            );
        }

        CaregiverServiceSelection? selection = _serviceSelections.SingleOrDefault(
            selection => selection.Id == serviceId
        );

        if (selection is null)
        {
            throw new DomainException(
                "The service is not selected."
            );
        }

        bool isRemovingFinalService = _serviceSelections.Count == 1;

        if (Status == CaregiverStatus.Active &&
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
        if (languageId == LanguageId.Empty)
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

    public void UpdateDetailedAddress(
        string? detailedAddress)
    {
        string? normalizedAddress =
            NormalizeOptionalDetailedAddress(
                detailedAddress);

        DetailedAddress = normalizedAddress;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void AddCompanionAvailabilityWindow(
        CompanionBookingType bookingType,
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        EnsureCompanionCaregiver();

        CompanionWeeklySchedule currentSchedule =
            CompanionSchedule ??
            CompanionWeeklySchedule.Create();

        CompanionWeeklySchedule updatedSchedule =
            currentSchedule.AddWindow(
                bookingType,
                dayOfWeek,
                startTime,
                endTime);

        CompanionSchedule = updatedSchedule;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void RemoveCompanionAvailabilityWindow(
        CompanionBookingType bookingType,
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        EnsureCompanionCaregiver();

        CompanionWeeklySchedule currentSchedule =
            CompanionSchedule ??
            CompanionWeeklySchedule.Create();

        CompanionWeeklySchedule updatedSchedule =
            currentSchedule.RemoveWindow(
                bookingType,
                dayOfWeek,
                startTime,
                endTime);

        if (Status == CaregiverStatus.Active &&
            !updatedSchedule.HasAvailability)
        {
            throw new DomainException(
                "An Active caregiver must have " +
                "at least one availability window.");
        }

        CompanionSchedule = updatedSchedule;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void AddMedicalShift(
        DayOfWeek dayOfWeek,
        MedicalShiftType shiftType
    )
    {
        EnsureMedicalCaregiver();

        MedicalWeeklySchedule currentSchedule =
            MedicalSchedule ??
            MedicalWeeklySchedule.Create();

        MedicalWeeklySchedule updatedSchedule =
            currentSchedule.AddShift(
                dayOfWeek,
                shiftType);

        MedicalSchedule = updatedSchedule;

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void RemoveMedicalShift(
        DayOfWeek dayOfWeek,
        MedicalShiftType shiftType)
    {
        EnsureMedicalCaregiver();

        MedicalWeeklySchedule currentSchedule =
            MedicalSchedule ??
            MedicalWeeklySchedule.Create();

        MedicalWeeklySchedule updatedSchedule =
            currentSchedule.RemoveShift(
                dayOfWeek,
                shiftType);

        EnsureActiveScheduleRemainsAvailable(
            updatedSchedule.HasAvailability);

        MedicalSchedule = updatedSchedule;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void AddMedicalHomeVisitWindow(
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        EnsureMedicalCaregiver();

        MedicalWeeklySchedule currentSchedule =
            MedicalSchedule ??
            MedicalWeeklySchedule.Create();

        MedicalWeeklySchedule updatedSchedule =
            currentSchedule.AddHomeVisitWindow(
                dayOfWeek,
                startTime,
                endTime);

        MedicalSchedule = updatedSchedule;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void RemoveMedicalHomeVisitWindow(
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        EnsureMedicalCaregiver();

        MedicalWeeklySchedule currentSchedule =
            MedicalSchedule ??
            MedicalWeeklySchedule.Create();

        MedicalWeeklySchedule updatedSchedule =
            currentSchedule.RemoveHomeVisitWindow(
                dayOfWeek,
                startTime,
                endTime);

        EnsureActiveScheduleRemainsAvailable(
            updatedSchedule.HasAvailability);

        MedicalSchedule = updatedSchedule;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void SubmitForReview(
        DateTime utcNow,
        DateOnly currentDate
    )
    {
        EnsureStatus(
            CaregiverStatus.Onboarding,
            "Only an Onboarding caregiver can submit for review."
        );

        ValidateUtc(utcNow);

        ValidateSubmissionReadiness(currentDate);

        Status = CaregiverStatus.PendingReview;
        StatusReason = null;
        Availability = CaregiverAvailability.Unavailable;

        UpdatedOnUtc = utcNow;
    }

    public void RequestCorrection(
        string reason,
        DateTime utcNow)
    {
        EnsureStatus(
            CaregiverStatus.PendingReview,
            "Only a Pending Review caregiver can need correction.");

        ValidateUtc(utcNow);

        string normalizedReason =
            NormalizeRequiredStatusReason(
                reason,
                "Correction reason");

        Status = CaregiverStatus.NeedsCorrection;
        StatusReason = normalizedReason;
        Availability =
            CaregiverAvailability.Unavailable;

        UpdatedOnUtc = utcNow;
    }

    public void ResubmitForReview(
        DateTime utcNow,
        DateOnly currentDate)
    {
        EnsureStatus(
            CaregiverStatus.NeedsCorrection,
            "Only a Needs Correction caregiver can resubmit.");

        ValidateUtc(utcNow);

        ValidateSubmissionReadiness(currentDate);

        Status = CaregiverStatus.PendingReview;
        StatusReason = null;
        Availability =
            CaregiverAvailability.Unavailable;

        UpdatedOnUtc = utcNow;
    }

    public void Approve(
        DateTime utcNow)
    {
        EnsureStatus(
            CaregiverStatus.PendingReview,
            "Only a Pending Review caregiver can be approved.");

        ValidateUtc(utcNow);

        Status = CaregiverStatus.Active;
        StatusReason = null;

        // Approval does not automatically accept work.
        Availability =
            CaregiverAvailability.Unavailable;

        UpdatedOnUtc = utcNow;
    }

    public void RejectApplication(
        string reason,
        DateTime utcNow)
    {
        EnsureStatus(
            CaregiverStatus.PendingReview,
            "Only a Pending Review caregiver can be rejected.");

        ValidateUtc(utcNow);

        string normalizedReason =
            NormalizeRequiredStatusReason(
                reason,
                "Rejection reason");

        Status = CaregiverStatus.Rejected;
        StatusReason = normalizedReason;
        Availability =
            CaregiverAvailability.Unavailable;

        UpdatedOnUtc = utcNow;
    }

    public void Suspend(
        string reason,
        DateTime utcNow)
    {
        EnsureStatus(
            CaregiverStatus.Active,
            "Only an Active caregiver can be suspended.");

        ValidateUtc(utcNow);

        string normalizedReason =
            NormalizeRequiredStatusReason(
                reason,
                "Suspension reason");

        Status = CaregiverStatus.Suspended;
        StatusReason = normalizedReason;
        Availability =
            CaregiverAvailability.Unavailable;

        UpdatedOnUtc = utcNow;
    }

    public void Reactivate(
        DateTime utcNow)
    {
        EnsureStatus(
            CaregiverStatus.Suspended,
            "Only a Suspended caregiver can be reactivated.");

        ValidateUtc(utcNow);

        Status = CaregiverStatus.Active;
        StatusReason = null;

        // Admin reactivation does not override
        // the caregiver's work-availability choice.
        Availability =
            CaregiverAvailability.Unavailable;

        UpdatedOnUtc = utcNow;
    }

    private static bool IsMandatoryCertificate(CaregiverCertificateType type)
    {
        return type is
            CaregiverCertificateType.PracticeLicense or
            CaregiverCertificateType.GraduationCertificate;
    }

    private void EnsureMandatoryCertificatesAreCompliant(
        DateOnly currentDate)
    {
        EnsureCertificateIsCompliant(
            CaregiverCertificateType.PracticeLicense,
            currentDate);

        EnsureCertificateIsCompliant(
            CaregiverCertificateType.GraduationCertificate,
            currentDate);
    }

    private void EnsureCertificateIsCompliant(
        CaregiverCertificateType certificateType,
        DateOnly currentDate)
    {
        CaregiverCertificate certificate =
            GetRequiredCertificate(certificateType);

        if (certificate.VerificationStatus !=
            CertificateVerificationStatus.Verified)
        {
            throw new DomainException(
                $"The {certificateType} must be Verified.");
        }

        bool isExpired =
            certificate.ExpiryDate.HasValue &&
            certificate.ExpiryDate.Value <
            currentDate;

        if (isExpired)
        {
            throw new DomainException(
                $"The {certificateType} has expired.");
        }
    }

    private static string? NormalizeOptionalDetailedAddress(
        string? detailedAddress)
    {
        if (string.IsNullOrWhiteSpace(
            detailedAddress))
        {
            return null;
        }

        string normalizedAddress =
            detailedAddress.Trim();

        if (normalizedAddress.Length >
            MaximumDetailedAddressLength)
        {
            throw new DomainException(
                $"Detailed address cannot exceed " +
                $"{MaximumDetailedAddressLength} characters.");
        }

        return normalizedAddress;
    }

    private void EnsureCompanionCaregiver()
    {
        if (Type != CaregiverType.Companion)
        {
            throw new DomainException(
                "Only a Companion caregiver can manage " +
                "a Companion schedule.");
        }
    }

    private void EnsureMedicalCaregiver()
    {
        if (Type != CaregiverType.Medical)
        {
            throw new DomainException(
                "Only a Medical caregiver can manage " +
                "a Medical schedule.");
        }
    }

    private void EnsureActiveScheduleRemainsAvailable(
        bool hasAvailability)
    {
        if (Status == CaregiverStatus.Active &&
            !hasAvailability)
        {
            throw new DomainException(
                "An Active caregiver must have " +
                "at least one availability entry.");
        }
    }

    private void EnsureStatus(
        CaregiverStatus requiredStatus,
        string errorMessage)
    {
        if (Status != requiredStatus)
        {
            throw new DomainException(
                errorMessage);
        }
    }

    private static string NormalizeRequiredStatusReason(
        string reason,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                $"{fieldName} is required.");
        }

        return reason.Trim();
    }

    private static void ValidateUtc(
        DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Transition time must be in UTC.");
        }
    }
    private void EnsureRequiredSelections()
    {
        if (_serviceSelections.Count == 0)
        {
            throw new DomainException(
                "At least one Service is required.");
        }

        if (_languageSelections.Count == 0)
        {
            throw new DomainException(
                "At least one Language is required.");
        }

        if (_areaSelections.Count == 0)
        {
            throw new DomainException(
                "At least one Area is required.");
        }
    }

    private void EnsureCompanionSubmissionReadiness()
    {
        if (CompanionProfile is null)
        {
            throw new DomainException(
                "Companion professional profile is required.");
        }

        if (CompanionPricing is null)
        {
            throw new DomainException(
                "Companion pricing is required.");
        }

        if (CompanionSchedule is null ||
            !CompanionSchedule.HasAvailability)
        {
            throw new DomainException(
                "At least one Companion availability " +
                "window is required.");
        }
    }

    private void EnsureMedicalSubmissionReadiness(
        DateOnly currentDate)
    {
        if (MedicalProfile is null)
        {
            throw new DomainException(
                "Medical professional profile is required.");
        }

        if (MedicalPricing is null)
        {
            throw new DomainException(
                "Medical pricing is required.");
        }

        if (MedicalSchedule is null ||
            !MedicalSchedule.HasAvailability)
        {
            throw new DomainException(
                "At least one Medical availability " +
                "entry is required.");
        }

        EnsureCertificateIsReadyForReview(
            CaregiverCertificateType.PracticeLicense,
            currentDate);

        EnsureCertificateIsReadyForReview(
            CaregiverCertificateType.GraduationCertificate,
            currentDate);
    }

    private void EnsureCertificateIsReadyForReview(
        CaregiverCertificateType certificateType,
        DateOnly currentDate)
    {
        CaregiverCertificate certificate =
            GetRequiredCertificate(
                certificateType);

        bool hasAllowedStatus =
            certificate.VerificationStatus is
                CertificateVerificationStatus.Pending or
                CertificateVerificationStatus.Verified;

        if (!hasAllowedStatus)
        {
            throw new DomainException(
                $"The {certificateType} must be replaced " +
                "before submission.");
        }

        bool isExpired =
            certificate.ExpiryDate.HasValue &&
            certificate.ExpiryDate.Value <
            currentDate;

        if (isExpired)
        {
            throw new DomainException(
                $"The {certificateType} has expired.");
        }
    }

    private CaregiverCertificate GetRequiredCertificate(
        CaregiverCertificateType certificateType)
    {
        CaregiverCertificate? certificate =
            _certificates.SingleOrDefault(
                certificate =>
                    certificate.Type ==
                    certificateType);

        if (certificate is null)
        {
            throw new DomainException(
                $"The caregiver does not have a " +
                $"{certificateType}.");
        }

        return certificate;
    }


}