using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.Modules.CareHomes.Application.Visits;

/// <summary>
/// F6 operational read of Care Home visit requests for SuperAdmin/SupportAdmin.
/// Read-only: no Admin scheduling, approval, rejection, cancellation or override
/// route exists. The projection adds the facility's approved display name and
/// the requesting family's name, and deliberately omits visitor name, visitor
/// phone (PII), visitor count, the requesting family user id and the pending
/// decision deadline.
/// </summary>
public sealed record AdminCareHomeVisitListItem(
    Guid VisitId,
    Guid FacilityId,
    string? FacilityName,
    Guid FamilyId,
    string? FamilyName,
    Guid? ElderlyId,
    CareHomeVisitKind Kind,
    CareHomeVisitStatus Status,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    Guid? RescheduleOfVisitId,
    Guid? DecidedByUserId,
    DateTime? DecidedOnUtc,
    string? DecisionReason,
    Guid? CancelledByUserId,
    DateTime? CancelledOnUtc,
    string? CancellationReason,
    DateTime CreatedOnUtc);

/// <summary>
/// Visit detail: the list projection plus the persisted last-updated instant.
/// </summary>
public sealed record AdminCareHomeVisitDetail(
    Guid VisitId,
    Guid FacilityId,
    string? FacilityName,
    Guid FamilyId,
    string? FamilyName,
    Guid? ElderlyId,
    CareHomeVisitKind Kind,
    CareHomeVisitStatus Status,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    Guid? RescheduleOfVisitId,
    Guid? DecidedByUserId,
    DateTime? DecidedOnUtc,
    string? DecisionReason,
    Guid? CancelledByUserId,
    DateTime? CancelledOnUtc,
    string? CancellationReason,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc);

/// <summary>
/// A page of visits with the actual filtered total count. Paging is clamped
/// (never an error): page is at least 1, page size defaults to 25 and is capped
/// at 100.
/// </summary>
public sealed record AdminCareHomeVisitPage(
    IReadOnlyList<AdminCareHomeVisitListItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>
/// Bounded visit read. <c>FacilityId</c>, <c>Status</c> and the scheduled window
/// (<c>From</c>/<c>To</c> as inclusive UTC days, resolved as a half-open
/// overlap) narrow the page; an empty facility id, an undefined status and an
/// inverted window are the only rejected inputs.
/// </summary>
public sealed record ListAdminCareHomeVisitsQuery(
    int Page = 1,
    int PageSize = 25,
    Guid? FacilityId = null,
    CareHomeVisitStatus? Status = null,
    DateOnly? From = null,
    DateOnly? To = null) : IQuery<AdminCareHomeVisitPage>;

public sealed record GetAdminCareHomeVisitQuery(Guid VisitId) : IQuery<AdminCareHomeVisitDetail>;

public static class AdminCareHomeVisitErrors
{
    /// <summary>
    /// Reuses the existing admin-read rejection code (already mapped to 400) so
    /// no new error code is introduced by this read.
    /// </summary>
    public static readonly Error InvalidQuery =
        new("CareHomes.Admin.InvalidQuery", "The visit query is invalid.");
}

public sealed class ListAdminCareHomeVisitsQueryHandler(
    ICareHomesDbContext dbContext,
    IFamiliesDbContext familiesDbContext)
    : IQueryHandler<ListAdminCareHomeVisitsQuery, AdminCareHomeVisitPage>
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public async Task<Result<AdminCareHomeVisitPage>> Handle(
        ListAdminCareHomeVisitsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.FacilityId == Guid.Empty
            || (request.Status is { } status && !Enum.IsDefined(status))
            || (request.From is { } from && request.To is { } to && to < from))
        {
            return Result<AdminCareHomeVisitPage>.Failure(AdminCareHomeVisitErrors.InvalidQuery);
        }

        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(
            request.PageSize <= 0 ? DefaultPageSize : request.PageSize, 1, MaxPageSize);

        IQueryable<CareHomeVisit> query = dbContext.Visits.AsNoTracking();

        if (request.FacilityId is { } facilityId)
        {
            var filterFacilityId = new CareHomeId(facilityId);
            query = query.Where(v => v.FacilityId == filterFacilityId);
        }

        if (request.Status is { } visitStatus)
        {
            query = query.Where(v => v.Status == visitStatus);
        }

        // Inclusive UTC days against the scheduled window: a visit is in range
        // when it ends after the window opens and starts before it closes.
        if (request.From is { } fromDay)
        {
            DateTime fromInclusiveUtc = fromDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(v => v.EndsAtUtc > fromInclusiveUtc);
        }

        if (request.To is { } toDay)
        {
            DateTime toExclusiveUtc = toDay == DateOnly.MaxValue
                ? DateTime.MaxValue
                : toDay.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(v => v.StartsAtUtc < toExclusiveUtc);
        }

        int totalCount = await query.CountAsync(cancellationToken);
        int skip = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);

        // Materialize the stored row shape first and project the wire row from it,
        // so the optional id columns are read as plain nullable columns and no
        // nullable value-object conditional has to be translated.
        var rows = await query
            .OrderByDescending(v => v.StartsAtUtc)
            .ThenBy(v => v.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(v => new
            {
                VisitId = v.Id,
                v.FacilityId,
                v.FamilyId,
                v.ElderlyId,
                v.Kind,
                v.Status,
                v.StartsAtUtc,
                v.EndsAtUtc,
                v.RescheduleOfVisitId,
                v.DecidedByUserId,
                v.DecidedOnUtc,
                v.DecisionReason,
                v.CancelledByUserId,
                v.CancelledOnUtc,
                v.CancellationReason,
                v.CreatedOnUtc
            })
            .ToListAsync(cancellationToken);

        // One batched name lookup per side for the whole page (never per row):
        // the facility's approved display name and the family's name.
        IReadOnlyDictionary<Guid, string> facilityNames =
            await AdminCareHomeVisitNames.ResolveFacilityNamesAsync(
                dbContext,
                rows.Select(v => v.FacilityId).ToArray(),
                cancellationToken);

        IReadOnlyDictionary<Guid, string> familyNames =
            await AdminCareHomeVisitNames.ResolveFamilyNamesAsync(
                familiesDbContext,
                rows.Select(v => v.FamilyId).ToArray(),
                cancellationToken);

        List<AdminCareHomeVisitListItem> items = rows
            .Select(v => new AdminCareHomeVisitListItem(
                v.VisitId,
                v.FacilityId.Value,
                AdminCareHomeVisitNames.Lookup(facilityNames, v.FacilityId.Value),
                v.FamilyId.Value,
                AdminCareHomeVisitNames.Lookup(familyNames, v.FamilyId.Value),
                v.ElderlyId?.Value,
                v.Kind,
                v.Status,
                v.StartsAtUtc,
                v.EndsAtUtc,
                v.RescheduleOfVisitId,
                v.DecidedByUserId?.Value,
                v.DecidedOnUtc,
                v.DecisionReason,
                v.CancelledByUserId?.Value,
                v.CancelledOnUtc,
                v.CancellationReason,
                v.CreatedOnUtc))
            .ToList();

        return new AdminCareHomeVisitPage(items, page, pageSize, totalCount);
    }
}

public sealed class GetAdminCareHomeVisitQueryHandler(
    ICareHomesDbContext dbContext,
    IFamiliesDbContext familiesDbContext)
    : IQueryHandler<GetAdminCareHomeVisitQuery, AdminCareHomeVisitDetail>
{
    public async Task<Result<AdminCareHomeVisitDetail>> Handle(
        GetAdminCareHomeVisitQuery request,
        CancellationToken cancellationToken)
    {
        CareHomeVisit? visit = await dbContext.Visits
            .AsNoTracking()
            .SingleOrDefaultAsync(v => v.Id == request.VisitId, cancellationToken);

        if (visit is null)
        {
            return CareHomeVisitErrors.NotFound;
        }

        IReadOnlyDictionary<Guid, string> facilityNames =
            await AdminCareHomeVisitNames.ResolveFacilityNamesAsync(
                dbContext,
                [visit.FacilityId],
                cancellationToken);

        IReadOnlyDictionary<Guid, string> familyNames =
            await AdminCareHomeVisitNames.ResolveFamilyNamesAsync(
                familiesDbContext,
                [visit.FamilyId],
                cancellationToken);

        return new AdminCareHomeVisitDetail(
            visit.Id,
            visit.FacilityId.Value,
            AdminCareHomeVisitNames.Lookup(facilityNames, visit.FacilityId.Value),
            visit.FamilyId.Value,
            AdminCareHomeVisitNames.Lookup(familyNames, visit.FamilyId.Value),
            visit.ElderlyId?.Value,
            visit.Kind,
            visit.Status,
            visit.StartsAtUtc,
            visit.EndsAtUtc,
            visit.RescheduleOfVisitId,
            visit.DecidedByUserId?.Value,
            visit.DecidedOnUtc,
            visit.DecisionReason,
            visit.CancelledByUserId?.Value,
            visit.CancelledOnUtc,
            visit.CancellationReason,
            visit.CreatedOnUtc,
            visit.UpdatedOnUtc);
    }
}

/// <summary>
/// Batched name enrichment for the admin visit register. A facility name is its
/// approved revision's display name (English preferred, Arabic fallback — the
/// same rule <c>ApiChatFacilityLookup</c> applies), and a family name is the
/// Families aggregate's name. A facility with no approved revision, or a family
/// row that cannot be resolved, contributes no name (<c>null</c>) rather than an
/// invented placeholder.
/// </summary>
internal static class AdminCareHomeVisitNames
{
    internal static async Task<IReadOnlyDictionary<Guid, string>> ResolveFacilityNamesAsync(
        ICareHomesDbContext dbContext,
        IReadOnlyCollection<CareHomeId> facilityIds,
        CancellationToken cancellationToken)
    {
        CareHomeId[] distinct = facilityIds.Distinct().ToArray();
        var names = new Dictionary<Guid, string>();

        if (distinct.Length == 0)
        {
            return names;
        }

        List<CareHomeFacility> facilities = await dbContext.Facilities
            .AsNoTracking()
            .Where(facility => distinct.Contains(facility.Id))
            .Include(facility => facility.Revisions)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        foreach (CareHomeFacility facility in facilities)
        {
            if (ApprovedName(facility) is { } name)
            {
                names[facility.Id.Value] = name;
            }
        }

        return names;
    }

    internal static async Task<IReadOnlyDictionary<Guid, string>> ResolveFamilyNamesAsync(
        IFamiliesDbContext dbContext,
        IReadOnlyCollection<FamilyId> familyIds,
        CancellationToken cancellationToken)
    {
        FamilyId[] distinct = familyIds.Distinct().ToArray();
        var names = new Dictionary<Guid, string>();

        if (distinct.Length == 0)
        {
            return names;
        }

        List<Family> families = await dbContext.Families
            .AsNoTracking()
            .Where(family => distinct.Contains(family.Id))
            .ToListAsync(cancellationToken);

        foreach (Family family in families)
        {
            // A deleted family is retained and its stored name is reported
            // (the Families admin read does the same); only a missing row
            // leaves the name null.
            if (!string.IsNullOrWhiteSpace(family.Name))
            {
                names[family.Id.Value] = family.Name;
            }
        }

        return names;
    }

    internal static string? Lookup(IReadOnlyDictionary<Guid, string> names, Guid id) =>
        names.TryGetValue(id, out string? name) ? name : null;

    private static string? ApprovedName(CareHomeFacility facility)
    {
        if (facility.ApprovedRevisionId is not Guid approvedRevisionId)
        {
            return null;
        }

        CareHomeProfileRevision? revision = facility.Revisions.FirstOrDefault(
            item => item.Id == approvedRevisionId && item.ApprovedOnUtc is not null);

        if (revision is null)
        {
            return null;
        }

        string name = string.IsNullOrWhiteSpace(revision.EnglishName)
            ? revision.ArabicName
            : revision.EnglishName;

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
