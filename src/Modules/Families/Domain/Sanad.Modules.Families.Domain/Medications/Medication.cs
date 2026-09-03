using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Medications.Events;

namespace Sanad.Modules.Families.Domain.Medications;

public sealed class Medication : AggregateRoot<MedicationId>
{
    public const int MaximumNameLength = 200;
    public const int MaximumDosageLength = 100;
    public const int MaximumDoseUnitLength = 50;
    public const int MaximumInstructionsLength = 500;

    private readonly List<TimeOnly> _doseTimes = [];

    private Medication()
    {
    }

    private Medication(
        MedicationId id,
        ElderlyId elderlyId,
        UserId createdByUserId,
        string name,
        string dosage,
        string doseUnit,
        int doseQuantity,
        IEnumerable<TimeOnly> doseTimes,
        DateOnly startDate,
        DateOnly? endDate,
        string? instructions,
        int? stockQuantity,
        int? lowStockThreshold)
        : base(id)
    {
        ElderlyId = elderlyId;
        CreatedByUserId = createdByUserId;
        Name = name;
        Dosage = dosage;
        DoseUnit = doseUnit;
        DoseQuantity = doseQuantity;
        _doseTimes = doseTimes.OrderBy(t => t).ToList();
        StartDate = startDate;
        EndDate = endDate;
        Instructions = instructions;
        StockQuantity = stockQuantity;
        LowStockThreshold = lowStockThreshold;
        Status = MedicationStatus.Active;

        CreatedOnUtc = DateTime.UtcNow;
        UpdatedOnUtc = DateTime.UtcNow;

        RaiseDomainEvent(new MedicationCreatedDomainEvent(Id, ElderlyId));
    }

    public ElderlyId ElderlyId { get; private set; }
    public UserId CreatedByUserId { get; private set; }
    public string Name { get; private set; } = default!;
    public string Dosage { get; private set; } = default!;
    public string DoseUnit { get; private set; } = default!;
    public int DoseQuantity { get; private set; }
    public IReadOnlyList<TimeOnly> DoseTimes => _doseTimes.AsReadOnly();
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public string? Instructions { get; private set; }
    public int? StockQuantity { get; private set; }
    public int? LowStockThreshold { get; private set; }
    public MedicationStatus Status { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }

    public static Medication Create(
        ElderlyId elderlyId,
        UserId createdByUserId,
        string name,
        string dosage,
        string doseUnit,
        int doseQuantity,
        IEnumerable<TimeOnly> doseTimes,
        DateOnly startDate,
        DateOnly? endDate,
        string? instructions = null,
        int? stockQuantity = null,
        int? lowStockThreshold = null)
    {
        ValidateCommon(name, dosage, doseUnit, doseQuantity, doseTimes, startDate, endDate, stockQuantity, lowStockThreshold);

        return new Medication(
            MedicationId.New(),
            elderlyId,
            createdByUserId,
            name.Trim(),
            dosage.Trim(),
            doseUnit.Trim(),
            doseQuantity,
            doseTimes,
            startDate,
            endDate,
            NormalizeOptional(instructions, MaximumInstructionsLength, "Instructions"),
            stockQuantity,
            lowStockThreshold);
    }

    public void UpdateDetails(
        string name,
        string dosage,
        string doseUnit,
        int doseQuantity,
        IEnumerable<TimeOnly> doseTimes,
        DateOnly startDate,
        DateOnly? endDate,
        string? instructions)
    {
        ValidateCommon(name, dosage, doseUnit, doseQuantity, doseTimes, startDate, endDate, StockQuantity, LowStockThreshold);

        Name = name.Trim();
        Dosage = dosage.Trim();
        DoseUnit = doseUnit.Trim();
        DoseQuantity = doseQuantity;
        _doseTimes.Clear();
        _doseTimes.AddRange(doseTimes.OrderBy(t => t));
        StartDate = startDate;
        EndDate = endDate;
        Instructions = NormalizeOptional(instructions, MaximumInstructionsLength, "Instructions");

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void UpdateStock(int? newStockQuantity, int? newLowStockThreshold)
    {
        if (newStockQuantity is < 0)
        {
            throw new DomainException("Stock quantity cannot be negative.");
        }

        if (newLowStockThreshold is < 0)
        {
            throw new DomainException("Low stock threshold cannot be negative.");
        }

        StockQuantity = newStockQuantity;
        LowStockThreshold = newLowStockThreshold;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void DecrementStock(int quantity)
    {
        if (!StockQuantity.HasValue || quantity <= 0)
        {
            return;
        }

        StockQuantity = Math.Max(0, StockQuantity.Value - quantity);
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public StockStatus GetStockStatus()
    {
        if (!StockQuantity.HasValue)
        {
            return StockStatus.NotTracked;
        }

        if (StockQuantity.Value == 0)
        {
            return StockStatus.OutOfStock;
        }

        if (LowStockThreshold.HasValue && StockQuantity.Value <= LowStockThreshold.Value)
        {
            return StockStatus.LowStock;
        }

        return StockStatus.Normal;
    }

    public void Pause()
    {
        if (Status == MedicationStatus.Discontinued)
        {
            throw new DomainException("Cannot pause a discontinued medication.");
        }

        Status = MedicationStatus.Paused;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Resume()
    {
        if (Status == MedicationStatus.Discontinued)
        {
            throw new DomainException("Cannot resume a discontinued medication.");
        }

        Status = MedicationStatus.Active;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Complete()
    {
        Status = MedicationStatus.Completed;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Discontinue()
    {
        Status = MedicationStatus.Discontinued;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    private static void ValidateCommon(
        string name,
        string dosage,
        string doseUnit,
        int doseQuantity,
        IEnumerable<TimeOnly> doseTimes,
        DateOnly startDate,
        DateOnly? endDate,
        int? stockQuantity,
        int? lowStockThreshold)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Medication name is required.");
        }

        if (name.Trim().Length > MaximumNameLength)
        {
            throw new DomainException($"Medication name cannot exceed {MaximumNameLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(dosage))
        {
            throw new DomainException("Dosage is required.");
        }

        if (dosage.Trim().Length > MaximumDosageLength)
        {
            throw new DomainException($"Dosage cannot exceed {MaximumDosageLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(doseUnit))
        {
            throw new DomainException("Dose unit is required.");
        }

        if (doseUnit.Trim().Length > MaximumDoseUnitLength)
        {
            throw new DomainException($"Dose unit cannot exceed {MaximumDoseUnitLength} characters.");
        }

        if (doseQuantity <= 0)
        {
            throw new DomainException("Dose quantity must be greater than zero.");
        }

        if (doseTimes == null || !doseTimes.Any())
        {
            throw new DomainException("At least one scheduled dose time is required.");
        }

        if (endDate.HasValue && endDate.Value < startDate)
        {
            throw new DomainException("End date cannot be earlier than start date.");
        }

        if (stockQuantity is < 0)
        {
            throw new DomainException("Stock quantity cannot be negative.");
        }

        if (lowStockThreshold is < 0)
        {
            throw new DomainException("Low stock threshold cannot be negative.");
        }
    }

    private static string? NormalizeOptional(string? value, int maxLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();

        if (normalized.Length > maxLength)
        {
            throw new DomainException($"{fieldName} cannot exceed {maxLength} characters.");
        }

        return normalized;
    }
}