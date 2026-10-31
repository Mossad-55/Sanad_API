using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Caregivers.Domain.Caregivers.Selections;

public sealed class CaregiverServiceSelection : Entity<ServiceId>
{
    private CaregiverServiceSelection()
    {
    }

    private CaregiverServiceSelection(ServiceId serviceId)
        : base(serviceId)
    {
    }

    internal static CaregiverServiceSelection Create(ServiceId serviceId)
    {
        if (serviceId == ServiceId.Empty)
        {
            throw new DomainException(
                "Service ID is required.");
        }

        return new CaregiverServiceSelection(serviceId);
    }
}