using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers.Events;

public sealed record CaregiverReactivatedDomainEvent(
        CaregiverId CaregiverId)
    : DomainEvent;