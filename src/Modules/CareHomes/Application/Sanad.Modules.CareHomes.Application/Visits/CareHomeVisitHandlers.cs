using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Application.Discovery;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Application.Visits;

public sealed record GetCareHomeVisitAvailabilityQuery(Guid FacilityId, DateOnly Date, int VisitorCount, DateTime UtcNow)
    : IQuery<CareHomeVisitAvailabilityResponse>;
public sealed record GetOwnerCareHomeVisitSettingsQuery(UserId Actor) : IQuery<CareHomeVisitSettingsResponse>;
public sealed record UpdateOwnerCareHomeVisitSettingsCommand(UserId Actor, int ExpectedVersion, int VisitorCapacity,
    IReadOnlyList<CareHomeVisitTimeWindow> OperatingHours,
    IReadOnlyList<CareHomeVisitTimeWindow> VisitingWindows,
    IReadOnlyList<CareHomeVisitClosure> Closures, DateTime UtcNow) : ICommand<CareHomeVisitSettingsResponse>;
public sealed record CreateCareHomeVisitCommand(UserId Actor, FamilyId FamilyId, Guid FacilityId,
    CareHomeVisitKind Kind, Guid? ElderlyId, string VisitorName, string VisitorPhone,
    int VisitorCount, DateTimeOffset StartsAt, DateTime UtcNow) : ICommand<CareHomeVisitResponse>;
public sealed record ListFamilyCareHomeVisitsQuery(FamilyId FamilyId, DateTime UtcNow)
    : IQuery<IReadOnlyList<CareHomeVisitResponse>>;
public sealed record GetFamilyCareHomeVisitQuery(FamilyId FamilyId, Guid VisitId)
    : IQuery<CareHomeVisitResponse>;
public sealed record CancelFamilyCareHomeVisitCommand(FamilyId FamilyId, Guid VisitId, UserId Actor,
    string? Reason, DateTime UtcNow) : ICommand<CareHomeVisitResponse>;
public sealed record RequestCareHomeVisitRescheduleCommand(FamilyId FamilyId, Guid VisitId, UserId Actor,
    DateTimeOffset StartsAt, DateTime UtcNow) : ICommand<CareHomeVisitResponse>;
public sealed record ListOwnerCareHomeVisitsQuery(UserId Actor, DateTime UtcNow)
    : IQuery<IReadOnlyList<CareHomeVisitResponse>>;
public sealed record DecideOwnerCareHomeVisitCommand(UserId Actor, Guid VisitId, bool Approve,
    string? Reason, DateTime UtcNow) : ICommand<CareHomeVisitResponse>;
public sealed record CancelOwnerCareHomeVisitCommand(UserId Actor, Guid VisitId, string? Reason,
    DateTime UtcNow) : ICommand<CareHomeVisitResponse>;

public static class CareHomeVisitErrors
{
    public static readonly Error NotFound = new("CareHomes.Visits.NotFound", "The care-home visit was not found.");
    public static readonly Error InvalidRequest = new("CareHomes.Visits.InvalidRequest", "The visit request is invalid.");
    public static readonly Error InvalidState = new("CareHomes.Visits.InvalidState", "The visit is not in a state that allows this action.");
    public static readonly Error Conflict = new("CareHomes.Visits.Conflict", "The visit or settings changed; reload and try again.");
    public static readonly Error CapacityConflict = new("CareHomes.Visits.CapacityConflict", "The selected time no longer has enough visitor capacity.");
}

public sealed class GetCareHomeVisitAvailabilityQueryHandler(ICareHomesDbContext db)
    : IQueryHandler<GetCareHomeVisitAvailabilityQuery, CareHomeVisitAvailabilityResponse>
{
    public async Task<Result<CareHomeVisitAvailabilityResponse>> Handle(GetCareHomeVisitAvailabilityQuery r, CancellationToken ct)
    {
        if (r.FacilityId == Guid.Empty || r.Date == default || r.VisitorCount < 1 || r.UtcNow.Kind != DateTimeKind.Utc)
            return Result<CareHomeVisitAvailabilityResponse>.Failure(CareHomeVisitErrors.InvalidRequest);
        var facilityId = new CareHomeId(r.FacilityId);
        var facility = await db.Facilities.AsNoTracking().Include(x => x.Revisions).Include(x => x.Documents)
            .SingleOrDefaultAsync(x => x.Id == facilityId && x.Status == CareHomeStatus.Approved, ct);
        DateOnly today = CareHomePublicEligibility.CairoDate(r.UtcNow);
        if (facility is null || !CareHomePublicEligibility.IsEligible(facility, today))
            return Result<CareHomeVisitAvailabilityResponse>.Failure(new Error("CareHomes.Discovery.NotFound", "The selected care home was not found."));
        var settings = await db.VisitSettings.AsNoTracking().SingleOrDefaultAsync(x => x.FacilityId == facilityId, ct);
        if (settings is null)
            return Result<CareHomeVisitAvailabilityResponse>.Success(new(r.FacilityId, r.Date,
                CareHomeVisitScheduleRules.CairoTimeZone.Id, r.VisitorCount, null, []));
        if (r.VisitorCount > settings.VisitorCapacity)
            return Result<CareHomeVisitAvailabilityResponse>.Failure(CareHomeVisitErrors.InvalidRequest);

        // Query a broad UTC envelope so Cairo midnight transitions never require
        // converting an ambiguous/nonexistent local midnight.
        DateTime fromUtc = DateTime.SpecifyKind(r.Date.ToDateTime(TimeOnly.MinValue).AddDays(-1), DateTimeKind.Utc);
        DateTime toUtc = DateTime.SpecifyKind(r.Date.ToDateTime(TimeOnly.MinValue).AddDays(2), DateTimeKind.Utc);
        List<CareHomeVisit> reservations = await db.Visits.AsNoTracking()
            .Where(x => x.FacilityId == facilityId && x.StartsAtUtc < toUtc && x.EndsAtUtc > fromUtc
                && (x.Status == CareHomeVisitStatus.Approved
                    || (x.Status == CareHomeVisitStatus.Pending || x.Status == CareHomeVisitStatus.ReschedulePending)
                    && x.DecisionExpiresOnUtc > r.UtcNow))
            .ToListAsync(ct);
        var slots = CareHomeVisitScheduleRules.GetSlots(settings, r.Date, r.VisitorCount, reservations, r.UtcNow);
        return Result<CareHomeVisitAvailabilityResponse>.Success(new(r.FacilityId, r.Date,
            CareHomeVisitScheduleRules.CairoTimeZone.Id, r.VisitorCount, settings.VisitorCapacity, slots));
    }
}

public sealed class GetOwnerCareHomeVisitSettingsQueryHandler(ICareHomesDbContext db)
    : IQueryHandler<GetOwnerCareHomeVisitSettingsQuery, CareHomeVisitSettingsResponse>
{
    public async Task<Result<CareHomeVisitSettingsResponse>> Handle(GetOwnerCareHomeVisitSettingsQuery r, CancellationToken ct)
    {
        var facility = await db.Facilities.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == r.Actor, ct);
        if (facility is null) return Result<CareHomeVisitSettingsResponse>.Failure(CareHomeVisitErrors.NotFound);
        var value = await db.VisitSettings.AsNoTracking().SingleOrDefaultAsync(x => x.FacilityId == facility.Id, ct);
        return Result<CareHomeVisitSettingsResponse>.Success(value?.ToResponse()
            ?? new(facility.Id.Value, 0, 0, [], [], []));
    }
}

public sealed class UpdateOwnerCareHomeVisitSettingsCommandHandler(
    ICareHomesDbContext db, ICareHomeVisitReservationGuard guard)
    : ICommandHandler<UpdateOwnerCareHomeVisitSettingsCommand, CareHomeVisitSettingsResponse>
{
    public async Task<Result<CareHomeVisitSettingsResponse>> Handle(UpdateOwnerCareHomeVisitSettingsCommand r, CancellationToken ct)
    {
        var facility = await db.Facilities.SingleOrDefaultAsync(x => x.OwnerUserId == r.Actor, ct);
        if (facility is null) return Result<CareHomeVisitSettingsResponse>.Failure(CareHomeVisitErrors.NotFound);
        if (facility.Status is CareHomeStatus.PendingReview or CareHomeStatus.Suspended)
            return Result<CareHomeVisitSettingsResponse>.Failure(CareHomeVisitErrors.InvalidState);
        try
        {
            return await guard.ExecuteAsync(facility.Id, async () =>
            {
                CareHomeVisitSettings proposed;
                try { proposed = CareHomeVisitSettings.Create(facility.Id, r.VisitorCapacity, r.OperatingHours, r.VisitingWindows, r.Closures, r.UtcNow); }
                catch (DomainException) { return Result<CareHomeVisitSettingsResponse>.Failure(CareHomeVisitErrors.InvalidRequest); }

                List<CareHomeVisit> active = await db.Visits.Where(x => x.FacilityId == facility.Id && x.StartsAtUtc > r.UtcNow
                    && (x.Status == CareHomeVisitStatus.Approved
                        || (x.Status == CareHomeVisitStatus.Pending || x.Status == CareHomeVisitStatus.ReschedulePending)
                        && x.DecisionExpiresOnUtc > r.UtcNow)).ToListAsync(ct);
                if (active.Any(x => !CareHomeVisitScheduleRules.IsAllowedStart(proposed, x.StartsAtUtc)))
                    return Result<CareHomeVisitSettingsResponse>.Failure(CareHomeVisitErrors.Conflict);
                if (active.Any(x => CareHomeVisitScheduleRules.ReservedVisitorCount(active, x.StartsAtUtc, x.EndsAtUtc, r.UtcNow) > proposed.VisitorCapacity))
                    return Result<CareHomeVisitSettingsResponse>.Failure(CareHomeVisitErrors.Conflict);

                var existing = await db.VisitSettings.SingleOrDefaultAsync(x => x.FacilityId == facility.Id, ct);
                if (existing is null)
                {
                    if (r.ExpectedVersion != 0) return Result<CareHomeVisitSettingsResponse>.Failure(CareHomeVisitErrors.Conflict);
                    db.VisitSettings.Add(proposed);
                    existing = proposed;
                }
                else
                {
                    try { existing.Update(r.ExpectedVersion, r.VisitorCapacity, r.OperatingHours, r.VisitingWindows, r.Closures, r.UtcNow); }
                    catch (DomainException) { return Result<CareHomeVisitSettingsResponse>.Failure(CareHomeVisitErrors.Conflict); }
                }
                await db.SaveChangesAsync(ct);
                return Result<CareHomeVisitSettingsResponse>.Success(existing.ToResponse());
            }, ct);
        }
        catch (CareHomeVisitCapacityConflictException)
        {
            return Result<CareHomeVisitSettingsResponse>.Failure(CareHomeVisitErrors.Conflict);
        }
    }
}

public sealed class CreateCareHomeVisitCommandHandler(
    ICareHomesDbContext db, ICareHomeVisitReservationGuard guard)
    : ICommandHandler<CreateCareHomeVisitCommand, CareHomeVisitResponse>
{
    public async Task<Result<CareHomeVisitResponse>> Handle(CreateCareHomeVisitCommand r, CancellationToken ct)
    {
        var facilityId = new CareHomeId(r.FacilityId);
        var facility = await db.Facilities.AsNoTracking().Include(x => x.Revisions).Include(x => x.Documents)
            .SingleOrDefaultAsync(x => x.Id == facilityId && x.Status == CareHomeStatus.Approved, ct);
        if (facility is null || !CareHomePublicEligibility.IsEligible(facility, CareHomePublicEligibility.CairoDate(r.UtcNow)))
            return Result<CareHomeVisitResponse>.Failure(new Error("CareHomes.Discovery.NotFound", "The selected care home was not found."));
        var settings = await db.VisitSettings.AsNoTracking().SingleOrDefaultAsync(x => x.FacilityId == facilityId, ct);
        DateTime startsAtUtc = r.StartsAt.UtcDateTime;
        DateOnly localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(startsAtUtc, CareHomeVisitScheduleRules.CairoTimeZone));
        if (settings is null || !CareHomeVisitScheduleRules.IsAllowedStart(settings, startsAtUtc)
            || startsAtUtc < r.UtcNow.AddHours(24) || r.VisitorCount > settings.VisitorCapacity)
            return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.InvalidRequest);
        if (r.Kind == CareHomeVisitKind.Resident)
        {
            if (r.ElderlyId is not Guid elderlyId || !await db.Bookings.AsNoTracking().AnyAsync(x =>
                    x.FacilityId == facilityId && x.FamilyId == r.FamilyId && x.ElderlyId == new ElderlyId(elderlyId)
                    && x.Status == CareHomeBookingStatus.Accepted && x.PaymentStatus == CareHomeBookingPaymentStatus.Paid
                    && x.StartDate <= localDate && x.EndDate > localDate && x.ActualCheckOutOnUtc == null, ct))
                return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.NotFound);
        }
        else if (r.ElderlyId.HasValue)
            return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.InvalidRequest);

        try
        {
            return await guard.ExecuteAsync(facilityId, async () =>
            {
                settings = await db.VisitSettings.AsNoTracking().SingleOrDefaultAsync(x => x.FacilityId == facilityId, ct);
                if (settings is null || !CareHomeVisitScheduleRules.IsAllowedStart(settings, startsAtUtc)
                    || startsAtUtc < r.UtcNow.AddHours(24) || r.VisitorCount > settings.VisitorCapacity)
                    return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.InvalidRequest);
                List<CareHomeVisit> active = await db.Visits.Where(x => x.FacilityId == facilityId
                    && x.StartsAtUtc < startsAtUtc.AddHours(1) && x.EndsAtUtc > startsAtUtc
                    && (x.Status == CareHomeVisitStatus.Approved
                        || (x.Status == CareHomeVisitStatus.Pending || x.Status == CareHomeVisitStatus.ReschedulePending)
                        && x.DecisionExpiresOnUtc > r.UtcNow)).ToListAsync(ct);
                if (CareHomeVisitScheduleRules.ReservedVisitorCount(active, startsAtUtc, startsAtUtc.AddHours(1), r.UtcNow) + r.VisitorCount > settings.VisitorCapacity)
                    return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.CapacityConflict);
                CareHomeVisit visit;
                try { visit = CareHomeVisit.Request(facilityId, r.FamilyId, r.Actor, r.Kind, r.ElderlyId is Guid id ? new ElderlyId(id) : null,
                    r.VisitorName, r.VisitorPhone, r.VisitorCount, startsAtUtc, r.UtcNow); }
                catch (DomainException) { return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.InvalidRequest); }
                db.Visits.Add(visit);
                await db.SaveChangesAsync(ct);
                return Result<CareHomeVisitResponse>.Success(visit.ToResponse());
            }, ct);
        }
        catch (CareHomeVisitCapacityConflictException)
        {
            return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.CapacityConflict);
        }
    }
}

public sealed class ListFamilyCareHomeVisitsQueryHandler(ICareHomesDbContext db)
    : IQueryHandler<ListFamilyCareHomeVisitsQuery, IReadOnlyList<CareHomeVisitResponse>>
{
    public async Task<Result<IReadOnlyList<CareHomeVisitResponse>>> Handle(ListFamilyCareHomeVisitsQuery r, CancellationToken ct)
    {
        var rows = await db.Visits.AsNoTracking().Where(x => x.FamilyId == r.FamilyId)
            .OrderByDescending(x => x.StartsAtUtc).ToListAsync(ct);
        return Result<IReadOnlyList<CareHomeVisitResponse>>.Success(rows.Select(x => x.ToResponse()).ToArray());
    }
}

public sealed class GetFamilyCareHomeVisitQueryHandler(ICareHomesDbContext db)
    : IQueryHandler<GetFamilyCareHomeVisitQuery, CareHomeVisitResponse>
{
    public async Task<Result<CareHomeVisitResponse>> Handle(GetFamilyCareHomeVisitQuery r, CancellationToken ct)
    {
        var value = await db.Visits.AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.VisitId && x.FamilyId == r.FamilyId, ct);
        return value is null ? Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.NotFound)
            : Result<CareHomeVisitResponse>.Success(value.ToResponse());
    }
}

public sealed class CancelFamilyCareHomeVisitCommandHandler(ICareHomesDbContext db, ICareHomeVisitReservationGuard guard)
    : ICommandHandler<CancelFamilyCareHomeVisitCommand, CareHomeVisitResponse>
{
    public async Task<Result<CareHomeVisitResponse>> Handle(CancelFamilyCareHomeVisitCommand r, CancellationToken ct)
    {
        var visit = await db.Visits.SingleOrDefaultAsync(x => x.Id == r.VisitId && x.FamilyId == r.FamilyId, ct);
        if (visit is null) return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.NotFound);
        try
        {
            return await guard.ExecuteAsync(visit.FacilityId, async () =>
            {
                try
                {
                    visit.Cancel(false, r.Actor, r.Reason, r.UtcNow);
                    List<CareHomeVisit> pendingReschedules = await db.Visits.Where(x => x.RescheduleOfVisitId == visit.Id
                        && x.Status == CareHomeVisitStatus.ReschedulePending).ToListAsync(ct);
                    foreach (CareHomeVisit pending in pendingReschedules) pending.Cancel(false, r.Actor, "Original visit cancelled.", r.UtcNow);
                }
                catch (DomainException) { return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.InvalidState); }
                await db.SaveChangesAsync(ct);
                return Result<CareHomeVisitResponse>.Success(visit.ToResponse());
            }, ct);
        }
        catch (CareHomeVisitCapacityConflictException) { return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.Conflict); }
    }
}

public sealed class RequestCareHomeVisitRescheduleCommandHandler(ICareHomesDbContext db, ICareHomeVisitReservationGuard guard)
    : ICommandHandler<RequestCareHomeVisitRescheduleCommand, CareHomeVisitResponse>
{
    public async Task<Result<CareHomeVisitResponse>> Handle(RequestCareHomeVisitRescheduleCommand r, CancellationToken ct)
    {
        var original = await db.Visits.SingleOrDefaultAsync(x => x.Id == r.VisitId && x.FamilyId == r.FamilyId, ct);
        if (original is null) return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.NotFound);
        DateTime start = r.StartsAt.UtcDateTime;
        var settings = await db.VisitSettings.AsNoTracking().SingleOrDefaultAsync(x => x.FacilityId == original.FacilityId, ct);
        if (settings is null || !CareHomeVisitScheduleRules.IsAllowedStart(settings, start)
            || start < r.UtcNow.AddHours(24) || original.VisitorCount > settings.VisitorCapacity)
            return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.InvalidRequest);
        try
        {
            return await guard.ExecuteAsync(original.FacilityId, async () =>
            {
                settings = await db.VisitSettings.AsNoTracking().SingleOrDefaultAsync(x => x.FacilityId == original.FacilityId, ct);
                if (settings is null || !CareHomeVisitScheduleRules.IsAllowedStart(settings, start)
                    || start < r.UtcNow.AddHours(24) || original.VisitorCount > settings.VisitorCapacity)
                    return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.InvalidRequest);
                if (await db.Visits.AnyAsync(x => x.RescheduleOfVisitId == original.Id && x.Status == CareHomeVisitStatus.ReschedulePending, ct))
                    return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.Conflict);
                List<CareHomeVisit> active = await db.Visits.Where(x => x.FacilityId == original.FacilityId
                    && x.StartsAtUtc < start.AddHours(1) && x.EndsAtUtc > start
                    && (x.Status == CareHomeVisitStatus.Approved
                        || (x.Status == CareHomeVisitStatus.Pending || x.Status == CareHomeVisitStatus.ReschedulePending)
                        && x.DecisionExpiresOnUtc > r.UtcNow)).ToListAsync(ct);
                if (CareHomeVisitScheduleRules.ReservedVisitorCount(active, start, start.AddHours(1), r.UtcNow) + original.VisitorCount > settings.VisitorCapacity)
                    return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.CapacityConflict);
                CareHomeVisit reschedule;
                try { reschedule = CareHomeVisit.RequestReschedule(original, start, r.UtcNow); }
                catch (DomainException) { return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.InvalidState); }
                db.Visits.Add(reschedule);
                await db.SaveChangesAsync(ct);
                return Result<CareHomeVisitResponse>.Success(reschedule.ToResponse());
            }, ct);
        }
        catch (CareHomeVisitCapacityConflictException) { return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.CapacityConflict); }
    }
}

public sealed class ListOwnerCareHomeVisitsQueryHandler(ICareHomesDbContext db)
    : IQueryHandler<ListOwnerCareHomeVisitsQuery, IReadOnlyList<CareHomeVisitResponse>>
{
    public async Task<Result<IReadOnlyList<CareHomeVisitResponse>>> Handle(ListOwnerCareHomeVisitsQuery r, CancellationToken ct)
    {
        var facility = await db.Facilities.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == r.Actor, ct);
        if (facility is null) return Result<IReadOnlyList<CareHomeVisitResponse>>.Failure(CareHomeVisitErrors.NotFound);
        var rows = await db.Visits.AsNoTracking().Where(x => x.FacilityId == facility.Id)
            .OrderBy(x => x.StartsAtUtc).ToListAsync(ct);
        return Result<IReadOnlyList<CareHomeVisitResponse>>.Success(rows.Select(x => x.ToResponse()).ToArray());
    }
}

public sealed class DecideOwnerCareHomeVisitCommandHandler(ICareHomesDbContext db, ICareHomeVisitReservationGuard guard)
    : ICommandHandler<DecideOwnerCareHomeVisitCommand, CareHomeVisitResponse>
{
    public async Task<Result<CareHomeVisitResponse>> Handle(DecideOwnerCareHomeVisitCommand r, CancellationToken ct)
    {
        var visit = await db.Visits.SingleOrDefaultAsync(x => x.Id == r.VisitId, ct);
        if (visit is null) return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.NotFound);
        if (!await db.Facilities.AnyAsync(x => x.Id == visit.FacilityId && x.OwnerUserId == r.Actor, ct))
            return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.NotFound);
        try
        {
            return await guard.ExecuteAsync(visit.FacilityId, async () =>
            {
                try
                {
                    if (r.Approve)
                    {
                        if (visit.RescheduleOfVisitId is Guid originalId)
                        {
                            CareHomeVisit? original = await db.Visits.SingleOrDefaultAsync(x => x.Id == originalId
                                && x.Status == CareHomeVisitStatus.Approved, ct);
                            if (original is null || original.StartsAtUtc <= r.UtcNow)
                                return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.Conflict);
                            visit.Approve(r.Actor, r.UtcNow);
                            original.MarkRescheduled(r.Actor, r.UtcNow);
                        }
                        else visit.Approve(r.Actor, r.UtcNow);
                    }
                    else visit.Reject(r.Actor, r.Reason ?? string.Empty, r.UtcNow);
                }
                catch (DomainException) { return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.InvalidState); }
                await db.SaveChangesAsync(ct);
                return Result<CareHomeVisitResponse>.Success(visit.ToResponse());
            }, ct);
        }
        catch (CareHomeVisitCapacityConflictException) { return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.Conflict); }
    }
}

public sealed class CancelOwnerCareHomeVisitCommandHandler(ICareHomesDbContext db, ICareHomeVisitReservationGuard guard)
    : ICommandHandler<CancelOwnerCareHomeVisitCommand, CareHomeVisitResponse>
{
    public async Task<Result<CareHomeVisitResponse>> Handle(CancelOwnerCareHomeVisitCommand r, CancellationToken ct)
    {
        var visit = await db.Visits.SingleOrDefaultAsync(x => x.Id == r.VisitId, ct);
        if (visit is null || !await db.Facilities.AnyAsync(x => x.Id == visit.FacilityId && x.OwnerUserId == r.Actor, ct))
            return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.NotFound);
        try
        {
            return await guard.ExecuteAsync(visit.FacilityId, async () =>
            {
                try
                {
                    visit.Cancel(true, r.Actor, r.Reason, r.UtcNow);
                    List<CareHomeVisit> pendingReschedules = await db.Visits.Where(x => x.RescheduleOfVisitId == visit.Id
                        && x.Status == CareHomeVisitStatus.ReschedulePending).ToListAsync(ct);
                    foreach (CareHomeVisit pending in pendingReschedules) pending.Cancel(true, r.Actor, "Original visit cancelled.", r.UtcNow);
                }
                catch (DomainException) { return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.InvalidState); }
                await db.SaveChangesAsync(ct);
                return Result<CareHomeVisitResponse>.Success(visit.ToResponse());
            }, ct);
        }
        catch (CareHomeVisitCapacityConflictException) { return Result<CareHomeVisitResponse>.Failure(CareHomeVisitErrors.Conflict); }
    }
}
