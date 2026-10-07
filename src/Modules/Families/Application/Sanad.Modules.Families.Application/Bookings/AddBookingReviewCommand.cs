using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Application.Bookings;

public sealed record AddBookingReviewCommand(
    Guid BookingId,
    Guid UserId,
    int Rating,
    string? Comment,
    bool IsAnonymous) : ICommand<BookingReviewResponse>;

public sealed record BookingReviewResponse(
    Guid Id,
    Guid BookingId,
    int Rating,
    string? Comment,
    bool IsAnonymous,
    DateTime CreatedOnUtc);
