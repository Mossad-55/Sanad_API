using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Elderlies.Events;

public sealed record ElderlyCreatedDomainEvent(
    ElderlyId ElderlyId)
    : DomainEvent;