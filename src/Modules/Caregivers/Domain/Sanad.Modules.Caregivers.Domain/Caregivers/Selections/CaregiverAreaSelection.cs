using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers.Selections;

public sealed class CaregiverAreaSelection :
    Entity<AreaId>
{
    private CaregiverAreaSelection()
    {
    }

    private CaregiverAreaSelection(
        AreaId areaId)
        : base(areaId)
    {
    }

    internal static CaregiverAreaSelection Create(
        AreaId areaId)
    {
        if (areaId == AreaId.Empty)
        {
            throw new DomainException(
                "Area ID is required.");
        }

        return new CaregiverAreaSelection(
            areaId);
    }
}