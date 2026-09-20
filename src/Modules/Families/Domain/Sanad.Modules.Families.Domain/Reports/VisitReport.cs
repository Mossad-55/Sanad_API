using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.Modules.Families.Domain.Reports;

public enum VisitReportAssessment
{
    NoImmediateConcern = 1,
    NeedsFollowUp = 2,
    UrgentConcern = 3,
    NotAssessed = 4
}

public sealed class VisitReport : AggregateRoot<VisitReportId>
{
    public const int MaximumTextLength = 2000;

    private VisitReport()
    {
    }

    private VisitReport(
        VisitReportId id,
        BookingId bookingId,
        FamilyId familyId,
        ElderlyId elderlyId,
        CaregiverId caregiverId,
        UserId caregiverUserId,
        BookingCaregiverType caregiverType,
        string? observedCondition,
        string? activities,
        string? notes,
        VisitReportAssessment assessment,
        DateTime startedOnUtc,
        DateTime completedOnUtc,
        DateTime submittedOnUtc)
        : base(id)
    {
        BookingId = bookingId;
        FamilyId = familyId;
        ElderlyId = elderlyId;
        CaregiverId = caregiverId;
        CaregiverUserId = caregiverUserId;
        CaregiverType = caregiverType;
        ObservedCondition = observedCondition;
        Activities = activities;
        Notes = notes;
        Assessment = assessment;
        StartedOnUtc = startedOnUtc;
        CompletedOnUtc = completedOnUtc;
        SubmittedOnUtc = submittedOnUtc;
    }

    public BookingId BookingId { get; private set; }
    public FamilyId FamilyId { get; private set; }
    public ElderlyId ElderlyId { get; private set; }
    public CaregiverId CaregiverId { get; private set; }
    public UserId CaregiverUserId { get; private set; }
    public BookingCaregiverType CaregiverType { get; private set; }
    public string? ObservedCondition { get; private set; }
    public string? Activities { get; private set; }
    public string? Notes { get; private set; }
    public VisitReportAssessment Assessment { get; private set; }
    public DateTime StartedOnUtc { get; private set; }
    public DateTime CompletedOnUtc { get; private set; }
    public DateTime SubmittedOnUtc { get; private set; }

    public static VisitReport Create(
        BookingId bookingId,
        FamilyId familyId,
        ElderlyId elderlyId,
        CaregiverId caregiverId,
        UserId caregiverUserId,
        BookingCaregiverType caregiverType,
        string? observedCondition,
        string? activities,
        string? notes,
        VisitReportAssessment assessment,
        DateTime startedOnUtc,
        DateTime completedOnUtc,
        DateTime submittedOnUtc)
    {
        if (bookingId == BookingId.Empty)
            throw new DomainException("Booking ID is required.");
        if (familyId == FamilyId.Empty)
            throw new DomainException("Family ID is required.");
        if (elderlyId == ElderlyId.Empty)
            throw new DomainException("Elderly ID is required.");
        if (caregiverId == CaregiverId.Empty)
            throw new DomainException("Caregiver ID is required.");
        if (caregiverUserId == UserId.Empty)
            throw new DomainException("Caregiver user ID is required.");
        if (!Enum.IsDefined(caregiverType))
            throw new DomainException("Caregiver type is invalid.");
        if (!Enum.IsDefined(assessment))
            throw new DomainException("Visit report assessment is invalid.");
        if (startedOnUtc.Kind != DateTimeKind.Utc ||
            completedOnUtc.Kind != DateTimeKind.Utc ||
            submittedOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Visit report timestamps must be in UTC.");
        if (completedOnUtc < startedOnUtc)
            throw new DomainException("Completed time cannot be before started time.");
        if (submittedOnUtc < completedOnUtc)
            throw new DomainException("Submitted time cannot be before completed time.");

        string? normalizedObservedCondition = NormalizeText(observedCondition, nameof(observedCondition));
        string? normalizedActivities = NormalizeText(activities, nameof(activities));
        string? normalizedNotes = NormalizeText(notes, nameof(notes));

        if (normalizedObservedCondition is null &&
            normalizedActivities is null &&
            normalizedNotes is null)
            throw new DomainException("At least one visit report text field is required.");

        return new VisitReport(
            VisitReportId.New(),
            bookingId,
            familyId,
            elderlyId,
            caregiverId,
            caregiverUserId,
            caregiverType,
            normalizedObservedCondition,
            normalizedActivities,
            normalizedNotes,
            assessment,
            startedOnUtc,
            completedOnUtc,
            submittedOnUtc);
    }

    private static string? NormalizeText(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string normalized = value.Trim();
        if (normalized.Length > MaximumTextLength)
            throw new DomainException($"{fieldName} cannot exceed {MaximumTextLength} characters.");

        return normalized;
    }
}
