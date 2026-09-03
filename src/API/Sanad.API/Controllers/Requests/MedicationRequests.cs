namespace Sanad.API.Controllers.Requests;

public sealed record AddMedicationRequest(
    string Name,
    string Dosage,
    string DoseUnit,
    int DoseQuantity,
    List<TimeOnly> DoseTimes,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Instructions,
    int? StockQuantity,
    int? LowStockThreshold);

public sealed record UpdateMedicationRequest(
    string Name,
    string Dosage,
    string DoseUnit,
    int DoseQuantity,
    List<TimeOnly> DoseTimes,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Instructions);

public sealed record UpdateMedicationStockRequest(
    int? StockQuantity,
    int? LowStockThreshold);

public sealed record RecordDoseTakenRequest(
    DateOnly ScheduledDate,
    TimeOnly ScheduledTime,
    string? Notes);

public sealed record RecordDoseSkippedRequest(
    DateOnly ScheduledDate,
    TimeOnly ScheduledTime,
    string? Reason);