using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Caregivers.Domain.Caregivers.Events;

public sealed record CaregiverSubmittedForReviewDomainEvent(
        CaregiverId CaregiverId,
        bool IsResubmission)
    : DomainEvent;