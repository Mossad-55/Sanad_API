using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Microsoft.EntityFrameworkCore;
using FeedbackEntity = Sanad.Modules.Identity.Domain.Feedback;

namespace Sanad.Modules.Identity.Application.Feedback;

public sealed class AppRatingFeedbackCommandHandler(
    IIdentityDbContext dbContext) : ICommandHandler<AppRatingFeedbackCommand, bool>
{
    public async Task<Result<bool>> Handle(
        AppRatingFeedbackCommand request,
        CancellationToken cancellationToken)
    {
        // Validate rating range
        if (request.Rating < 1 || request.Rating > 5)
        {
            return Result<bool>.Failure(
                new Error("Feedback.InvalidRating", "Rating must be between 1 and 5."));
        }

        // Create feedback record
        var feedback = new FeedbackEntity(
            Guid.NewGuid(),
            new UserId(request.UserId),
            request.Rating,
            request.Comment,
            request.DeviceInfo,
            request.AppVersion,
            DateTime.UtcNow);

        dbContext.Feedbacks.Add(feedback);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }
}
