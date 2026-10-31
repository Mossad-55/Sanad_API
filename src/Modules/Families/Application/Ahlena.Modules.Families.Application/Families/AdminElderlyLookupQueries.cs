using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Domain.Elderlies;

namespace Ahlena.Modules.Families.Application.Families;

/// <summary>
/// One dependent (elderly) name+id lookup row. <c>Id</c> is the single
/// dependent identifier (<c>families.elderlies.id</c>) used as both the
/// care-record <c>dependentId</c> and the elderly id of check-ins, SOS, help
/// requests and medications, so an operator can paste it into any of those
/// reads. The projection is deliberately limited to the two bilingual names:
/// no contact, address, emergency-contact, health-note or medical field is
/// ever selected into it.
/// </summary>
public sealed record AdminElderlyLookupItem(
    Guid Id,
    string ArabicFullName,
    string EnglishFullName);

/// <summary>
/// A page of dependent lookup rows with the actual filtered total count.
/// Paging is clamped and never an error: page is at least 1, page size defaults
/// to 20 and is capped at 100. The five fields match the Caregivers
/// <c>PagedResult&lt;T&gt;</c> envelope so both lookup routes serialize
/// identically even though the module boundary prevents sharing the type.
/// </summary>
public sealed record AdminElderlyLookupPage(
    IReadOnlyList<AdminElderlyLookupItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

/// <summary>
/// Bounded dependent name+id lookup for SuperAdmin/SupportAdmin. <c>Search</c>
/// is an optional case-insensitive substring of the Arabic or English full
/// name; a blank search returns the whole (live) register page and a search
/// that matches nothing returns an empty page, never an error. Phone numbers,
/// emails and every other contact or medical value are not searchable.
/// Dependents of deleted families are excluded, mirroring the F1 operational
/// reads.
/// </summary>
public sealed record LookupAdminElderliesQuery(
    string? Search = null,
    int Page = 1,
    int PageSize = 20) : IQuery<AdminElderlyLookupPage>;

/// <summary>
/// The exact query shape of the dependent lookup, kept as named builders so the
/// provider-level translation facts can pin the SQL that ships (ILIKE + ESCAPE,
/// the deterministic ordering and the narrow projection) without opening a
/// connection.
/// </summary>
public static class AdminElderlyLookupQuery
{
    /// <summary>The ILIKE escape character; the pattern is the escaped needle.</summary>
    public const string LikeEscapeCharacter = "\\";

    /// <summary>
    /// Escapes LIKE/ILIKE metacharacters in caller text so they match
    /// themselves literally instead of acting as wildcards, exactly as the
    /// Care Home resident search does for these same name columns: "\\", "%"
    /// and "_" are never stripped and never honored.
    /// </summary>
    public static string ToLikePattern(string search) =>
        $"%{search.Trim()
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_")}%";

    /// <summary>
    /// Live dependents only: dependents of a family with a deletion instant are
    /// excluded deliberately, and the family row and dependent row are never
    /// deleted by this rule.
    /// </summary>
    public static IQueryable<Elderly> LiveDependents(
        IFamiliesDbContext dbContext,
        string? search)
    {
        IQueryable<Elderly> query = dbContext.Elderlies
            .AsNoTracking()
            .Where(e => !dbContext.Families.Any(
                f => f.Id == e.FamilyId && f.DeletedOnUtc != null));

        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        // The names are value objects persisted through EF value converters:
        // the mapped property is what translates, so the search uses the
        // 3-argument ILIKE overload with an explicit escape character (the same
        // shape the Care Home resident search uses on families.elderlies) and
        // never a member of the converted type.
        string pattern = ToLikePattern(search);

        return query.Where(e =>
            EF.Functions.ILike(e.ArabicFullName, pattern, LikeEscapeCharacter) ||
            EF.Functions.ILike(e.EnglishFullName, pattern, LikeEscapeCharacter));
    }

    /// <summary>
    /// One SQL-side page: names then the id tie-breaker for a total order, and
    /// a skip that saturates instead of overflowing. The projection selects the
    /// two name columns and the id only — the entity, its owned medical profile
    /// and every contact/note column are never materialized.
    /// </summary>
    public static IQueryable<AdminElderlyLookupRow> Page(
        IFamiliesDbContext dbContext,
        string? search,
        int page,
        int pageSize) =>
        LiveDependents(dbContext, search)
            .OrderBy(e => e.ArabicFullName)
            .ThenBy(e => e.EnglishFullName)
            .ThenBy(e => e.Id)
            .Skip(ComputeSkip(page, pageSize))
            .Take(pageSize)
            .Select(e => new AdminElderlyLookupRow(
                e.Id,
                e.ArabicFullName,
                e.EnglishFullName));

    /// <summary>
    /// Overflow-safe skip: a very large page saturates at the last pageable
    /// offset instead of wrapping to a negative OFFSET (the same convention the
    /// family register uses).
    /// </summary>
    public static int ComputeSkip(int page, int pageSize) =>
        (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);
}

/// <summary>One projected lookup row before the values are unwrapped.</summary>
public sealed record AdminElderlyLookupRow(
    ElderlyId Id,
    FullName ArabicFullName,
    FullName EnglishFullName);

public sealed class LookupAdminElderliesQueryHandler(IFamiliesDbContext dbContext)
    : IQueryHandler<LookupAdminElderliesQuery, AdminElderlyLookupPage>
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public async Task<Result<AdminElderlyLookupPage>> Handle(
        LookupAdminElderliesQuery request,
        CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(
            request.PageSize <= 0 ? DefaultPageSize : request.PageSize,
            1,
            MaxPageSize);

        string? search = string.IsNullOrWhiteSpace(request.Search)
            ? null
            : request.Search;

        int totalCount = await AdminElderlyLookupQuery
            .LiveDependents(dbContext, search)
            .CountAsync(cancellationToken);

        List<AdminElderlyLookupRow> rows = await AdminElderlyLookupQuery
            .Page(dbContext, search, page, pageSize)
            .ToListAsync(cancellationToken);

        List<AdminElderlyLookupItem> items = rows
            .Select(row => new AdminElderlyLookupItem(
                row.Id.Value,
                row.ArabicFullName.Value,
                row.EnglishFullName.Value))
            .ToList();

        int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return Result<AdminElderlyLookupPage>.Success(
            new AdminElderlyLookupPage(
                items,
                page,
                pageSize,
                totalCount,
                totalPages));
    }
}
