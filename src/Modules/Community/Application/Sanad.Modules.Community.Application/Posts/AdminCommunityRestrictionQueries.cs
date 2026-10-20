using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Application.Abstractions.Data;
using Sanad.Modules.Community.Domain.Posts;

namespace Sanad.Modules.Community.Application.Posts;

/// <summary>
/// The single read-side "active now" predicate for retained Community author
/// restrictions. It mirrors the domain rule
/// (<see cref="CommunityAuthorRestriction.IsActiveAt"/>) and the write gate
/// (<see cref="CommunityAuthorRestrictionGuard.IsAuthorRestrictedAsync"/>) so a
/// restriction reported as active is exactly a restriction that blocks new
/// Community writes, and every read resolves it against one captured UTC
/// instant. Nothing else is invented: the stored start instant is reported but
/// is not a gate, because the domain has never treated it as one and every
/// restriction is created starting immediately.
/// </summary>
public static class CommunityAuthorRestrictionActiveFilter
{
    public static IQueryable<CommunityAuthorRestriction> ActiveAt(
        this IQueryable<CommunityAuthorRestriction> query,
        DateTime utcNow) =>
        query.Where(r => r.RevokedOnUtc == null && r.ExpiresOnUtc > utcNow);

    public static IQueryable<CommunityAuthorRestriction> NotActiveAt(
        this IQueryable<CommunityAuthorRestriction> query,
        DateTime utcNow) =>
        query.Where(r => r.RevokedOnUtc != null || r.ExpiresOnUtc <= utcNow);
}

/// <summary>
/// One retained restriction row for moderation reads: the existing persisted
/// restriction and revocation facts, the wire duration/reason codes the create
/// and revoke responses already publish, the active flag resolved at the page's
/// captured instant, and the author's Arabic/English display names. Contact
/// data is never returned.
/// </summary>
public sealed record AdminCommunityRestrictionListItem(
    Guid RestrictionId,
    Guid AuthorId,
    string AuthorArabicName,
    string AuthorEnglishName,
    Guid ModeratorId,
    string Duration,
    string ReasonCode,
    string? Note,
    DateTime StartsOnUtc,
    DateTime ExpiresOnUtc,
    bool IsActive,
    Guid? RevokedBy,
    DateTime? RevokedOnUtc,
    string? RevocationReason);

/// <summary>
/// A page of retained restrictions with the actual filtered total count. The
/// item list is never ring-fenced by the counters: there are no counters here,
/// only the count that belongs to the current filter.
/// </summary>
public sealed record AdminCommunityRestrictionsPage(
    IReadOnlyList<AdminCommunityRestrictionListItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>
/// F4 moderation read of retained author restrictions: optional author and
/// active filters, newest first by stored start instant with a stable id
/// tie-break, page size default 25 and maximum 100 (the existing moderation
/// queue conventions). <c>IsActive</c> is derived from the persisted
/// revocation/expiry facts at one captured UTC instant, so <c>true</c> means
/// currently active and <c>false</c> means not active.
/// </summary>
public sealed record GetAdminCommunityRestrictionsQuery(
    Guid? AuthorId = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 25) : IQuery<AdminCommunityRestrictionsPage>;

public sealed class GetAdminCommunityRestrictionsQueryHandler(ICommunityDbContext dbContext)
    : IQueryHandler<GetAdminCommunityRestrictionsQuery, AdminCommunityRestrictionsPage>
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public async Task<Result<AdminCommunityRestrictionsPage>> Handle(
        GetAdminCommunityRestrictionsQuery request,
        CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(
            request.PageSize <= 0 ? DefaultPageSize : request.PageSize, 1, MaxPageSize);

        // One captured clock instant for the whole read: the filter and the
        // reported active flag can never disagree inside one response.
        DateTime now = DateTime.UtcNow;

        IQueryable<CommunityAuthorRestriction> query = dbContext.CommunityAuthorRestrictions
            .AsNoTracking();

        if (request.AuthorId is { } authorId)
        {
            var filterAuthorId = new UserId(authorId);
            query = query.Where(r => r.AuthorId == filterAuthorId);
        }

        if (request.IsActive is { } isActive)
        {
            query = isActive
                ? query.ActiveAt(now)
                : query.NotActiveAt(now);
        }

        int totalCount = await query.CountAsync(cancellationToken);
        int skip = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);

        List<CommunityAuthorRestriction> rows = await query
            .OrderByDescending(r => r.StartsOnUtc)
            .ThenByDescending(r => r.Id)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // One batched moderator-safe identity lookup for the whole page: a
        // per-row lookup would be an N+1 and is never performed.
        IReadOnlyDictionary<Guid, CommunityAuthorName> authorNames =
            await dbContext.GetCommunityAuthorNamesAsync(
                rows.Select(r => r.AuthorId.Value).Distinct().ToArray(),
                cancellationToken);

        var items = new List<AdminCommunityRestrictionListItem>(rows.Count);
        foreach (CommunityAuthorRestriction restriction in rows)
        {
            authorNames.TryGetValue(restriction.AuthorId.Value, out CommunityAuthorName? author);
            items.Add(new AdminCommunityRestrictionListItem(
                restriction.Id.Value,
                restriction.AuthorId.Value,
                author?.ArabicFullName ?? string.Empty,
                author?.EnglishFullName ?? string.Empty,
                restriction.ModeratorId.Value,
                restriction.Duration.ToString(),
                restriction.ReasonCode,
                restriction.Note,
                restriction.StartsOnUtc,
                restriction.ExpiresOnUtc,
                restriction.RevokedOnUtc is null && restriction.ExpiresOnUtc > now,
                restriction.RevokedBy?.Value,
                restriction.RevokedOnUtc,
                restriction.RevocationReason));
        }

        return Result<AdminCommunityRestrictionsPage>.Success(
            new AdminCommunityRestrictionsPage(items, page, pageSize, totalCount));
    }
}
