using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Identity;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.Modules.Families.Application.Families;

/// <summary>
/// F6 operational read of families for SuperAdmin/SupportAdmin (read-only, no
/// write route and no impersonation). The projection is deliberately limited to
/// the pinned identity/association facts: family identity, owner, member count,
/// the created and (for a deleted family) deletion instants, each member's user
/// id, role, relationship, inviter, join instant and Identity account status,
/// and each dependent's id, Identity user id and relationship. Medical data,
/// notes, address, phone, health/assessment data, member display names, emails,
/// avatars and any deletion reason/message are never projected.
/// </summary>
public sealed record AdminFamilyListItem(
    Guid FamilyId,
    string Name,
    Guid OwnerUserId,
    int MemberCount,
    DateTime CreatedOnUtc,
    DateTime? DeletedOnUtc);

/// <summary>
/// One family member row. <c>Status</c> is the Identity account status resolved
/// through the existing family-member identity seam and is <c>0</c> when the
/// profile cannot be resolved.
/// </summary>
public sealed record AdminFamilyMemberItem(
    Guid UserId,
    FamilyRole Role,
    FamilyRelationshipType RelationshipType,
    Guid AddedByUserId,
    DateTime JoinedOnUtc,
    int Status);

/// <summary>
/// One dependent (elderly) association of the family: ids and relationship only.
/// </summary>
public sealed record AdminFamilyDependentItem(
    Guid ElderlyId,
    Guid IdentityUserId,
    FamilyRelationshipType RelationshipType);

public sealed record AdminFamilyDetail(
    Guid FamilyId,
    string Name,
    Guid OwnerUserId,
    int MemberCount,
    DateTime CreatedOnUtc,
    DateTime? DeletedOnUtc,
    IReadOnlyList<AdminFamilyMemberItem> Members,
    IReadOnlyList<AdminFamilyDependentItem> Dependents);

/// <summary>
/// A page of families with the actual filtered total count. Paging is clamped
/// (never an error): page is at least 1, page size defaults to 25 and is capped
/// at 100.
/// </summary>
public sealed record AdminFamilyPage(
    IReadOnlyList<AdminFamilyListItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>
/// Bounded family read. <c>Search</c> matches an exact family id when it parses
/// as a GUID and otherwise a case-insensitive substring of the family name; a
/// blank search and a search that matches nothing both return an empty page and
/// never an error. Deleted families are retained and report <c>DeletedOnUtc</c>.
/// </summary>
public sealed record ListAdminFamiliesQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null) : IQuery<AdminFamilyPage>;

public sealed record GetAdminFamilyQuery(Guid FamilyId) : IQuery<AdminFamilyDetail>;

public sealed class ListAdminFamiliesQueryHandler(IFamiliesDbContext dbContext)
    : IQueryHandler<ListAdminFamiliesQuery, AdminFamilyPage>
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public async Task<Result<AdminFamilyPage>> Handle(
        ListAdminFamiliesQuery request,
        CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(
            request.PageSize <= 0 ? DefaultPageSize : request.PageSize, 1, MaxPageSize);

        IQueryable<Family> query = dbContext.Families.AsNoTracking();

        string? search = string.IsNullOrWhiteSpace(request.Search)
            ? null
            : request.Search.Trim();

        if (search is not null)
        {
            query = Guid.TryParse(search, out Guid familyId)
                ? query.Where(f => f.Id == new FamilyId(familyId))
                : query.Where(f => f.Name.ToLower().Contains(search.ToLower()));
        }

        int totalCount = await query.CountAsync(cancellationToken);
        int skip = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);

        // The page is selected by key first and its members are then loaded with
        // a single bounded Include: the member count is read from the loaded
        // aggregate, never by a nested aggregate that a relational provider would
        // have to translate, and no read is repeated per row.
        List<FamilyId> pageIds = await query
            .OrderByDescending(f => f.CreatedOnUtc)
            .ThenBy(f => f.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);

        List<Family> pageRows = await dbContext.Families
            .AsNoTracking()
            .Where(f => pageIds.Contains(f.Id))
            .Include(f => f.Members)
            .ToListAsync(cancellationToken);

        Dictionary<FamilyId, Family> familiesById = pageRows.ToDictionary(f => f.Id);

        var items = new List<AdminFamilyListItem>(pageIds.Count);
        foreach (FamilyId familyId in pageIds)
        {
            if (!familiesById.TryGetValue(familyId, out Family? family))
            {
                continue;
            }

            items.Add(new AdminFamilyListItem(
                family.Id.Value,
                family.Name,
                family.OwnerUserId.Value,
                family.Members.Count,
                family.CreatedOnUtc,
                family.DeletedOnUtc));
        }

        return new AdminFamilyPage(items, page, pageSize, totalCount);
    }
}

public sealed class GetAdminFamilyQueryHandler(
    IFamiliesDbContext dbContext,
    IFamilyIdentityGateway identityGateway)
    : IQueryHandler<GetAdminFamilyQuery, AdminFamilyDetail>
{
    public async Task<Result<AdminFamilyDetail>> Handle(
        GetAdminFamilyQuery request,
        CancellationToken cancellationToken)
    {
        Family? family = await dbContext.Families
            .AsNoTracking()
            .Include(f => f.Members)
            .SingleOrDefaultAsync(
                f => f.Id == new FamilyId(request.FamilyId),
                cancellationToken);

        if (family is null)
        {
            return FamilyErrors.NotFound;
        }

        IReadOnlyDictionary<UserId, FamilyMemberProfile> profiles =
            await FamilyMemberEnrichment.ResolveProfilesAsync(
                identityGateway,
                family,
                cancellationToken);

        List<AdminFamilyMemberItem> members = family.Members
            .OrderBy(member => member.JoinedOnUtc)
            // The id tie-break compares the underlying Guid: UserId itself is a
            // record struct without a comparison, and in-memory ordering must not
            // depend on one.
            .ThenBy(member => member.Id.Value)
            .Select(member =>
            {
                profiles.TryGetValue(member.Id, out FamilyMemberProfile? profile);

                return new AdminFamilyMemberItem(
                    member.Id.Value,
                    member.Role,
                    member.RelationshipType,
                    member.AddedByUserId.Value,
                    member.JoinedOnUtc,
                    profile?.Status ?? 0);
            })
            .ToList();

        List<AdminFamilyDependentItem> dependents = await dbContext.Elderlies
            .AsNoTracking()
            .Where(e => e.FamilyId == family.Id)
            .OrderBy(e => e.CreatedOnUtc)
            .ThenBy(e => e.Id)
            .Select(e => new AdminFamilyDependentItem(
                e.Id.Value,
                e.IdentityUserId.Value,
                e.RelationshipType))
            .ToListAsync(cancellationToken);

        return new AdminFamilyDetail(
            family.Id.Value,
            family.Name,
            family.OwnerUserId.Value,
            family.Members.Count,
            family.CreatedOnUtc,
            family.DeletedOnUtc,
            members,
            dependents);
    }
}
