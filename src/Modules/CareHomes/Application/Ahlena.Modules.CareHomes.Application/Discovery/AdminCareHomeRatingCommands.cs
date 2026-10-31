using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.CareHomes.Application.Abstractions.Data;
using Ahlena.Modules.CareHomes.Domain.Bookings;
using Ahlena.Modules.CareHomes.Domain.Facilities;

namespace Ahlena.Modules.CareHomes.Application.Discovery;

public sealed record HideAdminCareHomeRatingCommand(
    Guid RatingId,
    UserId ActorUserId,
    string? Reason,
    DateTime UtcNow) : ICommand<bool>;

public sealed record KeepAdminCareHomeRatingCommand(
    Guid RatingId,
    UserId ActorUserId,
    string? Reason,
    DateTime UtcNow) : ICommand<bool>;

public sealed record RestoreAdminCareHomeRatingCommand(
    Guid RatingId,
    UserId ActorUserId,
    string? Reason,
    DateTime UtcNow) : ICommand<bool>;

/// <summary>
/// F7 Care Home-rating moderation. Same attributed hide/restore policy as the
/// caregiver target, plus <c>keep</c>: it leaves the rating visible and
/// resolves the unresolved owner flag as Kept. Hiding and keeping resolve that
/// flag with attribution in the same transaction, and the Care Home aggregate
/// is computed per query over the non-hidden set, so no stored value needs
/// recomputing.
/// </summary>
public sealed class HideAdminCareHomeRatingCommandHandler(ICareHomesDbContext dbContext)
    : ICommandHandler<HideAdminCareHomeRatingCommand, bool>
{
    public async Task<Result<bool>> Handle(
        HideAdminCareHomeRatingCommand request,
        CancellationToken cancellationToken)
    {
        var rating = await CareHomeRatingAction.FindAsync(dbContext, request.RatingId, cancellationToken);
        if (rating is null)
            return Result<bool>.Failure(CareHomeRatingModerationErrors.NotFound);

        if (CareHomeRatingAction.InvalidReason(request.Reason))
            return Result<bool>.Failure(CareHomeRatingModerationErrors.InvalidReason);

        if (rating.IsHidden)
            return Result<bool>.Failure(CareHomeRatingModerationErrors.AlreadyHidden);

        try
        {
            rating.Hide(request.ActorUserId, request.Reason!, request.UtcNow);
        }
        catch (InvalidOperationException)
        {
            return Result<bool>.Failure(CareHomeRatingModerationErrors.AlreadyHidden);
        }
        catch (ArgumentException)
        {
            return Result<bool>.Failure(CareHomeRatingModerationErrors.InvalidReason);
        }

        CareHomeRatingAction.Record(dbContext, rating, CareHomeRatingModerationActionType.Hidden,
            request.ActorUserId, request.Reason!, request.UtcNow);

        // Hiding resolves the rating's unresolved owner flag as Hidden, with the
        // same admin as resolver.
        var resolution = await CareHomeRatingAction.ResolveOpenFlagAsync(
            dbContext, rating.Id, request.ActorUserId, CareHomeRatingFlagResolution.Hidden,
            request.UtcNow, cancellationToken);
        if (resolution.IsFailure)
            return Result<bool>.Failure(resolution.Error);

        return await CareHomeRatingAction.SaveAsync(dbContext, cancellationToken);
    }
}

public sealed class KeepAdminCareHomeRatingCommandHandler(ICareHomesDbContext dbContext)
    : ICommandHandler<KeepAdminCareHomeRatingCommand, bool>
{
    public async Task<Result<bool>> Handle(
        KeepAdminCareHomeRatingCommand request,
        CancellationToken cancellationToken)
    {
        var rating = await CareHomeRatingAction.FindAsync(dbContext, request.RatingId, cancellationToken);
        if (rating is null)
            return Result<bool>.Failure(CareHomeRatingModerationErrors.NotFound);

        if (CareHomeRatingAction.InvalidReason(request.Reason))
            return Result<bool>.Failure(CareHomeRatingModerationErrors.InvalidReason);

        // Keep changes no visibility: it only closes the open flag as Kept.
        var resolution = await CareHomeRatingAction.ResolveOpenFlagAsync(
            dbContext, rating.Id, request.ActorUserId, CareHomeRatingFlagResolution.Kept,
            request.UtcNow, cancellationToken);
        if (resolution.IsFailure)
            return Result<bool>.Failure(resolution.Error);

        return await CareHomeRatingAction.SaveAsync(dbContext, cancellationToken);
    }
}

public sealed class RestoreAdminCareHomeRatingCommandHandler(ICareHomesDbContext dbContext)
    : ICommandHandler<RestoreAdminCareHomeRatingCommand, bool>
{
    public async Task<Result<bool>> Handle(
        RestoreAdminCareHomeRatingCommand request,
        CancellationToken cancellationToken)
    {
        var rating = await CareHomeRatingAction.FindAsync(dbContext, request.RatingId, cancellationToken);
        if (rating is null)
            return Result<bool>.Failure(CareHomeRatingModerationErrors.NotFound);

        if (CareHomeRatingAction.InvalidReason(request.Reason))
            return Result<bool>.Failure(CareHomeRatingModerationErrors.InvalidReason);

        if (!rating.IsHidden)
            return Result<bool>.Failure(CareHomeRatingModerationErrors.AlreadyVisible);

        try
        {
            rating.Restore(request.ActorUserId, request.Reason!, request.UtcNow);
        }
        catch (InvalidOperationException)
        {
            return Result<bool>.Failure(CareHomeRatingModerationErrors.AlreadyVisible);
        }
        catch (ArgumentException)
        {
            return Result<bool>.Failure(CareHomeRatingModerationErrors.InvalidReason);
        }

        CareHomeRatingAction.Record(dbContext, rating, CareHomeRatingModerationActionType.Restored,
            request.ActorUserId, request.Reason!, request.UtcNow);

        return await CareHomeRatingAction.SaveAsync(dbContext, cancellationToken);
    }
}

internal static class CareHomeRatingAction
{
    public static Task<CareHomeRating?> FindAsync(
        ICareHomesDbContext dbContext, Guid ratingId, CancellationToken cancellationToken) =>
        ratingId == Guid.Empty
            ? Task.FromResult<CareHomeRating?>(null)
            : dbContext.Ratings.SingleOrDefaultAsync(rating => rating.Id == ratingId, cancellationToken);

    /// <summary>Pinned precedence: unknown rating (404), reason (400), state (409).</summary>
    public static bool InvalidReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ||
        reason.Trim().Length > CareHomeRating.MaximumModerationReasonLength;

    public static void Record(
        ICareHomesDbContext dbContext,
        CareHomeRating rating,
        CareHomeRatingModerationActionType action,
        UserId actorUserId,
        string reason,
        DateTime utcNow) =>
        dbContext.RatingModerationActions.Add(CareHomeRatingModerationAction.Record(
            rating.Id, rating.CareHomeId, action, actorUserId, reason, utcNow));

    /// <summary>
    /// Resolves the rating's single unresolved flag. No open flag is a caller
    /// error for <c>keep</c> (409) and a harmless no-op for <c>hide</c>, which
    /// is why the caller decides how to treat <see cref="CareHomeRatingFlagResolution"/>.
    /// </summary>
    public static async Task<Result<bool>> ResolveOpenFlagAsync(
        ICareHomesDbContext dbContext,
        Guid ratingId,
        UserId actorUserId,
        CareHomeRatingFlagResolution resolution,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        CareHomeRatingFlag? flag = await dbContext.RatingFlags
            .SingleOrDefaultAsync(candidate => candidate.RatingId == ratingId &&
                candidate.Status == CareHomeRatingFlagStatus.Open, cancellationToken);

        if (flag is null)
        {
            // Hiding a rating that was never flagged is legitimate: the flag is
            // an owner-driven signal, not a precondition for moderation.
            return resolution == CareHomeRatingFlagResolution.Hidden
                ? Result<bool>.Success(true)
                : Result<bool>.Failure(CareHomeRatingModerationErrors.NoOpenFlag);
        }

        try
        {
            flag.Resolve(actorUserId, resolution, utcNow);
        }
        catch (InvalidOperationException)
        {
            return Result<bool>.Failure(CareHomeRatingModerationErrors.NoOpenFlag);
        }

        return Result<bool>.Success(true);
    }

    public static async Task<Result<bool>> SaveAsync(
        ICareHomesDbContext dbContext, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent flag resolution wins: the caller sees a 409 rather
            // than a silent second write.
            return Result<bool>.Failure(CareHomeRatingModerationErrors.NoOpenFlag);
        }

        return Result<bool>.Success(true);
    }
}
