using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Library;
using Sanad.Modules.Families.Application.Abstractions.Data;

namespace Sanad.Modules.Cms.Application.Library;

public sealed record GetLibraryContentListQuery(
    LibraryContentType? Type = null,
    Guid? CategoryId = null,
    string? Search = null,
    LibraryContentSort Sort = LibraryContentSort.Newest,
    int Page = 1,
    int PageSize = 20,
    Guid ViewerUserId = default) : IQuery<IReadOnlyList<LibraryContentListItemResponse>>;

public sealed class GetLibraryContentListQueryHandler(ICmsDbContext db, IFamiliesDbContext familiesDb)
    : IQueryHandler<GetLibraryContentListQuery, IReadOnlyList<LibraryContentListItemResponse>>
{
    public async Task<Result<IReadOnlyList<LibraryContentListItemResponse>>> Handle(
        GetLibraryContentListQuery request, CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, 100);

        IQueryable<LibraryContent> query = db.LibraryContents
            .AsNoTracking()
            .Where(content => content.Status == LibraryContentStatus.Published);

        if (request.Type is LibraryContentType type)
            query = query.Where(content => content.Type == type);

        if (request.CategoryId is Guid rawCategoryId)
            query = query.Where(content =>
                content.CategoryId == new LibraryCategoryId(rawCategoryId));

        string? term = string.IsNullOrWhiteSpace(request.Search)
            ? null
            : request.Search.Trim().ToLower();
        if (term is not null)
        {
            query = query.Where(content =>
                content.ArabicTitle.ToLower().Contains(term) ||
                content.EnglishTitle.ToLower().Contains(term) ||
                content.ArabicSummary.ToLower().Contains(term) ||
                content.EnglishSummary.ToLower().Contains(term));
        }

        query = request.Sort switch
        {
            LibraryContentSort.MostViewed =>
                query.OrderByDescending(content => content.ViewCount)
                    .ThenByDescending(content => content.CreatedOnUtc)
                    .ThenByDescending(content => content.Id),
            _ =>
                query.OrderByDescending(content => content.CreatedOnUtc)
                    .ThenByDescending(content => content.Id)
        };

        IReadOnlyList<LibraryContent> contents = await query
            .Include(content => content.Category)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return await LibraryFeedMaterializer.MaterializeAsync(
            db, familiesDb, contents, request.ViewerUserId, cancellationToken);
    }
}

public sealed record GetLibraryContentQuery(Guid Id, Guid ViewerUserId)
    : IQuery<LibraryContentDetailResponse>;

public sealed class GetLibraryContentQueryHandler(ICmsDbContext db, IFamiliesDbContext familiesDb)
    : IQueryHandler<GetLibraryContentQuery, LibraryContentDetailResponse>
{
    public async Task<Result<LibraryContentDetailResponse>> Handle(
        GetLibraryContentQuery request, CancellationToken cancellationToken)
    {
        LibraryContent? content = await db.LibraryContents
            .AsNoTracking()
            .Include(item => item.Category)
            .SingleOrDefaultAsync(
                item => item.Id == new LibraryContentId(request.Id) &&
                    item.Status == LibraryContentStatus.Published,
                cancellationToken);
        if (content is null)
            return LibraryErrors.ContentNotPublished;

        var state = await LibraryViewerState.LoadAsync(
            db, request.ViewerUserId, [content], cancellationToken);
        state.TryGetValue(content.Id.Value, out (bool Liked, bool Saved) viewer);
        bool hasPremium = await LibraryPremiumAccess.HasPremiumContentAccessAsync(
            familiesDb, new UserId(request.ViewerUserId), cancellationToken);

        return content.ToDetail(viewer.Liked, viewer.Saved, hasPremium || !content.IsPremium);
    }
}

public sealed record LibraryVideoFileResponse(string StorageKey);

/// <summary>
/// Resolves the stored private bytes of a published library video for the
/// authorized app caller. Publication, identity and the premium entitlement are
/// re-checked here rather than inherited from the detail response, and every
/// failure — unknown, unpublished, not a video, no uploaded bytes, not entitled —
/// returns the same <c>Cms.LibraryContent.NotPublished</c> so the route cannot be
/// used to probe entitlement or stored-file state.
/// </summary>
public sealed record GetLibraryContentVideoQuery(Guid Id, Guid ViewerUserId)
    : IQuery<LibraryVideoFileResponse>;

public sealed class GetLibraryContentVideoQueryHandler(ICmsDbContext db, IFamiliesDbContext familiesDb)
    : IQueryHandler<GetLibraryContentVideoQuery, LibraryVideoFileResponse>
{
    public async Task<Result<LibraryVideoFileResponse>> Handle(
        GetLibraryContentVideoQuery request, CancellationToken cancellationToken)
    {
        LibraryContent? content = await db.LibraryContents
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == new LibraryContentId(request.Id) &&
                    item.Status == LibraryContentStatus.Published &&
                    item.Type == LibraryContentType.Video,
                cancellationToken);
        if (content is null || !content.HasStoredVideo)
            return LibraryErrors.ContentNotPublished;

        bool hasPremium = await LibraryPremiumAccess.HasPremiumContentAccessAsync(
            familiesDb, new UserId(request.ViewerUserId), cancellationToken);
        if (content.IsPremium && !hasPremium)
            return LibraryErrors.ContentNotPublished;

        return new LibraryVideoFileResponse(content.VideoStorageKey!);
    }
}

public sealed record GetLibraryRecommendationsQuery(
    Guid? ContentId = null,
    Guid? CategoryId = null,
    int Page = 1,
    int PageSize = 10,
    Guid ViewerUserId = default) : IQuery<IReadOnlyList<LibraryContentListItemResponse>>;

public sealed class GetLibraryRecommendationsQueryHandler(ICmsDbContext db, IFamiliesDbContext familiesDb)
    : IQueryHandler<GetLibraryRecommendationsQuery, IReadOnlyList<LibraryContentListItemResponse>>
{
    public async Task<Result<IReadOnlyList<LibraryContentListItemResponse>>> Handle(
        GetLibraryRecommendationsQuery request, CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, 100);

        // Seed precedence: contentId > categoryId. Unknown ids fall back to
        // the top-viewed ranking. Same-category newest first, then
        // top-viewed fallback; no ML, deterministic and documented.
        LibraryCategoryId? seedCategoryId = null;
        Guid excludedContentId = Guid.Empty;
        if (request.ContentId is Guid rawContentId)
        {
            excludedContentId = rawContentId;
            LibraryContent? seed = await db.LibraryContents
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.Id == new LibraryContentId(rawContentId),
                    cancellationToken);
            seedCategoryId = seed?.CategoryId;
        }
        else if (request.CategoryId is Guid rawCategoryId)
        {
            seedCategoryId = new LibraryCategoryId(rawCategoryId);
        }

        IQueryable<LibraryContent> baseQuery = db.LibraryContents
            .AsNoTracking()
            .Where(content =>
                content.Status == LibraryContentStatus.Published &&
                content.Id != new LibraryContentId(excludedContentId));

        var orderedIds = new List<Guid>();
        if (seedCategoryId is LibraryCategoryId categoryId)
        {
            List<Guid> sameCategoryIds = await baseQuery
                .Where(content => content.CategoryId == categoryId)
                .OrderByDescending(content => content.CreatedOnUtc)
                .ThenByDescending(content => content.Id)
                .Select(content => content.Id.Value)
                .ToListAsync(cancellationToken);
            orderedIds.AddRange(sameCategoryIds);
        }

        List<Guid> taken = orderedIds.ToList();
        if (excludedContentId != Guid.Empty)
            taken.Add(excludedContentId);
        List<LibraryContentId> untakenIds = taken.Select(id => new LibraryContentId(id)).ToList();

        List<Guid> topViewedIds = await baseQuery
            .Where(content => !untakenIds.Contains(content.Id))
            .OrderByDescending(content => content.ViewCount)
            .ThenByDescending(content => content.CreatedOnUtc)
            .ThenByDescending(content => content.Id)
            .Select(content => content.Id.Value)
            .ToListAsync(cancellationToken);
        orderedIds.AddRange(topViewedIds);

        List<Guid> pageIds = orderedIds
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        if (pageIds.Count == 0)
            return Result<IReadOnlyList<LibraryContentListItemResponse>>.Success([]);
        List<LibraryContentId> pageContentIds = pageIds.Select(id => new LibraryContentId(id)).ToList();

        IReadOnlyList<LibraryContent> contents = await db.LibraryContents
            .AsNoTracking()
            .Include(content => content.Category)
            .Where(content => pageContentIds.Contains(content.Id))
            .ToListAsync(cancellationToken);

        var contentsById = contents.ToDictionary(content => content.Id.Value);
        IReadOnlyList<LibraryContent> ordered = pageIds
            .Where(contentsById.ContainsKey)
            .Select(id => contentsById[id])
            .ToList();

        return await LibraryFeedMaterializer.MaterializeAsync(
            db, familiesDb, ordered, request.ViewerUserId, cancellationToken);
    }
}

public sealed record GetSavedLibraryContentQuery(
    Guid UserId,
    int Page = 1,
    int PageSize = 20) : IQuery<IReadOnlyList<LibraryContentListItemResponse>>;

public sealed class GetSavedLibraryContentQueryHandler(ICmsDbContext db, IFamiliesDbContext familiesDb)
    : IQueryHandler<GetSavedLibraryContentQuery, IReadOnlyList<LibraryContentListItemResponse>>
{
    public async Task<Result<IReadOnlyList<LibraryContentListItemResponse>>> Handle(
        GetSavedLibraryContentQuery request, CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
            return Result<IReadOnlyList<LibraryContentListItemResponse>>.Success([]);

        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, 100);
        var user = new UserId(request.UserId);

        List<Guid> savedContentIds = await db.LibraryContentInteractions
            .AsNoTracking()
            .Where(interaction =>
                interaction.Kind == LibraryInteractionKind.Save &&
                interaction.UserId == user)
            .OrderByDescending(interaction => interaction.CreatedOnUtc)
            .ThenByDescending(interaction => interaction.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(interaction => interaction.ContentId.Value)
            .ToListAsync(cancellationToken);

        if (savedContentIds.Count == 0)
            return Result<IReadOnlyList<LibraryContentListItemResponse>>.Success([]);
        List<LibraryContentId> savedIds = savedContentIds.Select(id => new LibraryContentId(id)).ToList();

        IReadOnlyList<LibraryContent> contents = await db.LibraryContents
            .AsNoTracking()
            .Include(content => content.Category)
            .Where(content =>
                content.Status == LibraryContentStatus.Published &&
                savedIds.Contains(content.Id))
            .ToListAsync(cancellationToken);

        var contentsById = contents.ToDictionary(content => content.Id.Value);
        IReadOnlyList<LibraryContent> ordered = savedContentIds
            .Where(contentsById.ContainsKey)
            .Select(id => contentsById[id])
            .ToList();

        return await LibraryFeedMaterializer.MaterializeAsync(
            db, familiesDb, ordered, request.UserId, cancellationToken);
    }
}

public sealed record GetLibraryAdminListQuery(
    LibraryContentStatus? Status = null,
    LibraryContentType? Type = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20) : IQuery<LibraryAdminPageResponse>;

public sealed class GetLibraryAdminListQueryHandler(ICmsDbContext db)
    : IQueryHandler<GetLibraryAdminListQuery, LibraryAdminPageResponse>
{
    public async Task<Result<LibraryAdminPageResponse>> Handle(
        GetLibraryAdminListQuery request, CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, 100);

        IQueryable<LibraryContent> query = db.LibraryContents.AsNoTracking();

        if (request.Status is LibraryContentStatus status)
            query = query.Where(content => content.Status == status);
        if (request.Type is LibraryContentType type)
            query = query.Where(content => content.Type == type);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            string term = request.Search.Trim().ToLower();
            query = query.Where(content =>
                content.ArabicTitle.ToLower().Contains(term) ||
                content.EnglishTitle.ToLower().Contains(term));
        }

        int totalCount = await query.CountAsync(cancellationToken);
        IReadOnlyList<LibraryContent> contents = await query
            .Include(content => content.Category)
            .OrderByDescending(content => content.CreatedOnUtc)
            .ThenByDescending(content => content.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new LibraryAdminPageResponse(
            contents.Select(content => content.ToAdminResponse()).ToList(),
            page,
            pageSize,
            totalCount);
    }
}

public sealed record GetLibraryAdminContentQuery(Guid Id)
    : IQuery<LibraryAdminContentResponse>;

public sealed class GetLibraryAdminContentQueryHandler(ICmsDbContext db)
    : IQueryHandler<GetLibraryAdminContentQuery, LibraryAdminContentResponse>
{
    public async Task<Result<LibraryAdminContentResponse>> Handle(
        GetLibraryAdminContentQuery request, CancellationToken cancellationToken)
    {
        LibraryContent? content = await db.LibraryContents
            .AsNoTracking()
            .Include(item => item.Category)
            .SingleOrDefaultAsync(
                item => item.Id == new LibraryContentId(request.Id),
                cancellationToken);
        if (content is null)
            return LibraryErrors.ContentNotFound;

        return content.ToAdminResponse();
    }
}

internal static class LibraryFeedMaterializer
{
    public static async Task<Result<IReadOnlyList<LibraryContentListItemResponse>>> MaterializeAsync(
        ICmsDbContext db,
        IFamiliesDbContext familiesDb,
        IReadOnlyList<LibraryContent> contents,
        Guid viewerUserId,
        CancellationToken cancellationToken)
    {
        bool hasPremium = await LibraryPremiumAccess.HasPremiumContentAccessAsync(
            familiesDb, new UserId(viewerUserId), cancellationToken);
        var state = await LibraryViewerState.LoadAsync(
            db, viewerUserId, contents, cancellationToken);
        IReadOnlyList<LibraryContentFeedItem> items = LibraryViewerState.Attach(
            contents, state, hasPremium);

        return items
            .Select(item => item.Content.ToListItem(
                item.IsLiked, item.IsSaved, item.CanAccess))
            .ToList();
    }
}
