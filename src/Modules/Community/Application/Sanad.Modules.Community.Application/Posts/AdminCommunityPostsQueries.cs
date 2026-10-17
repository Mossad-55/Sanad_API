using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Categories;
using Sanad.Modules.Community.Domain.Posts;

namespace Sanad.Modules.Community.Application.Posts;

public sealed record AdminCommunityPostListItem(
    Guid Id,
    string TitleArabic,
    string TitleEnglish,
    string ContentArabic,
    string ContentEnglish,
    Guid? CategoryId,
    string? CategoryArabicName,
    string? CategoryEnglishName,
    string? ImageUrl,
    bool IsAnonymous,
    string Status,
    DateTime CreatedOnUtc,
    DateTime? UpdatedOnUtc,
    Guid AuthorId,
    string AuthorArabicName,
    string AuthorEnglishName,
    int ReportsCount,
    int OpenReportsCount);

public sealed record AdminCommunityModerationEvent(
    string Action,
    Guid? ActorId,
    string? ReasonCode,
    string? Note,
    DateTime OccurredOnUtc);

public sealed record AdminCommunityPostReportItem(
    Guid ReportId,
    Guid ReporterId,
    string ReasonCode,
    string? Note,
    string Status,
    DateTime CreatedOnUtc,
    Guid? ResolvedBy,
    DateTime? ResolvedOnUtc,
    string? ResolutionNote);

public sealed record AdminCommunityPostDetail(
    Guid Id,
    string TitleArabic,
    string TitleEnglish,
    string ContentArabic,
    string ContentEnglish,
    Guid? CategoryId,
    string? CategoryArabicName,
    string? CategoryEnglishName,
    string? ImageUrl,
    bool IsAnonymous,
    string Status,
    DateTime CreatedOnUtc,
    DateTime? UpdatedOnUtc,
    Guid AuthorId,
    string AuthorArabicName,
    string AuthorEnglishName,
    IReadOnlyList<AdminCommunityPostReportItem> Reports,
    IReadOnlyList<AdminCommunityModerationEvent> ModerationHistory);

/// <summary>
/// Actual persisted/query-derived queue counters, never fabricated zeroes.
/// They use the same database context as the queue it accompanies and are not
/// narrowed by the current page.
/// </summary>
public sealed record AdminCommunityCounters(
    int PendingReviewCount,
    int PublishedCount,
    int RejectedCount,
    int OpenReportCount,
    int AuthorsWithOpenReportsCount,
    int ActiveRestrictionCount);

public sealed record AdminCommunityPostsPage(
    IReadOnlyList<AdminCommunityPostListItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    AdminCommunityCounters Counters);

/// <summary>
/// FUR-07 admin moderation queue. Optional post-status and report-state
/// filters plus a bilingual title/content search; default newest first with a
/// stable id tie-break, page size default 25 and maximum 100.
/// </summary>
public sealed record GetAdminCommunityPostsQuery(
    string? Status = null,
    string? ReportStatus = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 25) : IQuery<AdminCommunityPostsPage>;

public sealed class GetAdminCommunityPostsQueryHandler(ICommunityDbContext dbContext)
    : IQueryHandler<GetAdminCommunityPostsQuery, AdminCommunityPostsPage>
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public async Task<Result<AdminCommunityPostsPage>> Handle(
        GetAdminCommunityPostsQuery request,
        CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(
            request.PageSize <= 0 ? DefaultPageSize : request.PageSize, 1, MaxPageSize);

        if (!TryParsePostStatus(request.Status, out PostStatus? status))
            return Result<AdminCommunityPostsPage>.Failure(CommunityModerationErrors.InvalidPostStatus);

        if (!TryParseReportStatus(request.ReportStatus, out AdminReportStatusFilter? reportStatus))
            return Result<AdminCommunityPostsPage>.Failure(CommunityModerationErrors.InvalidReportStatus);

        IQueryable<Post> query = dbContext.Posts
            .AsNoTracking()
            .Include(p => p.Category);

        if (status is { } postStatus)
            query = query.Where(p => p.Status == postStatus);

        string? term = string.IsNullOrWhiteSpace(request.Search)
            ? null
            : request.Search.Trim().ToLowerInvariant();

        if (term is not null)
        {
            query = query.Where(p =>
                p.TitleArabic.ToLower().Contains(term) ||
                p.TitleEnglish.ToLower().Contains(term) ||
                p.ContentArabic.ToLower().Contains(term) ||
                p.ContentEnglish.ToLower().Contains(term));
        }

        if (reportStatus is { } filter)
        {
            query = filter switch
            {
                AdminReportStatusFilter.Open => query.Where(p => dbContext.CommunityPostReports
                    .Any(r => r.PostId == p.Id && r.Status == CommunityReportStatus.Open)),
                AdminReportStatusFilter.ActionTaken => query.Where(p => dbContext.CommunityPostReports
                    .Any(r => r.PostId == p.Id && r.Status == CommunityReportStatus.ActionTaken)),
                AdminReportStatusFilter.Dismissed => query.Where(p => dbContext.CommunityPostReports
                    .Any(r => r.PostId == p.Id && r.Status == CommunityReportStatus.Dismissed)),
                _ => query.Where(p => !dbContext.CommunityPostReports.Any(r => r.PostId == p.Id))
            };
        }

        int totalCount = await query.CountAsync(cancellationToken);
        int skip = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);

        List<Post> posts = await query
            .OrderByDescending(p => p.CreatedOnUtc)
            .ThenByDescending(p => p.Id)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        List<CommunityPostId> pageIds = posts.Select(p => p.Id).ToList();

        var statsByPost = new Dictionary<CommunityPostId, (int Total, int Open)>();
        if (pageIds.Count > 0)
        {
            var reportStats = await dbContext.CommunityPostReports
                .AsNoTracking()
                .Where(r => pageIds.Contains(r.PostId))
                .GroupBy(r => r.PostId)
                .Select(g => new
                {
                    PostId = g.Key,
                    Total = g.Count(),
                    Open = g.Count(r => r.Status == CommunityReportStatus.Open)
                })
                .ToListAsync(cancellationToken);

            foreach (var stat in reportStats)
                statsByPost[stat.PostId] = (stat.Total, stat.Open);
        }

        var authorNames = await dbContext.GetCommunityAuthorNamesAsync(
            posts.Select(p => p.AuthorId.Value).Distinct().ToArray(),
            cancellationToken);

        var items = new List<AdminCommunityPostListItem>(posts.Count);
        foreach (Post post in posts)
        {
            (int total, int open) = statsByPost.TryGetValue(post.Id, out var stat) ? stat : (0, 0);
            authorNames.TryGetValue(post.AuthorId.Value, out CommunityAuthorName? author);
            items.Add(new AdminCommunityPostListItem(
                post.Id.Value,
                post.TitleArabic,
                post.TitleEnglish,
                post.ContentArabic,
                post.ContentEnglish,
                post.CategoryId?.Value,
                post.Category?.ArabicName,
                post.Category?.EnglishName,
                post.ImageUrl,
                post.IsAnonymous,
                post.Status.ToString(),
                post.CreatedOnUtc,
                post.UpdatedOnUtc,
                post.AuthorId.Value,
                author?.ArabicFullName ?? string.Empty,
                author?.EnglishFullName ?? string.Empty,
                total,
                open));
        }

        DateTime now = DateTime.UtcNow;
        var counters = new AdminCommunityCounters(
            PendingReviewCount: await dbContext.Posts
                .AsNoTracking().CountAsync(p => p.Status == PostStatus.PendingReview, cancellationToken),
            PublishedCount: await dbContext.Posts
                .AsNoTracking().CountAsync(p => p.Status == PostStatus.Published, cancellationToken),
            RejectedCount: await dbContext.Posts
                .AsNoTracking().CountAsync(p => p.Status == PostStatus.Rejected, cancellationToken),
            OpenReportCount: await dbContext.CommunityPostReports
                .AsNoTracking().CountAsync(r => r.Status == CommunityReportStatus.Open, cancellationToken),
            AuthorsWithOpenReportsCount: await (
                from report in dbContext.CommunityPostReports.AsNoTracking()
                where report.Status == CommunityReportStatus.Open
                join post in dbContext.Posts.AsNoTracking() on report.PostId equals post.Id
                select post.AuthorId).Distinct().CountAsync(cancellationToken),
            ActiveRestrictionCount: await dbContext.CommunityAuthorRestrictions
                .AsNoTracking()
                .Where(r => r.RevokedOnUtc == null && r.ExpiresOnUtc > now)
                .Select(r => r.AuthorId)
                .Distinct()
                .CountAsync(cancellationToken));

        return Result<AdminCommunityPostsPage>.Success(
            new AdminCommunityPostsPage(items, page, pageSize, totalCount, counters));
    }

    private static bool TryParsePostStatus(string? value, out PostStatus? status)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            status = null;
            return true;
        }

        if (Enum.TryParse(value.Trim(), ignoreCase: true, out PostStatus parsed) &&
            Enum.IsDefined(parsed))
        {
            status = parsed;
            return true;
        }

        status = null;
        return false;
    }

    private static bool TryParseReportStatus(string? value, out AdminReportStatusFilter? status)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            status = null;
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "open":
                status = AdminReportStatusFilter.Open;
                return true;
            case "actiontaken":
                status = AdminReportStatusFilter.ActionTaken;
                return true;
            case "dismissed":
                status = AdminReportStatusFilter.Dismissed;
                return true;
            case "none":
                status = AdminReportStatusFilter.None;
                return true;
            default:
                status = null;
                return false;
        }
    }

    private enum AdminReportStatusFilter
    {
        Open,
        ActionTaken,
        Dismissed,
        None
    }
}

/// <summary>
/// FUR-07 admin post detail with retained reports and moderation history.
/// Author fields are the id and Arabic/English display names only; no email,
/// phone, or other contact data is returned.
/// </summary>
public sealed record GetAdminCommunityPostDetailQuery(Guid PostId) : IQuery<AdminCommunityPostDetail>;

public sealed class GetAdminCommunityPostDetailQueryHandler(ICommunityDbContext dbContext)
    : IQueryHandler<GetAdminCommunityPostDetailQuery, AdminCommunityPostDetail>
{
    public async Task<Result<AdminCommunityPostDetail>> Handle(
        GetAdminCommunityPostDetailQuery request,
        CancellationToken cancellationToken)
    {
        var postId = new CommunityPostId(request.PostId);
        Post? post = await dbContext.Posts
            .AsNoTracking()
            .Include(p => p.Category)
            .SingleOrDefaultAsync(p => p.Id == postId, cancellationToken);

        if (post is null)
            return Result<AdminCommunityPostDetail>.Failure(
                new Error("Community.PostNotFound", "Community post not found."));

        List<CommunityPostReport> reports = await dbContext.CommunityPostReports
            .AsNoTracking()
            .Where(r => r.PostId == postId)
            .OrderByDescending(r => r.CreatedOnUtc)
            .ThenByDescending(r => r.Id)
            .ToListAsync(cancellationToken);

        var authorNames = await dbContext.GetCommunityAuthorNamesAsync(
            [post.AuthorId.Value], cancellationToken);
        authorNames.TryGetValue(post.AuthorId.Value, out CommunityAuthorName? author);

        var reportItems = reports
            .Select(r => new AdminCommunityPostReportItem(
                r.Id.Value,
                r.ReporterId.Value,
                r.ReasonCode,
                r.Note,
                r.Status.ToString(),
                r.CreatedOnUtc,
                r.ResolvedBy?.Value,
                r.ResolvedOnUtc,
                r.ResolutionNote))
            .ToList();

        var history = new List<AdminCommunityModerationEvent>();
        if (post.PublishedOnUtc is { } publishedOnUtc)
        {
            history.Add(new AdminCommunityModerationEvent(
                "Published",
                Guid.TryParse(post.PublishedBy, out Guid publishedBy) ? publishedBy : null,
                null,
                null,
                publishedOnUtc));
        }

        if (post.RejectedOnUtc is { } rejectedOnUtc)
        {
            history.Add(new AdminCommunityModerationEvent(
                "Rejected",
                post.RejectedBy?.Value,
                post.RejectionReasonCode,
                post.RejectionNote,
                rejectedOnUtc));
        }

        history.Sort((left, right) => left.OccurredOnUtc.CompareTo(right.OccurredOnUtc));

        return Result<AdminCommunityPostDetail>.Success(new AdminCommunityPostDetail(
            post.Id.Value,
            post.TitleArabic,
            post.TitleEnglish,
            post.ContentArabic,
            post.ContentEnglish,
            post.CategoryId?.Value,
            post.Category?.ArabicName,
            post.Category?.EnglishName,
            post.ImageUrl,
            post.IsAnonymous,
            post.Status.ToString(),
            post.CreatedOnUtc,
            post.UpdatedOnUtc,
            post.AuthorId.Value,
            author?.ArabicFullName ?? string.Empty,
            author?.EnglishFullName ?? string.Empty,
            reportItems,
            history));
    }
}
