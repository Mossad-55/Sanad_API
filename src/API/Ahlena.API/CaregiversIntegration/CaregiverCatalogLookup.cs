using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Caregivers.Application.Abstractions.Data;
using Ahlena.Modules.Caregivers.Domain.Caregivers;
using Ahlena.Modules.Families.Application.Abstractions.Caregivers;

namespace Ahlena.API.CaregiversIntegration;

/// <summary>
/// Chat Plus pricing-attribution validation over the Caregivers store: both
/// ids must be active catalogue entries AND selected by the booked
/// caregiver at creation. Unknown/inactive/unselected collapse into one
/// false (the checkout handler serves its own 400 vocabulary).
/// </summary>
public sealed class CaregiverCatalogLookup : ICaregiverCatalogLookup
{
    private readonly ICaregiversDbContext _caregivers;

    public CaregiverCatalogLookup(ICaregiversDbContext caregivers)
    {
        _caregivers = caregivers;
    }

    public async Task<bool> IsSelectedPairAsync(
        CaregiverId caregiverId,
        Guid areaId,
        Guid serviceId,
        CancellationToken cancellationToken = default)
    {
        if (caregiverId == CaregiverId.Empty ||
            areaId == Guid.Empty ||
            serviceId == Guid.Empty)
        {
            return false;
        }

        var areaKey = new AreaId(areaId);
        var serviceKey = new ServiceId(serviceId);

        bool areaActive = await _caregivers.Areas
            .AsNoTracking()
            .AnyAsync(
                area => area.Id == areaKey && area.IsActive,
                cancellationToken);

        if (!areaActive)
        {
            return false;
        }

        bool serviceActive = await _caregivers.Services
            .AsNoTracking()
            .AnyAsync(
                service => service.Id == serviceKey && service.IsActive,
                cancellationToken);

        if (!serviceActive)
        {
            return false;
        }

        Caregiver? caregiver = await _caregivers.Caregivers
            .AsNoTracking()
            .Where(item => item.Id == caregiverId)
            .Include(item => item.AreaSelections)
            .Include(item => item.ServiceSelections)
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);

        if (caregiver is null)
        {
            return false;
        }

        // Whole-struct comparison in memory; selections are small and owned.
        var selectedAreas = caregiver.AreaSelections
            .Select(selection => selection.Id)
            .ToHashSet();
        var selectedServices = caregiver.ServiceSelections
            .Select(selection => selection.Id)
            .ToHashSet();

        return selectedAreas.Contains(areaKey) &&
            selectedServices.Contains(serviceKey);
    }
}
