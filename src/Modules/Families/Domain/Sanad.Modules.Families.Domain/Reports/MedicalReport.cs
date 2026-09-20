using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.Modules.Families.Domain.Reports;

public sealed class MedicalReport : AggregateRoot<MedicalReportId>
{
    private MedicalReport() { }

    private MedicalReport(
        MedicalReportId id, BookingId bookingId, FamilyId familyId, ElderlyId elderlyId,
        CaregiverId caregiverId, UserId caregiverUserId, BookingCaregiverType caregiverType,
        int? systolic, int? diastolic, int? pulse, decimal? temperature, string? notes,
        DateTime? measurementTakenOnUtc, VisitReportAssessment assessment,
        DateTime submittedOnUtc, bool photoConsentConfirmed, DateTime photoConsentAttestedOnUtc,
        UserId photoConsentCaregiverUserId, string? photoKey) : base(id)
    {
        BookingId = bookingId; FamilyId = familyId; ElderlyId = elderlyId;
        CaregiverId = caregiverId; CaregiverUserId = caregiverUserId; CaregiverType = caregiverType;
        Systolic = systolic; Diastolic = diastolic; Pulse = pulse; Temperature = temperature;
        Notes = notes;
        MeasurementTakenOnUtc = measurementTakenOnUtc; Assessment = assessment;
        SubmittedOnUtc = submittedOnUtc; PhotoConsentConfirmed = photoConsentConfirmed;
        PhotoConsentAttestedOnUtc = photoConsentAttestedOnUtc;
        PhotoConsentCaregiverUserId = photoConsentCaregiverUserId; PhotoKey = photoKey;
    }

    public BookingId BookingId { get; private set; }
    public FamilyId FamilyId { get; private set; }
    public ElderlyId ElderlyId { get; private set; }
    public CaregiverId CaregiverId { get; private set; }
    public UserId CaregiverUserId { get; private set; }
    public BookingCaregiverType CaregiverType { get; private set; }
    public int? Systolic { get; private set; }
    public int? Diastolic { get; private set; }
    public int? Pulse { get; private set; }
    public decimal? Temperature { get; private set; }
    public string? Notes { get; private set; }
    public DateTime? MeasurementTakenOnUtc { get; private set; }
    public VisitReportAssessment Assessment { get; private set; }
    public DateTime SubmittedOnUtc { get; private set; }
    public bool PhotoConsentConfirmed { get; private set; }
    public DateTime PhotoConsentAttestedOnUtc { get; private set; }
    public UserId PhotoConsentCaregiverUserId { get; private set; }
    public string? PhotoKey { get; private set; }

    public static MedicalReport Create(
        BookingId bookingId, FamilyId familyId, ElderlyId elderlyId, CaregiverId caregiverId,
        UserId caregiverUserId, BookingCaregiverType caregiverType,
        int? systolic, int? diastolic, int? pulse, decimal? temperature, string? notes,
        DateTime? measurementTakenOnUtc, VisitReportAssessment assessment, DateTime submittedOnUtc,
        bool photoConsentConfirmed, DateTime photoConsentAttestedOnUtc,
        UserId photoConsentCaregiverUserId, string? photoKey)
    {
        if (bookingId == BookingId.Empty || familyId == FamilyId.Empty || elderlyId == ElderlyId.Empty || caregiverId == CaregiverId.Empty)
            throw new DomainException("Report ownership identifiers are required.");
        if (caregiverUserId == UserId.Empty || photoConsentCaregiverUserId == UserId.Empty)
            throw new DomainException("Caregiver user identifiers are required.");
        if (caregiverType != BookingCaregiverType.Medical || !Enum.IsDefined(assessment))
            throw new DomainException("Medical report caregiver type or assessment is invalid.");
        if (submittedOnUtc.Kind != DateTimeKind.Utc || photoConsentAttestedOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Report timestamps must be in UTC.");
        if (systolic.HasValue != diastolic.HasValue)
            throw new DomainException("Blood-pressure values must be supplied together.");
        bool hasMeasurement = systolic.HasValue || diastolic.HasValue || pulse.HasValue || temperature.HasValue;
        if (hasMeasurement && (!measurementTakenOnUtc.HasValue || measurementTakenOnUtc.Value.Kind != DateTimeKind.Utc))
            throw new DomainException("Measurement time is required and must be UTC.");
        if (measurementTakenOnUtc > submittedOnUtc)
            throw new DomainException("Measurement time cannot be in the future.");
        if (photoKey is not null && !photoConsentConfirmed)
            throw new DomainException("Photo consent is required when a photo is supplied.");
        if (photoConsentCaregiverUserId != caregiverUserId)
            throw new DomainException("Photo consent must be attested by the submitting caregiver.");
        string? normalizedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (normalizedNotes?.Length > 2000)
            throw new DomainException("Notes cannot exceed 2000 characters.");

        return new MedicalReport(MedicalReportId.New(), bookingId, familyId, elderlyId, caregiverId,
            caregiverUserId, caregiverType, systolic, diastolic, pulse, temperature, normalizedNotes,
            measurementTakenOnUtc, assessment, submittedOnUtc, photoConsentConfirmed,
            photoConsentAttestedOnUtc, photoConsentCaregiverUserId, photoKey);
    }
}
