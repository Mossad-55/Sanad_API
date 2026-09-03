using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Medications.Events;

public sealed record MedicationCreatedDomainEvent(
    MedicationId MedicationId,
    ElderlyId ElderlyId) : DomainEvent;