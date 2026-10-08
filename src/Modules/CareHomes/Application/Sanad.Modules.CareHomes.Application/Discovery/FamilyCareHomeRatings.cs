using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Application.Discovery;

public sealed record CareHomeRatingResponse(Guid RatingId, Guid BookingId, Guid CareHomeId,
    int Stars, string? ReviewText, DateTime CreatedOnUtc, DateTime UpdatedOnUtc);

public sealed record TopRatedCareHomeCard(Guid CareHomeId, string ArabicName, string EnglishName,
    decimal AverageRating, int ReviewsCount);

public sealed record RateCareHomeBookingCommand(Guid BookingId, UserId ActorUserId, FamilyId FamilyId,
    int Stars, string? ReviewText, DateTime UtcNow) : ICommand<CareHomeRatingResponse>;

public sealed class RateCareHomeBookingCommandValidator : AbstractValidator<RateCareHomeBookingCommand>
{
    public RateCareHomeBookingCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.ActorUserId).Must(x => x != UserId.Empty);
        RuleFor(x => x.FamilyId).Must(x => x != FamilyId.Empty);
        RuleFor(x => x.Stars).InclusiveBetween(1, 5);
        RuleFor(x => x.ReviewText).MaximumLength(CareHomeRating.MaximumReviewTextLength);
    }
}

public static class CareHomeRatingErrors
{
    public static readonly Error BookingNotEligible = new("CareHomes.Rating.BookingNotEligible",
        "A booking with Family-confirmed check-in in your Family is required to rate this care home.");
    public static readonly Error RatingConflict = new("CareHomes.Rating.Conflict",
        "This booking already has a rating for another Family or care home.");
}

public sealed class RateCareHomeBookingCommandHandler(ICareHomesDbContext db)
    : ICommandHandler<RateCareHomeBookingCommand, CareHomeRatingResponse>
{
    public async Task<Result<CareHomeRatingResponse>> Handle(RateCareHomeBookingCommand request, CancellationToken ct)
    {
        var booking = await db.Bookings.SingleOrDefaultAsync(x => x.Id == request.BookingId &&
            x.FamilyId == request.FamilyId && x.FamilyCheckInConfirmedOnUtc != null, ct);
        if (booking is null)
            return Result<CareHomeRatingResponse>.Failure(CareHomeRatingErrors.BookingNotEligible);

        CareHomeRating? rating = await db.Ratings.SingleOrDefaultAsync(x => x.BookingId == booking.Id, ct);
        if (rating is not null)
        {
            if (rating.CareHomeId != booking.FacilityId || rating.FamilyId != booking.FamilyId)
                return Result<CareHomeRatingResponse>.Failure(CareHomeRatingErrors.RatingConflict);
            rating.Edit(request.ActorUserId, request.Stars, request.ReviewText, request.UtcNow);
        }
        else
        {
            rating = CareHomeRating.Create(booking.Id, booking.FacilityId, booking.FamilyId,
                request.ActorUserId, request.Stars, request.ReviewText, request.UtcNow);
            db.Ratings.Add(rating);
        }

        await db.SaveChangesAsync(ct);
        return Result<CareHomeRatingResponse>.Success(new(rating.Id, rating.BookingId,
            rating.CareHomeId.Value, rating.Stars, rating.ReviewText, rating.CreatedOnUtc, rating.UpdatedOnUtc));
    }
}

public sealed record GetTopRatedCareHomesQuery : IQuery<IReadOnlyList<TopRatedCareHomeCard>>;

public sealed class GetTopRatedCareHomesQueryHandler(ICareHomesDbContext db, IDateTimeProvider clock)
    : IQueryHandler<GetTopRatedCareHomesQuery, IReadOnlyList<TopRatedCareHomeCard>>
{
    public async Task<Result<IReadOnlyList<TopRatedCareHomeCard>>> Handle(GetTopRatedCareHomesQuery request, CancellationToken ct)
    {
        var reader = new CareHomeDiscoveryReader(db, clock);
        DateOnly today = CareHomePublicEligibility.CairoDate(clock.UtcNow);
        List<CareHomeId> eligibleIds = await reader.EligibleQuery(today)
            .Select(x => x.Id).ToListAsync(ct);
        var aggregates = await db.Ratings.AsNoTracking().Where(x => eligibleIds.Contains(x.CareHomeId))
            .GroupBy(x => x.CareHomeId)
            .Select(group => new { CareHomeId = group.Key, Average = group.Average(x => (decimal)x.Stars), Count = group.Count() })
            .OrderByDescending(x => x.Average).ThenByDescending(x => x.Count).ThenBy(x => x.CareHomeId)
            .Take(10)
            .ToListAsync(ct);

        var result = new List<TopRatedCareHomeCard>(10);
        foreach (var aggregate in aggregates)
        {
            var eligible = await reader.ReadEligibleDetail(aggregate.CareHomeId.Value, ct);
            if (eligible is null) continue;
            result.Add(new(eligible.Id, eligible.Summary.ArabicName, eligible.Summary.EnglishName,
                decimal.Round(aggregate.Average, 2, MidpointRounding.AwayFromZero), aggregate.Count));
        }
        return Result<IReadOnlyList<TopRatedCareHomeCard>>.Success(result);
    }
}
