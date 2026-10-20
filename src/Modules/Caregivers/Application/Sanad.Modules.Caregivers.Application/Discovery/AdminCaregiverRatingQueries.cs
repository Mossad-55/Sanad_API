using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Application.Discovery;

/// <summary>
/// F7 admin moderation errors shared by the caregiver-rating reads and actions.
/// The status codes are pinned in the shared problem-details mapper (unknown
/// rating 404, bad reason 400, illegal transition 409) so both targets answer
/// identically.
/// </summary>
public static class CaregiverRatingModerationErrors
{
    public static readonly Error NotFound = new(
        "Caregivers.Rating.NotFound",
        "The caregiver rating was not found.");

    public static readonly Error InvalidReason = new(
        "Caregivers.Rating.InvalidReason",
        "A moderation reason between 1 and 500 characters is required.");

    public static readonly Error AlreadyHidden = new(
        "Caregivers.Rating.AlreadyHidden",
        "The caregiver rating is already hidden.");

    public static readonly Error AlreadyVisible = new(
        "Caregivers.Rating.AlreadyVisible",
        "The caregiver rating is already visible.");

    public static readonly Error InvalidQuery = new(
        "Caregivers.Rating.InvalidQuery",
        "The rating filter is invalid.");
}

/// <summary>
/// F7 admin rating item. Exposes the moderation facts and the retained rating
/// content only: no reviewer name, phone, email or avatar is ever projected.
/// </summary>
public sealed record AdminCaregiverRatingListItem(
    Guid RatingId,
    Guid CaregiverId,
    Guid BookingId,
    Guid FamilyId,
    int Stars,
    string? ReviewText,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc,
    string ModerationState,
    Guid? HiddenByUserId,
    DateTime? HiddenOnUtc,
    string? HideReason);

public sealed record AdminCaregiverRatingPage(
    IReadOnlyList<AdminCaregiverRatingListItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record GetAdminCaregiverRatingsQuery(
    Guid? CaregiverId,
    int? Stars,
    string? Moderation,
    int Page,
    int PageSize) : IQuery<AdminCaregiverRatingPage>;

/// <summary>
/// The moderation filter accepts exactly <c>visible</c>, <c>hidden</c> or
/// <c>all</c> (default <c>all</c>); anything else is a 400, never a silently
/// different result set.
/// </summary>
public static class RatingModerationFilter
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

    public static string Describe(RatingModerationState state) =>
        state == RatingModerationState.Hidden ? "hidden" : "visible";
}

public sealed class GetAdminCaregiverRatingsQueryHandler(ICaregiversDbContext dbContext)
    : IQueryHandler<GetAdminCaregiverRatingsQuery, AdminCaregiverRatingPage>
{
    public async Task<Result<AdminCaregiverRatingPage>> Handle(
        GetAdminCaregiverRatingsQuery request,
        CancellationToken cancellationToken)
    {
        // Validated in the handler rather than by a FluentValidation rule so an
        // invalid filter answers the documented Caregivers.Rating.InvalidQuery
        // (400) code instead of the pipeline's generic Api.Validation.Failed.
        if (request.Page < 1 ||
            request.PageSize is < 1 or > 100 ||
            (request.Stars is int stars && stars is < 1 or > 5) ||
            !RatingModerationFilter.TryParse(request.Moderation, out bool? filterHidden))
        {
            return Result<AdminCaregiverRatingPage>.Failure(CaregiverRatingModerationErrors.InvalidQuery);
        }

        IQueryable<CaregiverRating> query = dbContext.CaregiverRatings.AsNoTracking();

        if (request.CaregiverId is Guid caregiverId)
            query = query.Where(rating => rating.CaregiverId == new CaregiverId(caregiverId));
        if (request.Stars is int starFilter)
            query = query.Where(rating => rating.Stars == starFilter);
        if (filterHidden is bool hidden)
        {
            RatingModerationState state = hidden
                ? RatingModerationState.Hidden
                : RatingModerationState.Visible;
            query = query.Where(rating => rating.ModerationState == state);
        }

        int totalCount = await query.CountAsync(cancellationToken);

        List<CaregiverRating> ratings = await query
            .OrderByDescending(rating => rating.CreatedOnUtc)
            .ThenByDescending(rating => rating.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return Result<AdminCaregiverRatingPage>.Success(new AdminCaregiverRatingPage(
            ratings.Select(rating => new AdminCaregiverRatingListItem(
                rating.Id,
                rating.CaregiverId.Value,
                rating.BookingId,
                rating.FamilyId.Value,
                rating.Stars,
                rating.ReviewText,
                rating.CreatedOnUtc,
                rating.UpdatedOnUtc,
                RatingModerationFilter.Describe(rating.ModerationState),
                rating.HiddenByUserId?.Value,
                rating.HiddenOnUtc,
                rating.HideReason)).ToArray(),
            request.Page,
            request.PageSize,
            totalCount));
    }
}
