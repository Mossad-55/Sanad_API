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

    public async Task<MedicalCaregiverRecipientPage> SearchActiveMedicalCaregiversAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await caregivers.SearchActiveMedicalCaregiversAsync(
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            page,
            pageSize,
            cancellationToken);

        return new MedicalCaregiverRecipientPage(
            items.Select(item => new MedicalCaregiverRecipient(
                item.CaregiverId.Value,
                item.UserId.Value,
                item.ArabicFullName,
                item.EnglishFullName,
                item.AvatarUrl,
                item.SpecializationId,
                item.SpecializationArabicName,
                item.SpecializationEnglishName)).ToList(),
            totalCount);
    }
}
