using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.Discovery;

namespace Sanad.Modules.Caregivers.Application.Lookups;

/// <summary>
/// One caregiver name+id lookup row. <c>Id</c> is the caregiver aggregate id
/// (<c>caregivers.caregivers.id</c>), which is the id every Admin caregiver
/// route expects, and never the Identity user id. The projection is limited to
/// the two bilingual names: no phone number, email, bank, payout or medical
/// field is selected into it.
/// </summary>
public sealed record AdminCaregiverLookupItem(
    Guid Id,
    string ArabicFullName,
    string EnglishFullName);

/// <summary>
/// Bounded caregiver name+id lookup for SuperAdmin/SupportAdmin
/// (<c>CaregiverReviewAdmin</c>, the policy the caregiver review surface
/// already uses). <c>Search</c> is an optional case-insensitive substring of
/// the Arabic or English full name; a blank search returns the whole register
/// page and a search that matches nothing returns an empty page, never an
/// error. Only the name columns are searchable — phone and email are not, so
/// this route never becomes a second contact-data read beside the
/// SuperAdmin-only admin user list. Caregivers without a linked Identity user
/// (legacy orphan rows) are excluded, deliberately and without deleting the
/// rows: a row with no resolvable name cannot satisfy a name lookup.
/// </summary>
public sealed record LookupAdminCaregiversQuery(
    string? Search = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<AdminCaregiverLookupItem>>;

/// <summary>
/// The posted pattern rules of the caregiver lookup, named so the escaping can
/// be pinned by tests even though the SQL itself only runs against PostgreSQL.
/// </summary>
public static class AdminCaregiverLookupQuery
{
    /// <summary>The LIKE escape character declared in the SQL.</summary>
    public const string LikeEscapeCharacter = "\\";

    /// <summary>
    /// Wraps caller text in the substring wildcards and escapes the LIKE
    /// metacharacters "\\", "%" and "_" so they match themselves literally —
    /// the caller's text is a literal substring, never a pattern. Blank or
    /// whitespace-only text means "no filter" and returns <c>null</c> (the SQL
    /// then matches every caregiver). The result is always bound as a
    /// parameter, never concatenated into the statement.
    /// </summary>
    public static string? ToLikePattern(string? search) =>
        string.IsNullOrWhiteSpace(search)
            ? null
            : $"%{search.Trim()
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_")}%";
}

public sealed class LookupAdminCaregiversQueryHandler(ICaregiversDbContext dbContext)
    : IQueryHandler<LookupAdminCaregiversQuery, PagedResult<AdminCaregiverLookupItem>>
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public async Task<Result<PagedResult<AdminCaregiverLookupItem>>> Handle(
        LookupAdminCaregiversQuery request,
        CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(
            request.PageSize <= 0 ? DefaultPageSize : request.PageSize,
            1,
            MaxPageSize);

        string? search = string.IsNullOrWhiteSpace(request.Search)
            ? null
            : request.Search.Trim();

        // The names live in identity.users, which this module reads only
        // through the existing admin raw-SQL reads (the admin caregiver list
        // and the medical recipient picker). Filtering and paging stay in SQL,
        // so no user row is materialized to filter it in memory.
        (IReadOnlyList<AdminCaregiverLookupItem> items, int totalCount) =
            await dbContext.GetAdminCaregiverLookupAsync(
                search,
                page,
                pageSize,
                cancellationToken);

        int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return Result<PagedResult<AdminCaregiverLookupItem>>.Success(
            new PagedResult<AdminCaregiverLookupItem>(
                items,
                page,
                pageSize,
                totalCount,
                totalPages));
    }
}
