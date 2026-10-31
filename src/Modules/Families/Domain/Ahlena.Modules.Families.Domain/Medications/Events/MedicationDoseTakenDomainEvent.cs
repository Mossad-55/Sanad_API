using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Families.Domain.Medications.Events;

public sealed record MedicationDoseTakenDomainEvent(
    MedicationDoseLogId DoseLogId,
    MedicationId MedicationId,
    ElderlyId ElderlyId,
    int RemainingStock) : DomainEvent;