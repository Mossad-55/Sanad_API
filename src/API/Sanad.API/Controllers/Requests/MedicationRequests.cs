using System.Text.Json.Serialization;

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
    string? Instructions,
    UpdateMedicationStockRequest? Stock);

public sealed class UpdateMedicationStockRequest
{
    private int? _stockQuantity;
    private int? _lowStockThreshold;

    public int? StockQuantity
    {
        get => _stockQuantity;
        set
        {
            _stockQuantity = value;
            HasStockQuantity = true;
        }
    }

    public int? LowStockThreshold
    {
        get => _lowStockThreshold;
        set
        {
            _lowStockThreshold = value;
            HasLowStockThreshold = true;
        }
    }

    [JsonIgnore]
    public bool HasStockQuantity { get; private set; }

    [JsonIgnore]
    public bool HasLowStockThreshold { get; private set; }

    [JsonIgnore]
    public bool IsComplete => HasStockQuantity && HasLowStockThreshold;
}

public sealed record RecordDoseTakenRequest(
    DateOnly ScheduledDate,
    TimeOnly ScheduledTime,
    string? Notes);

public sealed record RecordDoseSkippedRequest(
    DateOnly ScheduledDate,
    TimeOnly ScheduledTime,
    string? Reason);

public sealed record MedicationDoseHistoryRequest(
    DateOnly? StartDate,
    DateOnly? EndDate);
