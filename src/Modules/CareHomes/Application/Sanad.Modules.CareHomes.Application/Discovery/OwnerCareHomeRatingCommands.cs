using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Application.Discovery;

public sealed record FlagOwnerCareHomeRatingCommand(
    UserId ActorUserId,
    Guid RatingId,
    string? Reason,
    DateTime UtcNow) : ICommand<bool>;

/// <summary>
/// F7 owner flag. An owner may flag a rating about their own facility only
/// (anything else is a uniform 404, so the route never confirms whether an
/// unknown rating exists). At most one unresolved flag exists per
/// (rating, owner): a repeat flag while one is open returns success and writes
/// nothing — no duplicate row and no history row — while a flag raised after a
/// resolution opens a fresh case. The filtered unique index is the final
/// arbiter when two requests race.
/// </summary>
public sealed class FlagOwnerCareHomeRatingCommandHandler(ICareHomesDbContext dbContext)
    : ICommandHandler<FlagOwnerCareHomeRatingCommand, bool>
{
    private static readonly Error RatingNotFound = new(
        "CareHomes.Rating.NotFound", "The care-home rating was not found.");

    public async Task<Result<bool>> Handle(
        FlagOwnerCareHomeRatingCommand request,
        CancellationToken cancellationToken)
    {
        CareHomeFacility? facility = await dbContext.Facilities.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.OwnerUserId == request.ActorUserId, cancellationToken);
        if (facility is null)
            return Result<bool>.Failure(RatingNotFound);

        CareHomeRating? rating = request.RatingId == Guid.Empty
            ? null
            : await dbContext.Ratings.AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == request.RatingId && candidate.CareHomeId == facility.Id,
                    cancellationToken);
        if (rating is null)
            return Result<bool>.Failure(RatingNotFound);

        if (string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length > CareHomeRatingFlag.MaximumReasonLength)
        {
            return Result<bool>.Failure(CareHomeRatingModerationErrors.InvalidReason);
        }

        bool alreadyOpen = await dbContext.RatingFlags.AsNoTracking()
            .AnyAsync(flag => flag.RatingId == rating.Id &&
                flag.OwnerUserId == request.ActorUserId &&
                flag.Status == CareHomeRatingFlagStatus.Open, cancellationToken);
        if (alreadyOpen)
            return Result<bool>.Success(true);

        CareHomeRatingFlag created = CareHomeRatingFlag.Open(
            rating.Id, facility.Id, request.ActorUserId, request.Reason!, request.UtcNow);
        dbContext.RatingFlags.Add(created);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsOpenFlagDuplicate(exception))
        {
            // Two requests raced past the read-before-insert check: the unique
            // index kept one row, and the loser's repeat is still a 204 no-op.
            if (dbContext is DbContext context)
                context.Entry(created).State = EntityState.Detached;

            return Result<bool>.Success(true);
        }

        return Result<bool>.Success(true);
    }

    private static bool IsOpenFlagDuplicate(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            Type type = current.GetType();
            string? sqlState = type.GetProperty("SqlState")?.GetValue(current) as string;
            string? constraintName = type.GetProperty("ConstraintName")?.GetValue(current) as string;
            if (sqlState == "23505" &&
                (constraintName == OpenFlagIndex ||
                 current.Message.Contains(OpenFlagIndex, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private const string OpenFlagIndex = "IX_rating_flags_rating_id_owner_user_id";
}
