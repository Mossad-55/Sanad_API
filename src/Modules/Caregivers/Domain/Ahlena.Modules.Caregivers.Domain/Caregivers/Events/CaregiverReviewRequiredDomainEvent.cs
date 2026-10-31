using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Caregivers.Domain.Caregivers.Events;

public sealed record CaregiverReviewRequiredDomainEvent(
        CaregiverId CaregiverId,
        CaregiverReviewTrigger Trigger)
    : DomainEvent;