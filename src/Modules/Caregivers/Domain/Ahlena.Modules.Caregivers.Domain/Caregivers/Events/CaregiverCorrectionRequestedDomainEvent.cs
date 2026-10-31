using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Caregivers.Domain.Caregivers.Events;

public sealed record CaregiverCorrectionRequestedDomainEvent(
        CaregiverId CaregiverId,
        string Reason)
    : DomainEvent;