using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Application.Abstractions.Caregivers;

public interface IMedicalCaregiverGateway
{
    Task<bool> IsActiveMedicalCaregiverAsync(
        UserId userId,
        CancellationToken cancellationToken = default);

    Task<MedicalCaregiverRecipientPage> SearchActiveMedicalCaregiversAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
