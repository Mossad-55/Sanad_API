using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

public sealed class City : Entity<CityId>
{
    private City()
    {
    }

    public GovernorateId GovernorateId { get; private set; }

    public string ArabicName { get; private set; } = string.Empty;

    public string EnglishName { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }
}