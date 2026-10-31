using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using FeedbackEntity = Ahlena.Modules.Identity.Domain.Feedback;

namespace Ahlena.Modules.Identity.Application.Feedback;

/// <summary>
/// One stored app rating for the Admin rating-investigation read. The fields
/// are exactly the columns the authenticated app submission saved — attribution is
/// limited to the internal user id; no identity join, contact field, or
/// derived data is added (portal second pass B3).
/// </summary>
public sealed record AdminAppRatingResponse(
    Guid Id,
    Guid UserId,
    int Rating,
    string? Comment,
    string? DeviceInfo,
    string? AppVersion,
    DateTime CreatedOnUtc);

public sealed record PagedAdminAppRatings(
    IReadOnlyList<AdminAppRatingResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record GetAdminAppRatingsQuery(int Page = 1, int PageSize = 20)
    : IQuery<PagedAdminAppRatings>;

public sealed class GetAdminAppRatingsQueryHandler(IIdentityDbContext dbContext)
    : IQueryHandler<GetAdminAppRatingsQuery, PagedAdminAppRatings>
{
    public async Task<Result<PagedAdminAppRatings>> Handle(
        GetAdminAppRatingsQuery request,
        CancellationToken cancellationToken)
    {
        // The same silent-clamp convention as the other paged admin reads
        // (e.g. admin subscription plans): out-of-range values fall back to
        // the defaults instead of surface errors.
        int page = request.Page < 1 ? 1 : request.Page;
        int pageSize = request.PageSize is < 1 or > 100 ? 20 : request.PageSize;

        IQueryable<FeedbackEntity> query = dbContext.Feedbacks.AsNoTracking();

        int totalCount = await query.CountAsync(cancellationToken);

        List<FeedbackEntity> ratings = await query
            .OrderByDescending(rating => rating.CreatedOnUtc)
            .ThenBy(rating => rating.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedAdminAppRatings(
            ratings.Select(rating => new AdminAppRatingResponse(
                rating.Id,
                rating.UserId.Value,
                rating.Rating,
                rating.Comment,
                rating.DeviceInfo,
                rating.AppVersion,
                rating.CreatedOnUtc)).ToList(),
            page,
            pageSize,
            totalCount);
    }
}
