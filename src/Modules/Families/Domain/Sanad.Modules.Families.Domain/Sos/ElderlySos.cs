using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Sos;

public enum ElderlySosStatus { Open = 1, Acknowledged = 2, Resolved = 3, Cancelled = 4 }
public enum ElderlySosHistoryAction { Created = 1, Acknowledged = 2, Resolved = 3, Cancelled = 4 }

public sealed class ElderlySos : AggregateRoot<Guid>
{
    private readonly List<ElderlySosHistory> _history = [];
    private ElderlySos() { }
    private ElderlySos(Guid id, UserId elderly, Guid family, string key, bool consent, decimal? latitude, decimal? longitude, DateTime now) : base(id)
    {
        ElderlyIdentityUserId = elderly; FamilyId = family; IdempotencyKey = key; LocationConsentGranted = consent;
        Latitude = latitude; Longitude = longitude; Status = ElderlySosStatus.Open; CreatedOnUtc = now; UpdatedOnUtc = now;
    }

    public UserId ElderlyIdentityUserId { get; private set; }
    public Guid FamilyId { get; private set; }
    public string IdempotencyKey { get; private set; } = "";
    public bool LocationConsentGranted { get; private set; }
    public decimal? Latitude { get; private set; }
    public decimal? Longitude { get; private set; }
    public ElderlySosStatus Status { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    public IReadOnlyCollection<ElderlySosHistory> History => _history.AsReadOnly();

    public static ElderlySos Create(UserId elderly, Guid family, string idempotencyKey, bool consent, decimal? latitude, decimal? longitude)
    {
        if (elderly == UserId.Empty || family == Guid.Empty || string.IsNullOrWhiteSpace(idempotencyKey))
            throw new DomainException("SOS identity and idempotency key are required.");
        if ((latitude is null) != (longitude is null) || (latitude is not null && (!consent || latitude is < -90 or > 90 || longitude is < -180 or > 180)))
            throw new DomainException("SOS location is invalid.");
        var now = DateTime.UtcNow;
        var x = new ElderlySos(Guid.CreateVersion7(), elderly, family, idempotencyKey.Trim(), consent,
            latitude is null ? null : decimal.Round(latitude.Value, 3), longitude is null ? null : decimal.Round(longitude.Value, 3), now);
        x.AddHistory(ElderlySosHistoryAction.Created, elderly);
        return x;
    }

    public void Transition(ElderlySosHistoryAction action, UserId actor)
    {
        var next = action switch
        {
            ElderlySosHistoryAction.Acknowledged when Status == ElderlySosStatus.Open => ElderlySosStatus.Acknowledged,
            ElderlySosHistoryAction.Resolved when Status is ElderlySosStatus.Open or ElderlySosStatus.Acknowledged => ElderlySosStatus.Resolved,
            ElderlySosHistoryAction.Cancelled when Status is ElderlySosStatus.Open or ElderlySosStatus.Acknowledged => ElderlySosStatus.Cancelled,
            _ => throw new DomainException("The SOS transition is invalid.")
        };
        Status = next; UpdatedOnUtc = DateTime.UtcNow; AddHistory(action, actor);
    }

    private void AddHistory(ElderlySosHistoryAction action, UserId actor) => _history.Add(ElderlySosHistory.Create(Id, action, Status, actor));
}

public sealed class ElderlySosHistory : Entity<Guid>
{
    private ElderlySosHistory() { }
    private ElderlySosHistory(Guid sosId, ElderlySosHistoryAction action, ElderlySosStatus status, UserId actor) : base(Guid.CreateVersion7())
    { ElderlySosId = sosId; Action = action; Status = status; ActorUserId = actor; OccurredOnUtc = DateTime.UtcNow; }
    public Guid ElderlySosId { get; private set; }
    public ElderlySosHistoryAction Action { get; private set; }
    public ElderlySosStatus Status { get; private set; }
    public UserId ActorUserId { get; private set; }
    public DateTime OccurredOnUtc { get; private set; }
    public static ElderlySosHistory Create(Guid id, ElderlySosHistoryAction action, ElderlySosStatus status, UserId actor) => new(id, action, status, actor);
}
