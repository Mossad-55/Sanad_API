using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Medications.Events;

public sealed record MedicationDoseTakenDomainEvent(
    MedicationDoseLogId DoseLogId,
    MedicationId MedicationId,
    ElderlyId ElderlyId,
    int RemainingStock) : DomainEvent;