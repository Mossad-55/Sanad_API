using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Families.Domain.Elderlies.Medical;

public sealed class ElderlyMedicalProfile
{
    public const int MinimumHeightCm = 50;
    public const int MaximumHeightCm = 250;
    public const decimal MinimumWeightKg = 20.0m;
    public const decimal MaximumWeightKg = 300.0m;
    public const int MaximumConditionsCount = 30;
    public const int MaximumConditionTextLength = 150;

    private readonly List<string> _chronicConditions = [];
    private readonly List<AllergyEntry> _allergies = [];
    private readonly List<MedicalHistoryEntry> _medicalHistory = [];

    private ElderlyMedicalProfile()
    {
    }

    internal ElderlyMedicalProfile(
        BloodType bloodType,
        int? heightCm,
        decimal? weightKg,
        IEnumerable<string> chronicConditions,
        IEnumerable<AllergyEntry> allergies,
        IEnumerable<MedicalHistoryEntry> medicalHistory)
    {
        BloodType = bloodType;
        HeightCm = heightCm;
        WeightKg = weightKg;

        _chronicConditions.AddRange(chronicConditions);
        _allergies.AddRange(allergies);
        _medicalHistory.AddRange(medicalHistory);

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public BloodType BloodType { get; private set; }
    public int? HeightCm { get; private set; }
    public decimal? WeightKg { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }

    public IReadOnlyList<string> ChronicConditions =>
        _chronicConditions.AsReadOnly();

    public IReadOnlyList<AllergyEntry> Allergies =>
        _allergies.AsReadOnly();

    public IReadOnlyList<MedicalHistoryEntry> MedicalHistory =>
        _medicalHistory.AsReadOnly();

    public static ElderlyMedicalProfile Create(
        BloodType bloodType,
        int? heightCm,
        decimal? weightKg,
        IEnumerable<string>? chronicConditions = null,
        IEnumerable<AllergyEntry>? allergies = null,
        IEnumerable<MedicalHistoryEntry>? medicalHistory = null)
    {
        ValidateMetrics(bloodType, heightCm, weightKg);

        var conditionsList = ValidateAndNormalizeConditions(chronicConditions);
        var allergiesList = (allergies ?? []).ToList();
        var historyList = (medicalHistory ?? []).ToList();

        return new ElderlyMedicalProfile(
            bloodType,
            heightCm,
            weightKg,
            conditionsList,
            allergiesList,
            historyList);
    }

    public void Update(
        BloodType bloodType,
        int? heightCm,
        decimal? weightKg,
        IEnumerable<string>? chronicConditions,
        IEnumerable<AllergyEntry>? allergies,
        IEnumerable<MedicalHistoryEntry>? medicalHistory)
    {
        ValidateMetrics(bloodType, heightCm, weightKg);

        var conditionsList = ValidateAndNormalizeConditions(chronicConditions);
        var allergiesList = (allergies ?? []).ToList();
        var historyList = (medicalHistory ?? []).ToList();

        BloodType = bloodType;
        HeightCm = heightCm;
        WeightKg = weightKg;

        _chronicConditions.Clear();
        _chronicConditions.AddRange(conditionsList);

        _allergies.Clear();
        _allergies.AddRange(allergiesList);

        _medicalHistory.Clear();
        _medicalHistory.AddRange(historyList);

        UpdatedOnUtc = DateTime.UtcNow;
    }

    private static void ValidateMetrics(
        BloodType bloodType,
        int? heightCm,
        decimal? weightKg)
    {
        if (!Enum.IsDefined(bloodType))
        {
            throw new DomainException("Invalid blood type.");
        }

        if (heightCm.HasValue && (heightCm < MinimumHeightCm || heightCm > MaximumHeightCm))
        {
            throw new DomainException(
                $"Height must be between {MinimumHeightCm} and {MaximumHeightCm} cm.");
        }

        if (weightKg.HasValue && (weightKg < MinimumWeightKg || weightKg > MaximumWeightKg))
        {
            throw new DomainException(
                $"Weight must be between {MinimumWeightKg} and {MaximumWeightKg} kg.");
        }
    }

    private static List<string> ValidateAndNormalizeConditions(
        IEnumerable<string>? conditions)
    {
        if (conditions is null)
        {
            return [];
        }

        var list = new List<string>();
        foreach (var condition in conditions)
        {
            if (string.IsNullOrWhiteSpace(condition))
            {
                continue;
            }

            string trimmed = condition.Trim();
            if (trimmed.Length > MaximumConditionTextLength)
            {
                throw new DomainException(
                    $"Chronic condition cannot exceed {MaximumConditionTextLength} characters.");
            }

            if (!list.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(trimmed);
            }
        }

        if (list.Count > MaximumConditionsCount)
        {
            throw new DomainException(
                $"Cannot add more than {MaximumConditionsCount} chronic conditions.");
        }

        return list;
    }
}