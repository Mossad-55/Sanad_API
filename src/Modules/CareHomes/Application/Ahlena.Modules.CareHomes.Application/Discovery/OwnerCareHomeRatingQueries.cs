using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.CareHomes.Application.Abstractions.Data;
using Ahlena.Modules.CareHomes.Domain.Bookings;
using Ahlena.Modules.CareHomes.Domain.Facilities;

namespace Ahlena.Modules.CareHomes.Application.Discovery;

/// <summary>
/// F7 owner-visible flag status of a rating. <c>None</c> means the caller has
/// never flagged it, <c>Open</c> that one waiting case exists, and
/// <c>Resolved</c> that the caller's flagged case was closed by an admin.
/// </summary>
public static class OwnerRatingFlagStatus
{
    public const string None = "None";
    public const string Open = "Open";
    public const string Resolved = "Resolved";
}

/// <summary>
/// F7 owner rating item. Deliberately anonymous: the reviewer is never
/// identified — there is no name, user id, family id, phone, email or avatar
/// field to leak, and the flag facts are the caller's own.
/// </summary>
public sealed record OwnerCareHomeRatingItem(
    Guid RatingId,
    int Stars,
    string? ReviewText,
    DateTime CreatedOnUtc,
    string FlagStatus,
    string? MyFlagReason,
    DateTime? MyFlaggedOnUtc);

public sealed record OwnerCareHomeRatingPage(
    IReadOnlyList<OwnerCareHomeRatingItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    decimal AverageStars,
    int RatingCount);

public sealed record GetOwnerCareHomeRatingsQuery(
    UserId ActorUserId,
    int Page,
    int PageSize) : IQuery<OwnerCareHomeRatingPage>;

public sealed class GetOwnerCareHomeRatingsQueryHandler(ICareHomesDbContext dbContext)
    : IQueryHandler<GetOwnerCareHomeRatingsQuery, OwnerCareHomeRatingPage>
{
    private static readonly Error FacilityNotFound = new(
        "CareHomes.Facility.NotFound", "Care-home facility was not found.");

    public async Task<Result<OwnerCareHomeRatingPage>> Handle(
        GetOwnerCareHomeRatingsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 100)
            return Result<OwnerCareHomeRatingPage>.Failure(CareHomeRatingModerationErrors.InvalidQuery);

        CareHomeFacility? facility = await dbContext.Facilities.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.OwnerUserId == request.ActorUserId, cancellationToken);
        if (facility is null)
            return Result<OwnerCareHomeRatingPage>.Failure(FacilityNotFound);

        // F7: hidden ratings are invisible to the owner and leave both the page
        // and the average.
        IQueryable<CareHomeRating> visible = dbContext.Ratings.AsNoTracking()
            .Where(rating => rating.CareHomeId == facility.Id &&
                rating.ModerationState == CareHomeRatingModerationState.Visible);

        int ratingCount = await visible.CountAsync(cancellationToken);
        int starsTotal = ratingCount == 0
            ? 0
            : await visible.SumAsync(rating => rating.Stars, cancellationToken);
        decimal averageStars = ratingCount == 0
            ? 0m
            : decimal.Round((decimal)starsTotal / ratingCount, 2, MidpointRounding.AwayFromZero);

        List<CareHomeRating> ratings = await visible
            .OrderByDescending(rating => rating.CreatedOnUtc)
            .ThenByDescending(rating => rating.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        Guid[] ratingIds = ratings.Select(rating => rating.Id).ToArray();
        List<CareHomeRatingFlag> myFlags = ratingIds.Length == 0
            ? []
            : await dbContext.RatingFlags.AsNoTracking()
                .Where(flag => ratingIds.Contains(flag.RatingId) &&
                    flag.OwnerUserId == request.ActorUserId)
                .ToListAsync(cancellationToken);
        Dictionary<Guid, CareHomeRatingFlag> flagsByRating = myFlags
            .GroupBy(flag => flag.RatingId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(flag => flag.IsOpen)
                    .ThenByDescending(flag => flag.CreatedOnUtc)
                    .First());

        return Result<OwnerCareHomeRatingPage>.Success(new OwnerCareHomeRatingPage(
            ratings.Select(rating =>
            {
                flagsByRating.TryGetValue(rating.Id, out CareHomeRatingFlag? flag);
                string status = flag is null
                    ? OwnerRatingFlagStatus.None
                    : flag.IsOpen
                        ? OwnerRatingFlagStatus.Open
                        : OwnerRatingFlagStatus.Resolved;
                return new OwnerCareHomeRatingItem(
                    rating.Id,
                    rating.Stars,
                    rating.ReviewText,
                    rating.CreatedOnUtc,
                    status,
                    flag?.Reason,
                    flag?.CreatedOnUtc);
            }).ToArray(),
            request.Page,
            request.PageSize,
            ratingCount,
            averageStars,
            ratingCount));
    }
}
