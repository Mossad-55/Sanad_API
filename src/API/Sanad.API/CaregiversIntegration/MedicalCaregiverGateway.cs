using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Families.Application.Abstractions.Caregivers;

namespace Sanad.API.CaregiversIntegration;

public sealed class MedicalCaregiverGateway(ICaregiversDbContext caregivers) : IMedicalCaregiverGateway
{
    public Task<bool> IsActiveMedicalCaregiverAsync(UserId userId, CancellationToken cancellationToken = default) =>
        caregivers.Caregivers.AnyAsync(
            caregiver => caregiver.UserId == userId &&
                         caregiver.Type == CaregiverType.Medical &&
                         caregiver.Status == CaregiverStatus.Active,
            cancellationToken);
}
