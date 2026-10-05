using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Application.MedicationTasks;

public sealed record SkipCaregiverMedicationTaskCommand(
    Guid CaregiverId,
    MedicationDoseLogId TaskId,
    UserId ActorUserId,
    string Reason) : ICommand<CaregiverMedicationTaskActionResponse>;

public sealed record SkipCaregiverMedicationTaskRequest(string Reason);
