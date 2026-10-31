using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Families.Domain.Medications.Events;

public sealed record MedicationCreatedDomainEvent(
    MedicationId MedicationId,
    ElderlyId ElderlyId) : DomainEvent;