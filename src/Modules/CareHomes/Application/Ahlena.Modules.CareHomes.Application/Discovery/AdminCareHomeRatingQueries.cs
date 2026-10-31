using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.CareHomes.Application.Abstractions.Data;
using Ahlena.Modules.CareHomes.Domain.Bookings;
using Ahlena.Modules.CareHomes.Domain.Facilities;

namespace Ahlena.Modules.CareHomes.Application.Discovery;

/// <summary>
/// F7 Care Home-rating moderation errors. The caregiver target raises the same
/// verbs under <c>Caregivers.Rating.*</c>; the status codes are pinned in the
/// shared problem-details mapper so both targets answer identically.
/// </summary>
public static class CareHomeRatingModerationErrors
{
    public static readonly Error NotFound = new(
        "CareHomes.Rating.NotFound",
        "The care-home rating was not found.");

    public static readonly Error InvalidReason = new(
        "CareHomes.Rating.InvalidReason",
        "A moderation reason between 1 and 500 characters is required.");

    public static readonly Error AlreadyHidden = new(
        "CareHomes.Rating.AlreadyHidden",
        "The care-home rating is already hidden.");

    public static readonly Error AlreadyVisible = new(
        "CareHomes.Rating.AlreadyVisible",
        "The care-home rating is already visible.");

    public static readonly Error NoOpenFlag = new(
        "CareHomes.Rating.NoOpenFlag",
        "The care-home rating has no unresolved owner flag.");

    public static readonly Error InvalidQuery = new(
        "CareHomes.Rating.InvalidQuery",
        "The rating filter is invalid.");
}

/// <summary>
/// F7 admin Care Home-rating item. Exposes the moderation facts, the retained
/// rating content and the unresolved flag summary only: no reviewer name,
/// phone, email or avatar is ever projected.
/// </summary>
public sealed record AdminCareHomeRatingListItem(
    Guid RatingId,
    Guid FacilityId,
    Guid BookingId,
    Guid FamilyId,
    int Stars,
    string? ReviewText,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc,
    string ModerationState,
    Guid? HiddenByUserId,
    DateTime? HiddenOnUtc,
    string? HideReason,
    Guid? UnresolvedFlagId,
    string? UnresolvedFlagReason,
    DateTime? UnresolvedFlaggedOnUtc);

public sealed record AdminCareHomeRatingPage(
    IReadOnlyList<AdminCareHomeRatingListItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record GetAdminCareHomeRatingsQuery(
    Guid? FacilityId,
    int? Stars,
    string? Moderation,
    bool? Flagged,
    int Page,
    int PageSize) : IQuery<AdminCareHomeRatingPage>;

/// <summary>
/// The moderation filter accepts exactly <c>visible</c>, <c>hidden</c> or
/// <c>all</c> (default <c>all</c>); anything else is a 400, never a silently
/// different result set.
/// </summary>
public static class CareHomeRatingModerationFilter
{
    public static bool TryParse(string? value, out bool? isHidden)
    {
        isHidden = null;

        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, "visible", StringComparison.OrdinalIgnoreCase))
        {
            isHidden = false;
            return true;
        }

        if (string.Equals(value, "hidden", StringComparison.OrdinalIgnoreCase))
        {
            isHidden = true;
            return true;
        }

        return false;
    }

    public static string Describe(CareHomeRatingModerationState state) =>
        state == CareHomeRatingModerationState.Hidden ? "hidden" : "visible";
}

public sealed class GetAdminCareHomeRatingsQueryHandler(ICareHomesDbContext dbContext)
    : IQueryHandler<GetAdminCareHomeRatingsQuery, AdminCareHomeRatingPage>
{
    public async Task<Result<AdminCareHomeRatingPage>> Handle(
        GetAdminCareHomeRatingsQuery request,
        CancellationToken cancellationToken)
    {
        // Validated in the handler (not by a FluentValidation rule) so an
        // invalid filter answers the documented CareHomes.Rating.InvalidQuery
        // (400) code instead of the pipeline's generic Api.Validation.Failed.
        if (request.Page < 1 ||
            request.PageSize is < 1 or > 100 ||
            (request.Stars is int stars && stars is < 1 or > 5) ||
            !CareHomeRatingModerationFilter.TryParse(request.Moderation, out bool? filterHidden))
        {
            return Result<AdminCareHomeRatingPage>.Failure(CareHomeRatingModerationErrors.InvalidQuery);
        }

        IQueryable<CareHomeRating> query = dbContext.Ratings.AsNoTracking();

        if (request.FacilityId is Guid facilityId)
            query = query.Where(rating => rating.CareHomeId == new CareHomeId(facilityId));
        if (request.Stars is int starFilter)
            query = query.Where(rating => rating.Stars == starFilter);
        if (filterHidden is bool hidden)
        {
            CareHomeRatingModerationState state = hidden
                ? CareHomeRatingModerationState.Hidden
                : CareHomeRatingModerationState.Visible;
            query = query.Where(rating => rating.ModerationState == state);
        }

        if (request.Flagged is bool flagged)
        {
            query = flagged
                ? query.Where(rating => dbContext.RatingFlags.Any(flag =>
                    flag.RatingId == rating.Id && flag.Status == CareHomeRatingFlagStatus.Open))
                : query.Where(rating => !dbContext.RatingFlags.Any(flag =>
                    flag.RatingId == rating.Id && flag.Status == CareHomeRatingFlagStatus.Open));
        }

        int totalCount = await query.CountAsync(cancellationToken);

        // Unresolved-flagged first, then newest-first.
        List<CareHomeRating> ratings = await query
            .OrderByDescending(rating => dbContext.RatingFlags.Any(flag =>
                flag.RatingId == rating.Id && flag.Status == CareHomeRatingFlagStatus.Open))
            .ThenByDescending(rating => rating.CreatedOnUtc)
            .ThenByDescending(rating => rating.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        Guid[] ratingIds = ratings.Select(rating => rating.Id).ToArray();
        List<CareHomeRatingFlag> openFlags = ratingIds.Length == 0
            ? []
            : await dbContext.RatingFlags.AsNoTracking()
                .Where(flag => ratingIds.Contains(flag.RatingId) &&
                    flag.Status == CareHomeRatingFlagStatus.Open)
                .ToListAsync(cancellationToken);
        Dictionary<Guid, CareHomeRatingFlag> flagsByRating = openFlags
            .GroupBy(flag => flag.RatingId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(f => f.CreatedOnUtc).First());

        return Result<AdminCareHomeRatingPage>.Success(new AdminCareHomeRatingPage(
            ratings.Select(rating =>
            {
                flagsByRating.TryGetValue(rating.Id, out CareHomeRatingFlag? flag);
                return new AdminCareHomeRatingListItem(
                    rating.Id,
                    rating.CareHomeId.Value,
                    rating.BookingId,
                    rating.FamilyId.Value,
                    rating.Stars,
                    rating.ReviewText,
                    rating.CreatedOnUtc,
                    rating.UpdatedOnUtc,
                    CareHomeRatingModerationFilter.Describe(rating.ModerationState),
                    rating.HiddenByUserId?.Value,
                    rating.HiddenOnUtc,
                    rating.HideReason,
                    flag?.Id,
                    flag?.Reason,
                    flag?.CreatedOnUtc);
            }).ToArray(),
            request.Page,
            request.PageSize,
            totalCount));
    }
}
