using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Application.Discovery;

public sealed record TopRatedCaregiverCard(
    Guid CaregiverId,
    int Type,
    string ArabicFullName,
    string EnglishFullName,
    string? AvatarUrl,
    decimal AverageRating,
    int ReviewsCount);

public sealed record CaregiverRatingResponse(
    Guid RatingId,
    Guid BookingId,
    Guid CaregiverId,
    int Stars,
    string? ReviewText,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc);

public interface ICaregiverBookingRatingEligibility
{
    Task<AuthorizedCompletedCaregiverBooking?> GetEligibleBookingAsync(
        Guid bookingId,
        UserId actorUserId,
        CancellationToken cancellationToken);
}

public sealed record AuthorizedCompletedCaregiverBooking(
    Guid FamilyId,
    CaregiverId CaregiverId);

public sealed record RateCompletedCaregiverBookingCommand(
    Guid BookingId,
    UserId ActorUserId,
    int Stars,
    string? ReviewText,
    DateTime UtcNow)
    : ICommand<CaregiverRatingResponse>;

public sealed class RateCompletedCaregiverBookingCommandValidator
    : AbstractValidator<RateCompletedCaregiverBookingCommand>
{
    public RateCompletedCaregiverBookingCommandValidator()
    {
        RuleFor(command => command.BookingId).NotEmpty();
        RuleFor(command => command.ActorUserId).Must(id => id != UserId.Empty);
        RuleFor(command => command.Stars).InclusiveBetween(1, 5);
        RuleFor(command => command.ReviewText)
            .MaximumLength(CaregiverRating.MaximumReviewTextLength);
    }
}

public static class CaregiverRatingErrors
{
    public static readonly Error BookingNotEligible = new(
        "Caregivers.Rating.BookingNotEligible",
        "A completed caregiver booking in your Family is required to rate this service.");

    public static readonly Error CaregiverNotFound = new(
        "Caregivers.Rating.CaregiverNotFound",
        "The caregiver was not found.");

    public static readonly Error RatingConflict = new(
        "Caregivers.Rating.Conflict",
        "This completed booking already has a rating for another Family or caregiver.");
}

public sealed class RateCompletedCaregiverBookingCommandHandler
    : ICommandHandler<RateCompletedCaregiverBookingCommand, CaregiverRatingResponse>
{
    private readonly ICaregiversDbContext _dbContext;
    private readonly ICaregiverBookingRatingEligibility _eligibility;

    public RateCompletedCaregiverBookingCommandHandler(
        ICaregiversDbContext dbContext,
        ICaregiverBookingRatingEligibility eligibility)
    {
        _dbContext = dbContext;
        _eligibility = eligibility;
    }

    public async Task<Result<CaregiverRatingResponse>> Handle(
        RateCompletedCaregiverBookingCommand request,
        CancellationToken cancellationToken)
    {
        AuthorizedCompletedCaregiverBooking? eligibleBooking = await _eligibility.GetEligibleBookingAsync(
            request.BookingId,
            request.ActorUserId,
            cancellationToken);

        if (eligibleBooking is null)
            return Result<CaregiverRatingResponse>.Failure(CaregiverRatingErrors.BookingNotEligible);

        CaregiverId caregiverId = eligibleBooking.CaregiverId;
        Guid familyId = eligibleBooking.FamilyId;

        Caregiver? caregiver = await _dbContext.Caregivers
            .SingleOrDefaultAsync(item => item.Id == caregiverId, cancellationToken);
        if (caregiver is null)
            return Result<CaregiverRatingResponse>.Failure(CaregiverRatingErrors.CaregiverNotFound);

        CaregiverRating? rating = await _dbContext.CaregiverRatings
            .SingleOrDefaultAsync(item => item.BookingId == request.BookingId, cancellationToken);
        bool isNewRating = rating is null;

        if (rating is not null)
        {
            if (rating.CaregiverId != caregiverId || rating.FamilyId.Value != familyId)
                return Result<CaregiverRatingResponse>.Failure(CaregiverRatingErrors.RatingConflict);

            rating.Edit(request.ActorUserId, request.Stars, request.ReviewText, request.UtcNow);
        }
        else
        {
            rating = CaregiverRating.Create(
                caregiverId,
                request.BookingId,
                new FamilyId(familyId),
                request.ActorUserId,
                request.Stars,
                request.ReviewText,
                request.UtcNow);
            _dbContext.CaregiverRatings.Add(rating);
        }

        List<CaregiverRating> ratings = await _dbContext.CaregiverRatings
            .Where(item => item.CaregiverId == caregiverId)
            .ToListAsync(cancellationToken);
        if (isNewRating)
            ratings.Add(rating!);

        decimal average = ratings.Sum(item => (decimal)item.Stars) / ratings.Count;
        caregiver.UpdateFamilyRatingSummary(average, ratings.Count, request.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<CaregiverRatingResponse>.Success(new CaregiverRatingResponse(
            rating.Id,
            rating.BookingId,
            rating.CaregiverId.Value,
            rating.Stars,
            rating.ReviewText,
            rating.CreatedOnUtc,
            rating.UpdatedOnUtc));
    }
}

public sealed record GetTopRatedCaregiversQuery : IQuery<IReadOnlyList<TopRatedCaregiverCard>>;

public sealed class GetTopRatedCaregiversQueryHandler
    : IQueryHandler<GetTopRatedCaregiversQuery, IReadOnlyList<TopRatedCaregiverCard>>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetTopRatedCaregiversQueryHandler(ICaregiversDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<IReadOnlyList<TopRatedCaregiverCard>>> Handle(
        GetTopRatedCaregiversQuery request,
        CancellationToken cancellationToken) =>
        Result<IReadOnlyList<TopRatedCaregiverCard>>.Success(
            await _dbContext.GetTopRatedCaregiversAsync(cancellationToken));
}
