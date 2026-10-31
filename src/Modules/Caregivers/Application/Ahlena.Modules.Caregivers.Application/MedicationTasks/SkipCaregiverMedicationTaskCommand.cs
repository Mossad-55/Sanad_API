using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Caregivers.Application.MedicationTasks;

public sealed record SkipCaregiverMedicationTaskCommand(
    Guid CaregiverId,
    MedicationDoseLogId TaskId,
    UserId ActorUserId,
    string Reason) : ICommand<CaregiverMedicationTaskActionResponse>;

public sealed record SkipCaregiverMedicationTaskRequest(string Reason);
