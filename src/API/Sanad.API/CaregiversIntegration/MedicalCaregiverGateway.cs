using MediatR;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Families.Application.Abstractions.Caregivers;
using Sanad.Modules.Identity.Application.Users;

namespace Sanad.API.CaregiversIntegration;

public sealed class MedicalCaregiverGateway(
    ICaregiversDbContext caregivers,
    ISender sender) : IMedicalCaregiverGateway
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

    public async Task<IReadOnlyDictionary<Guid, CaregiverDisplayName>> GetCaregiverDisplayNamesAsync(
        IReadOnlyCollection<Guid> caregiverIds,
        CancellationToken cancellationToken = default)
    {
        if (caregiverIds.Count == 0)
        {
            return new Dictionary<Guid, CaregiverDisplayName>();
        }

        // Batch 1 — caregiver id → owning identity user id. Read without status/type filters on
        // purpose: an admin booking list may reference an onboarding, deactivated or retired
        // caregiver, and the booking's caregiverId must still resolve to whatever Identity
        // publishes for it. The caregiver id itself is routing data only, never display output.
        // Ids stay in their mapped wrappers for the filter and projection; unwrapping them
        // (caregiver.Id.Value) is not translatable for the relational provider, so the rows are
        // unwrapped after materialization instead.
        List<CaregiverId> typedCaregiverIds = caregiverIds.Select(id => new CaregiverId(id)).ToList();
        List<CaregiverUserRow> rows = (await caregivers.Caregivers
                .AsNoTracking()
                .Where(caregiver => typedCaregiverIds.Contains(caregiver.Id))
                .Select(caregiver => new { caregiver.Id, caregiver.UserId })
                .ToListAsync(cancellationToken))
            .Select(caregiver => new CaregiverUserRow(caregiver.Id.Value, caregiver.UserId.Value))
            .ToList();

        if (rows.Count == 0)
        {
            return new Dictionary<Guid, CaregiverDisplayName>();
        }

        // Batch 2 — the published Identity display-info query. Reusing it (instead of
        // reimplementing the anonymization check) is the contract: anonymized identities are
        // filtered out by the query, so they read as missing rows and the projection below
        // answers null names rather than reviving hidden personal data.
        Result<IReadOnlyList<UserDisplayInfo>> display = await sender.Send(
            new GetUserDisplayInfoByIdsQuery(
                rows.Select(row => new UserId(row.UserId)).ToList()),
            cancellationToken);

        Dictionary<Guid, UserDisplayInfo> displayByUserId = display.IsSuccess
            ? display.Value.ToDictionary(info => info.UserId.Value)
            : new Dictionary<Guid, UserDisplayInfo>();

        return rows.ToDictionary(
            row => row.CaregiverId,
            row => displayByUserId.TryGetValue(row.UserId, out UserDisplayInfo? info)
                ? new CaregiverDisplayName(row.CaregiverId, info.ArabicFullName, info.EnglishFullName)
                : new CaregiverDisplayName(row.CaregiverId, null, null));
    }

    /// <summary>Projection row for the batched caregiver read (private, never exposed).</summary>
    private sealed record CaregiverUserRow(Guid CaregiverId, Guid UserId);
}
