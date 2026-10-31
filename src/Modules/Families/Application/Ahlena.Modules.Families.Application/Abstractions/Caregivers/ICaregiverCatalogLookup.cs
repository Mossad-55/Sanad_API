using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Families.Application.Abstractions.Caregivers;

/// <summary>
/// Chat Plus pricing-attribution seam: validates caller-selected booked
/// dimensions against the catalogue and the booked caregiver's own
/// selections at creation. Implemented in Ahlena.API over the Caregivers
/// store; the Families Application declares only the seam. No location
/// inference, no current-profile backfill, no multi-service expansion.
/// </summary>
public interface ICaregiverCatalogLookup
{
    /// <summary>
    /// Returns true when both ids are active catalogue entries AND both are
    /// selected by the given caregiver. Either id unknown/inactive or not
    /// selected by that caregiver returns false (never which check failed).
    /// </summary>
    Task<bool> IsSelectedPairAsync(
        CaregiverId caregiverId,
        Guid areaId,
        Guid serviceId,
        CancellationToken cancellationToken = default);
}
