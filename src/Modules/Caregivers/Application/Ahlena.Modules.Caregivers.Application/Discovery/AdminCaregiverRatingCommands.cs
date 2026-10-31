using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Caregivers.Application.Abstractions.Data;
using Ahlena.Modules.Caregivers.Domain.Caregivers;

namespace Ahlena.Modules.Caregivers.Application.Discovery;

public sealed record HideAdminCaregiverRatingCommand(
    Guid RatingId,
    UserId ActorUserId,
    string? Reason,
    DateTime UtcNow) : ICommand<bool>;

public sealed record RestoreAdminCaregiverRatingCommand(
    Guid RatingId,
    UserId ActorUserId,
    string? Reason,
    DateTime UtcNow) : ICommand<bool>;

/// <summary>
/// F7 caregiver-rating moderation. Hide and restore are the same attributed
/// policy as the Care Home target: the rating row is never mutated past its
/// visibility state (stars, text, authorship and eligibility are untouched),
/// every transition appends one audit row, and the stored caregiver aggregate
/// is recomputed over the non-hidden set inside the same transaction so the
/// public profile, search and earnings consumers agree with the SQL-computed
/// top-10 aggregate.
/// </summary>
public sealed class HideAdminCaregiverRatingCommandHandler(ICaregiversDbContext dbContext)
    : ICommandHandler<HideAdminCaregiverRatingCommand, bool>
{
    public Task<Result<bool>> Handle(
        HideAdminCaregiverRatingCommand request,
        CancellationToken cancellationToken) =>
        CaregiverRatingModeration.ApplyAsync(
            dbContext,
            request.RatingId,
            request.ActorUserId,
            request.Reason,
            request.UtcNow,
            hide: true,
            cancellationToken);
}

public sealed class RestoreAdminCaregiverRatingCommandHandler(ICaregiversDbContext dbContext)
    : ICommandHandler<RestoreAdminCaregiverRatingCommand, bool>
{
    public Task<Result<bool>> Handle(
        RestoreAdminCaregiverRatingCommand request,
        CancellationToken cancellationToken) =>
        CaregiverRatingModeration.ApplyAsync(
            dbContext,
            request.RatingId,
            request.ActorUserId,
            request.Reason,
            request.UtcNow,
            hide: false,
            cancellationToken);
}

internal static class CaregiverRatingModeration
{
    public static async Task<Result<bool>> ApplyAsync(
        ICaregiversDbContext dbContext,
        Guid ratingId,
        UserId actorUserId,
        string? reason,
        DateTime utcNow,
        bool hide,
        CancellationToken cancellationToken)
    {
        // Pinned precedence: unknown rating (404), then reason shape (400),
        // then the illegal transition (409) — never a silent no-op.
        CaregiverRating? rating = ratingId == Guid.Empty
            ? null
            : await dbContext.CaregiverRatings
                .SingleOrDefaultAsync(item => item.Id == ratingId, cancellationToken);

        if (rating is null)
            return Result<bool>.Failure(CaregiverRatingModerationErrors.NotFound);

        Caregiver? caregiver = await dbContext.Caregivers
            .SingleOrDefaultAsync(item => item.Id == rating.CaregiverId, cancellationToken);
        if (caregiver is null)
            return Result<bool>.Failure(CaregiverRatingModerationErrors.NotFound);

        if (string.IsNullOrWhiteSpace(reason) ||
            reason.Trim().Length > CaregiverRating.MaximumModerationReasonLength)
        {
            return Result<bool>.Failure(CaregiverRatingModerationErrors.InvalidReason);
        }

        if (hide && rating.IsHidden)
            return Result<bool>.Failure(CaregiverRatingModerationErrors.AlreadyHidden);
        if (!hide && !rating.IsHidden)
            return Result<bool>.Failure(CaregiverRatingModerationErrors.AlreadyVisible);

        try
        {
            if (hide)
                rating.Hide(actorUserId, reason, utcNow);
            else
                rating.Restore(actorUserId, reason, utcNow);
        }
        catch (DomainException)
        {
            return Result<bool>.Failure(hide
                ? CaregiverRatingModerationErrors.AlreadyHidden
                : CaregiverRatingModerationErrors.AlreadyVisible);
        }

        dbContext.RatingModerationActions.Add(CaregiverRatingModerationAction.Record(
            rating.Id,
            rating.CaregiverId,
            hide
                ? CaregiverRatingModerationActionType.Hidden
                : CaregiverRatingModerationActionType.Restored,
            actorUserId,
            reason,
            utcNow));

        await CaregiverRatingAggregate.RefreshAsync(
            dbContext, caregiver, pendingRating: null, utcNow, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }
}
