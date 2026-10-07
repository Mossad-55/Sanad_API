using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Microsoft.EntityFrameworkCore;

namespace Sanad.Modules.Caregivers.Application.MedicationTasks;

public sealed record CaregiverMedicationTaskResponse(
    Guid TaskId,
    Guid MedicationId,
    string MedicationNameArabic,
    string MedicationNameEnglish,
    string Dosage,
    string Frequency,
    DateOnly DueDate,
    TimeOnly DueTime,
    string TimeZoneId,
    MedicationTaskStatus Status,
    bool IsAdministered,
    bool IsSkipped);

public enum MedicationTaskStatus { Pending = 1, Administered = 2, Skipped = 3, Overdue = 4 }

public sealed record GetCaregiverMedicationTasksQuery(
    Guid CaregiverId,
    UserId ActorUserId,
    MedicationTaskStatus? Status = null,
    DateOnly? startDate = null,
    DateOnly? endDate = null) : IQuery<CaregiverMedicationTaskResponse[]>;
