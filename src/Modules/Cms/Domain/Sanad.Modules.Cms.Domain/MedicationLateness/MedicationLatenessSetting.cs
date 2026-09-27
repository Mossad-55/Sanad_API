using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Cms.Domain.MedicationLateness;

public sealed class MedicationLatenessSetting : AggregateRoot<Guid>
{
    public const string StableKey = "medication-lateness-threshold";
    public const int InitialThresholdMinutes = 60;
    public const int MinimumThresholdMinutes = 1;
    public const int MaximumThresholdMinutes = 24 * 60;

    private readonly List<MedicationLatenessSettingRevision> _revisions = [];
    private MedicationLatenessSetting() { }
    private MedicationLatenessSetting(Guid id) : base(id) { }

    public IReadOnlyCollection<MedicationLatenessSettingRevision> Revisions => _revisions.AsReadOnly();

    public static MedicationLatenessSetting Create() => new(Guid.CreateVersion7());

    public MedicationLatenessSettingRevision AddRevision(int thresholdMinutes)
    {
        if (thresholdMinutes is < MinimumThresholdMinutes or > MaximumThresholdMinutes)
            throw new DomainException("Medication lateness threshold is invalid.");
        var revision = MedicationLatenessSettingRevision.Create(Id, _revisions.Count == 0 ? 1 : _revisions.Max(x => x.Version) + 1, thresholdMinutes);
        _revisions.Add(revision);
        return revision;
    }
}

public sealed class MedicationLatenessSettingRevision : Entity<Guid>
{
    private MedicationLatenessSettingRevision() { }
    private MedicationLatenessSettingRevision(Guid id, Guid settingId, int version, int thresholdMinutes) : base(id)
    {
        SettingId = settingId;
        Version = version;
        ThresholdMinutes = thresholdMinutes;
        CreatedOnUtc = DateTime.UtcNow;
    }

    public Guid SettingId { get; private set; }
    public MedicationLatenessSetting? Setting { get; private set; }
    public int Version { get; private set; }
    public int ThresholdMinutes { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    public static MedicationLatenessSettingRevision Create(Guid settingId, int version, int thresholdMinutes)
    {
        if (thresholdMinutes is < MedicationLatenessSetting.MinimumThresholdMinutes or > MedicationLatenessSetting.MaximumThresholdMinutes)
            throw new DomainException("Medication lateness threshold is invalid.");
        return new(Guid.CreateVersion7(), settingId, version, thresholdMinutes);
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
