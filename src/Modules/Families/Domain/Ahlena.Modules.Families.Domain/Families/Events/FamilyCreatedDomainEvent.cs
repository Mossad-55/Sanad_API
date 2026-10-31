using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Families.Domain.Families.Events;

public sealed record FamilyCreatedDomainEvent(
    FamilyId FamilyId,
    UserId OwnerUserId)
    : DomainEvent;