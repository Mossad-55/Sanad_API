using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Application.Abstractions.Medications;

public sealed record MedicationLatenessSettingSnapshot(int ThresholdMinutes, int Version);
public sealed record MedicationLateAlertRequest(UserId ElderlyIdentityUserId, Guid ElderlyId, Guid MedicationId, DateOnly ScheduledDate, TimeOnly ScheduledTime, DateTime CreatedOnUtc);

public interface IMedicationLatenessSettingGateway
{
    Task<MedicationLatenessSettingSnapshot?> GetActiveAsync(CancellationToken cancellationToken = default);
}

public interface IMedicationLateAlertGateway
{
    Task<int> CreateAsync(MedicationLateAlertRequest request, CancellationToken cancellationToken = default);
}
