using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Medications;

public sealed class MedicationDoseLog : Entity<MedicationDoseLogId>
{
    public const int MaximumNotesLength = 500;

    private MedicationDoseLog()
    {
    }

    private MedicationDoseLog(
        MedicationDoseLogId id,
        MedicationId medicationId,
        ElderlyId elderlyId,
        DateOnly scheduledDate,
        TimeOnly scheduledTime,
        DoseStatus status)
        : base(id)
    {
        MedicationId = medicationId;
        ElderlyId = elderlyId;
        ScheduledDate = scheduledDate;
        ScheduledTime = scheduledTime;
        Status = status;

        CreatedOnUtc = DateTime.UtcNow;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public MedicationId MedicationId { get; private set; }
    public ElderlyId ElderlyId { get; private set; }
    public DateOnly ScheduledDate { get; private set; }
    public TimeOnly ScheduledTime { get; private set; }
    public DoseStatus Status { get; private set; }
    public DateTime? TakenAtUtc { get; private set; }
    public DateTime? SkippedAtUtc { get; private set; }
    public string? Notes { get; private set; }
    public UserId? LoggedByUserId { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }

    public static MedicationDoseLog CreateScheduled(
        MedicationId medicationId,
        ElderlyId elderlyId,
        DateOnly scheduledDate,
        TimeOnly scheduledTime)
    {
        return new MedicationDoseLog(
            MedicationDoseLogId.New(),
            medicationId,
            elderlyId,
            scheduledDate,
            scheduledTime,
            DoseStatus.Scheduled);
    }

    public void MarkAsTaken(UserId loggedByUserId, DateTime takenAtUtc, string? notes = null)
    {
        if (Status == DoseStatus.Taken)
        {
            throw new DomainException("This dose has already been marked as taken.");
        }

        Status = DoseStatus.Taken;
        TakenAtUtc = takenAtUtc;
        SkippedAtUtc = null;
        LoggedByUserId = loggedByUserId;
        Notes = NormalizeOptional(notes, MaximumNotesLength, "Notes");
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void MarkAsSkipped(UserId loggedByUserId, DateTime skippedAtUtc, string? reason = null)
    {
        Status = DoseStatus.Skipped;
        SkippedAtUtc = skippedAtUtc;
        TakenAtUtc = null;
        LoggedByUserId = loggedByUserId;
        Notes = NormalizeOptional(reason, MaximumNotesLength, "Reason");
        UpdatedOnUtc = DateTime.UtcNow;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();

        if (normalized.Length > maxLength)
        {
            throw new DomainException($"{fieldName} cannot exceed {maxLength} characters.");
        }

        return normalized;
    }
}