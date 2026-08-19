using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers.Selections;

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