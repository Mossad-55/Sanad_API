using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Domain.Bookings;

public enum CareHomeVisitKind { Prospective = 1, Resident = 2 }
public enum CareHomeVisitStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    FamilyCancelled = 4,
    FacilityCancelled = 5,
    Expired = 6,
    ReschedulePending = 7,
    Rescheduled = 8
}

public sealed record CareHomeVisitTimeWindow(DayOfWeek Day, TimeOnly StartTime, TimeOnly EndTime);
public sealed record CareHomeVisitClosure(DateOnly Date, string? Reason);

public sealed class CareHomeVisitSettings : Entity<Guid>
{
    private CareHomeVisitSettings() { }

    private CareHomeVisitSettings(
        Guid id, CareHomeId facilityId, int visitorCapacity,
        IReadOnlyList<CareHomeVisitTimeWindow> operatingHours,
        IReadOnlyList<CareHomeVisitTimeWindow> visitingWindows,
        IReadOnlyList<CareHomeVisitClosure> closures, DateTime utcNow)
        : base(id)
    {
        FacilityId = facilityId;
        VisitorCapacity = visitorCapacity;
        OperatingHours = operatingHours;
        VisitingWindows = visitingWindows;
        Closures = closures;
        Version = 1;
        UpdatedOnUtc = utcNow;
    }

    public CareHomeId FacilityId { get; private set; }
    public int VisitorCapacity { get; private set; }
    public int Version { get; private set; }
    public IReadOnlyList<CareHomeVisitTimeWindow> OperatingHours { get; private set; } = [];
    public IReadOnlyList<CareHomeVisitTimeWindow> VisitingWindows { get; private set; } = [];
    public IReadOnlyList<CareHomeVisitClosure> Closures { get; private set; } = [];
    public DateTime UpdatedOnUtc { get; private set; }

    public static CareHomeVisitSettings Create(
        CareHomeId facilityId, int visitorCapacity,
        IReadOnlyList<CareHomeVisitTimeWindow> operatingHours,
        IReadOnlyList<CareHomeVisitTimeWindow> visitingWindows,
        IReadOnlyList<CareHomeVisitClosure> closures, DateTime utcNow)
    {
        Validate(facilityId, visitorCapacity, operatingHours, visitingWindows, closures, utcNow);
        return new CareHomeVisitSettings(Guid.CreateVersion7(), facilityId, visitorCapacity,
            Normalize(operatingHours), Normalize(visitingWindows), NormalizeClosures(closures), utcNow);
    }

    public void Update(int expectedVersion, int visitorCapacity,
        IReadOnlyList<CareHomeVisitTimeWindow> operatingHours,
        IReadOnlyList<CareHomeVisitTimeWindow> visitingWindows,
        IReadOnlyList<CareHomeVisitClosure> closures, DateTime utcNow)
    {
        if (Version != expectedVersion) throw new DomainException("Care-home visit settings version is stale.");
        Validate(FacilityId, visitorCapacity, operatingHours, visitingWindows, closures, utcNow);
        VisitorCapacity = visitorCapacity;
        OperatingHours = Normalize(operatingHours);
        VisitingWindows = Normalize(visitingWindows);
        Closures = NormalizeClosures(closures);
        UpdatedOnUtc = utcNow;
        Version++;
    }

    public bool IsClosed(DateOnly date) => Closures.Any(x => x.Date == date);

    public bool Allows(DateOnly date, TimeOnly start, TimeOnly end)
    {
        if (IsClosed(date) || end <= start) return false;
        DayOfWeek day = date.DayOfWeek;
        return OperatingHours.Any(x => x.Day == day && x.StartTime <= start && x.EndTime >= end)
            && VisitingWindows.Any(x => x.Day == day && x.StartTime <= start && x.EndTime >= end);
    }

    private static void Validate(CareHomeId facilityId, int capacity,
        IReadOnlyList<CareHomeVisitTimeWindow> operatingHours,
        IReadOnlyList<CareHomeVisitTimeWindow> visitingWindows,
        IReadOnlyList<CareHomeVisitClosure> closures, DateTime utcNow)
    {
        if (facilityId == CareHomeId.Empty) throw new DomainException("Care-home facility is required.");
        if (capacity < 1) throw new DomainException("Visitor capacity must be positive.");
        if (utcNow.Kind != DateTimeKind.Utc) throw new DomainException("Care-home timestamps must be UTC.");
        if (operatingHours is null || visitingWindows is null || closures is null)
            throw new DomainException("Operating hours, Family visiting windows, and closures are required.");
        ValidateWindows(operatingHours, "Operating hours");
        ValidateWindows(visitingWindows, "Visiting windows");
        if (closures.Any(x => x is null || x.Date == default || (x.Reason?.Length ?? 0) > 500))
            throw new DomainException("Visit closures require a date and a reason no longer than 500 characters.");
        if (closures.Select(x => x.Date).Distinct().Count() != closures.Count)
            throw new DomainException("A visit closure date can only be configured once.");
    }

    private static void ValidateWindows(IReadOnlyList<CareHomeVisitTimeWindow> windows, string label)
    {
        if (windows is null || windows.Any(x => x is null || !Enum.IsDefined(x.Day) || x.EndTime <= x.StartTime))
            throw new DomainException($"{label} must use valid, same-day time ranges.");
        foreach (IGrouping<DayOfWeek, CareHomeVisitTimeWindow> day in windows.GroupBy(x => x.Day))
        {
            CareHomeVisitTimeWindow[] ordered = day.OrderBy(x => x.StartTime).ToArray();
            for (int i = 1; i < ordered.Length; i++)
                if (ordered[i].StartTime < ordered[i - 1].EndTime)
                    throw new DomainException($"{label} cannot overlap on the same day.");
        }
    }

    private static IReadOnlyList<CareHomeVisitTimeWindow> Normalize(IReadOnlyList<CareHomeVisitTimeWindow> value) =>
        value.OrderBy(x => x.Day).ThenBy(x => x.StartTime).ToArray();

    private static IReadOnlyList<CareHomeVisitClosure> NormalizeClosures(IReadOnlyList<CareHomeVisitClosure> value) =>
        value.OrderBy(x => x.Date).Select(x => x with { Reason = string.IsNullOrWhiteSpace(x.Reason) ? null : x.Reason.Trim() }).ToArray();
}

public sealed class CareHomeVisit : AggregateRoot<Guid>
{
    private CareHomeVisit() { }

    private CareHomeVisit(Guid id, CareHomeId facilityId, FamilyId familyId, UserId familyUserId,
        CareHomeVisitKind kind, ElderlyId? elderlyId, string visitorName, string visitorPhone,
        int visitorCount, DateTime startsAtUtc, DateTime utcNow, Guid? rescheduleOfVisitId)
        : base(id)
    {
        FacilityId = facilityId;
        FamilyId = familyId;
        FamilyUserId = familyUserId;
        Kind = kind;
        ElderlyId = elderlyId;
        VisitorName = visitorName;
        VisitorPhone = visitorPhone;
        VisitorCount = visitorCount;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = startsAtUtc.AddHours(1);
        CreatedOnUtc = utcNow;
        UpdatedOnUtc = utcNow;
        DecisionExpiresOnUtc = utcNow.AddHours(24);
        RescheduleOfVisitId = rescheduleOfVisitId;
        Status = rescheduleOfVisitId.HasValue ? CareHomeVisitStatus.ReschedulePending : CareHomeVisitStatus.Pending;
        Version = 1;
    }

    public CareHomeId FacilityId { get; private set; }
    public FamilyId FamilyId { get; private set; }
    public UserId FamilyUserId { get; private set; }
    public CareHomeVisitKind Kind { get; private set; }
    public ElderlyId? ElderlyId { get; private set; }
    public string VisitorName { get; private set; } = string.Empty;
    public string VisitorPhone { get; private set; } = string.Empty;
    public int VisitorCount { get; private set; }
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public CareHomeVisitStatus Status { get; private set; }
    public Guid? RescheduleOfVisitId { get; private set; }
    public DateTime DecisionExpiresOnUtc { get; private set; }
    public UserId? DecidedByUserId { get; private set; }
    public DateTime? DecidedOnUtc { get; private set; }
    public string? DecisionReason { get; private set; }
    public UserId? CancelledByUserId { get; private set; }
    public DateTime? CancelledOnUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public int Version { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }

    public static CareHomeVisit Request(CareHomeId facilityId, FamilyId familyId, UserId familyUserId,
        CareHomeVisitKind kind, ElderlyId? elderlyId, string visitorName, string visitorPhone,
        int visitorCount, DateTime startsAtUtc, DateTime utcNow) =>
        Create(facilityId, familyId, familyUserId, kind, elderlyId, visitorName, visitorPhone,
            visitorCount, startsAtUtc, utcNow, null);

    public static CareHomeVisit RequestReschedule(CareHomeVisit approvedVisit, DateTime startsAtUtc, DateTime utcNow)
    {
        if (approvedVisit.Status != CareHomeVisitStatus.Approved || approvedVisit.StartsAtUtc <= utcNow)
            throw new DomainException("Only a future approved visit can be rescheduled.");
        if (approvedVisit.StartsAtUtc == startsAtUtc)
            throw new DomainException("Choose a different visit time to reschedule.");
        return Create(approvedVisit.FacilityId, approvedVisit.FamilyId, approvedVisit.FamilyUserId,
            approvedVisit.Kind, approvedVisit.ElderlyId, approvedVisit.VisitorName, approvedVisit.VisitorPhone,
            approvedVisit.VisitorCount, startsAtUtc, utcNow, approvedVisit.Id);
    }

    public void Approve(UserId actor, DateTime utcNow)
    {
        EnsurePending(utcNow);
        if (actor == UserId.Empty) throw new DomainException("Decision actor is required.");
        Status = CareHomeVisitStatus.Approved;
        DecidedByUserId = actor;
        DecidedOnUtc = utcNow;
        UpdatedOnUtc = utcNow;
        Version++;
    }

    public void Reject(UserId actor, string reason, DateTime utcNow)
    {
        EnsurePending(utcNow);
        if (actor == UserId.Empty) throw new DomainException("Decision actor is required.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000)
            throw new DomainException("A rejection reason of at most 1000 characters is required.");
        Status = CareHomeVisitStatus.Rejected;
        DecidedByUserId = actor;
        DecidedOnUtc = utcNow;
        DecisionReason = reason.Trim();
        UpdatedOnUtc = utcNow;
        Version++;
    }

    public void Cancel(bool byFacility, UserId actor, string? reason, DateTime utcNow)
    {
        if (Status is not (CareHomeVisitStatus.Pending or CareHomeVisitStatus.Approved or CareHomeVisitStatus.ReschedulePending))
            throw new DomainException("This visit can no longer be cancelled.");
        if (actor == UserId.Empty) throw new DomainException("Cancellation actor is required.");
        if (StartsAtUtc <= utcNow) throw new DomainException("A visit cannot be cancelled after it has started.");
        if ((reason?.Trim().Length ?? 0) > 1000) throw new DomainException("Cancellation reason cannot exceed 1000 characters.");
        Status = byFacility ? CareHomeVisitStatus.FacilityCancelled : CareHomeVisitStatus.FamilyCancelled;
        CancelledByUserId = actor;
        CancelledOnUtc = utcNow;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        UpdatedOnUtc = utcNow;
        Version++;
    }

    public void MarkRescheduled(UserId actor, DateTime utcNow)
    {
        if (Status != CareHomeVisitStatus.Approved || actor == UserId.Empty)
            throw new DomainException("Only an approved visit can be replaced by an approved reschedule.");
        Status = CareHomeVisitStatus.Rescheduled;
        DecidedByUserId = actor;
        DecidedOnUtc = utcNow;
        UpdatedOnUtc = utcNow;
        Version++;
    }

    public bool Expire(DateTime utcNow)
    {
        if (Status is not (CareHomeVisitStatus.Pending or CareHomeVisitStatus.ReschedulePending)
            || DecisionExpiresOnUtc > utcNow) return false;
        Status = CareHomeVisitStatus.Expired;
        UpdatedOnUtc = utcNow;
        Version++;
        return true;
    }

    private void EnsurePending(DateTime utcNow)
    {
        if (Status is not (CareHomeVisitStatus.Pending or CareHomeVisitStatus.ReschedulePending)
            || DecisionExpiresOnUtc <= utcNow || StartsAtUtc <= utcNow)
            throw new DomainException("This visit is not awaiting a decision.");
    }

    private static CareHomeVisit Create(CareHomeId facilityId, FamilyId familyId, UserId familyUserId,
        CareHomeVisitKind kind, ElderlyId? elderlyId, string visitorName, string visitorPhone,
        int visitorCount, DateTime startsAtUtc, DateTime utcNow, Guid? rescheduleOfVisitId)
    {
        if (facilityId == CareHomeId.Empty || familyId == FamilyId.Empty || familyUserId == UserId.Empty)
            throw new DomainException("Facility, Family, and requester are required.");
        if (!Enum.IsDefined(kind) || (kind == CareHomeVisitKind.Resident) != elderlyId.HasValue)
            throw new DomainException("Resident visits require an elderly person; prospective visits must not specify one.");
        if (visitorCount < 1) throw new DomainException("Visitor count must be positive.");
        if (string.IsNullOrWhiteSpace(visitorName) || visitorName.Trim().Length > 200)
            throw new DomainException("Visitor name is required and cannot exceed 200 characters.");
        if (string.IsNullOrWhiteSpace(visitorPhone) || visitorPhone.Trim().Length > 32)
            throw new DomainException("Visitor phone is required and cannot exceed 32 characters.");
        if (startsAtUtc.Kind != DateTimeKind.Utc || utcNow.Kind != DateTimeKind.Utc)
            throw new DomainException("Visit timestamps must be UTC.");
        if (startsAtUtc < utcNow.AddHours(24))
            throw new DomainException("Visits must be requested at least 24 hours in advance.");
        return new CareHomeVisit(Guid.CreateVersion7(), facilityId, familyId, familyUserId, kind,
            elderlyId, visitorName.Trim(), visitorPhone.Trim(), visitorCount, startsAtUtc, utcNow, rescheduleOfVisitId);
    }
}
