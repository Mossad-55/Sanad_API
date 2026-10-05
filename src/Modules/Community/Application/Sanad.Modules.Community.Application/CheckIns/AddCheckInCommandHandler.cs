using Sanad.Modules.Community.Application.Abstractions.Data;
using Sanad.Modules.Community.Domain.CheckIns;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Community.Application.CheckIns;

public sealed class AddCheckInCommandHandler : ICommandHandler<AddCheckInCommand, bool>
{
    private readonly ICommunityDbContext _dbContext;

    public AddCheckInCommandHandler(ICommunityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<bool>> Handle(AddCheckInCommand request, CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty || string.IsNullOrWhiteSpace(request.TimeZoneId))
            return Result<bool>.Failure(new Error(
                "Community.CheckIn.Invalid",
                "A valid user and time zone are required."));

        try { _ = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId); }
        catch (TimeZoneNotFoundException) { return Result<bool>.Failure(new Error("Community.CheckIn.Invalid", "The time zone is not recognized.")); }
        catch (InvalidTimeZoneException) { return Result<bool>.Failure(new Error("Community.CheckIn.Invalid", "The time zone is invalid.")); }

        var post = await _dbContext.Posts.FindAsync([new CommunityPostId(request.PostId)], cancellationToken);

        if (post is null || post.Status != PostStatus.Published)
            return Result<bool>.Failure(new Error("Community.PostNotFound", "Published community post not found."));

        // Check if user already has a check-in for today
        var existingCheckIn = await _dbContext.CheckIns
            .FirstOrDefaultAsync(c =>
                c.PostId == new CommunityPostId(request.PostId) &&
                c.UserId == new UserId(request.UserId) &&
                c.LocalDate == request.LocalDate);

        try
        {
            if (existingCheckIn is not null)
            {
                existingCheckIn.UpdateAnswer(
                    request.Answer,
                    request.TimeZoneId,
                    request.LocalDate,
                    request.AnsweredAtLocalTime,
                    DateTime.UtcNow);
            }
            else
            {
                _dbContext.CheckIns.Add(CheckIn.Create(
                    new CommunityPostId(request.PostId),
                    new UserId(request.UserId),
                    request.Answer,
                    request.TimeZoneId,
                    request.LocalDate,
                    request.AnsweredAtLocalTime,
                    DateTime.UtcNow));
            }
        }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException exception)
        {
            return Result<bool>.Failure(new Error("Community.CheckIn.Invalid", exception.Message));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
