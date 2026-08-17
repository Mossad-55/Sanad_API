using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Families.Events;

public sealed record FamilyCreatedDomainEvent(
    FamilyId FamilyId,
    UserId OwnerUserId)
    : DomainEvent;