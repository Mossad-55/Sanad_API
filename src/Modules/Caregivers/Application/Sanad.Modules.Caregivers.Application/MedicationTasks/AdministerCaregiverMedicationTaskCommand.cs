using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Application.MedicationTasks;

public sealed record AdministerCaregiverMedicationTaskCommand(
    Guid CaregiverId,
    MedicationDoseLogId TaskId,
    UserId ActorUserId,
    string? Notes) : ICommand<CaregiverMedicationTaskActionResponse>;

public sealed record CaregiverMedicationTaskActionResponse(
    Guid TaskId,
    MedicationTaskStatus Status,
    DateTime UpdatedOnUtc);
