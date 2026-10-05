using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Families;
using Microsoft.EntityFrameworkCore;

namespace Sanad.Modules.Families.Application.Bookings;

public sealed class AddBookingReviewCommandHandler(
    IFamiliesDbContext dbContext) : ICommandHandler<AddBookingReviewCommand, BookingReviewResponse>
{
    public async Task<Result<BookingReviewResponse>> Handle(
        AddBookingReviewCommand request,
        CancellationToken cancellationToken)
    {
        var booking = await dbContext.Bookings
            .SingleOrDefaultAsync(
                b => b.Id == new BookingId(request.BookingId),
                cancellationToken);

        if (booking is null)
        {
            return Result<BookingReviewResponse>.Failure(
                new Error("Bookings.NotFound", "Booking not found."));
        }

        if (booking.Status != BookingStatus.Completed)
        {
            return Result<BookingReviewResponse>.Failure(
                new Error("Bookings.ReviewNotAllowed", "A review can only be added after a booking is completed."));
        }

        // Check if user has access to this booking
        var family = await dbContext.Families
            .SingleOrDefaultAsync(
                f => f.Id == booking.FamilyId,
                cancellationToken);

        if (family is null)
        {
            return Result<BookingReviewResponse>.Failure(
                new Error("Bookings.AccessDenied", "Access denied to this booking."));
        }

        var role = family.GetRole(new UserId(request.UserId));
        if (role is null || !(role == FamilyRole.Owner || role == FamilyRole.Editor))
        {
            return Result<BookingReviewResponse>.Failure(
                new Error("Bookings.AccessDenied", "Only family owners/editors can add reviews."));
        }

        // Check if review already exists
        var existingReview = await dbContext.BookingReviews
            .SingleOrDefaultAsync(
                r => r.BookingId == new BookingId(request.BookingId),
                cancellationToken);

        if (existingReview is not null)
        {
            return Result<BookingReviewResponse>.Failure(
                new Error("Bookings.ReviewExists", "A review already exists for this booking."));
        }

        BookingReview review;
        try
        {
            review = BookingReview.Create(
                booking.Id,
                new UserId(request.UserId),
                request.Rating,
                request.Comment,
                request.IsAnonymous,
                DateTime.UtcNow);
        }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException exception)
        {
            return Result<BookingReviewResponse>.Failure(
                new Error("Bookings.InvalidReview", exception.Message));
        }

        dbContext.BookingReviews.Add(review);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<BookingReviewResponse>.Success(
            new BookingReviewResponse(
                review.Id.Value,
                review.BookingId.Value,
                review.Rating,
                review.Comment,
                review.IsAnonymous,
                review.CreatedOnUtc));
    }
}
