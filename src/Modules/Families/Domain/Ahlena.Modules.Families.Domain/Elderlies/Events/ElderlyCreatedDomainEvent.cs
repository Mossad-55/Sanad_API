using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Families.Domain.Elderlies.Events;

public sealed record ElderlyCreatedDomainEvent(
    ElderlyId ElderlyId)
    : DomainEvent;