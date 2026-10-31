using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Caregivers.Domain.Caregivers.Selections;

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