using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

public sealed class Area : AggregateRoot<AreaId>
{
    private Area()
    {
    }

    public CityId CityId { get; private set; }

    public string ArabicName { get; private set; } = string.Empty;

    public string EnglishName { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }
}