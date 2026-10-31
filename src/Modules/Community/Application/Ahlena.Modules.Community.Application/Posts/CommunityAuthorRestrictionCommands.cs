using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Community.Application.Abstractions.Data;
using Ahlena.Modules.Community.Domain.Posts;

namespace Ahlena.Modules.Community.Application.Posts;

/// <summary>
/// Shared Community write gate. A restriction blocks new posts, comments, and
/// replies only; existing-content edits/deletes and every non-write
/// interaction remain available. Expiry is enforced by comparing UTC
/// timestamps here, with no background job.
/// </summary>
public static class CommunityAuthorRestrictionGuard
{
    public static Task<bool> IsAuthorRestrictedAsync(
        ICommunityDbContext dbContext,
        UserId authorId,
        DateTime utcNow,
        CancellationToken cancellationToken) =>
        dbContext.CommunityAuthorRestrictions
            .AsNoTracking()
            .AnyAsync(
                r => r.AuthorId == authorId &&
                     r.RevokedOnUtc == null &&
                     r.ExpiresOnUtc > utcNow,
                cancellationToken);
}

public sealed record CommunityAuthorRestrictionResponse(
    Guid RestrictionId,
    Guid AuthorId,
    Guid ModeratorId,
    string Duration,
    string ReasonCode,
    string? Note,
    DateTime StartsOnUtc,
    DateTime ExpiresOnUtc,
    Guid? RevokedBy,
    DateTime? RevokedOnUtc,
    string? RevocationReason,
    bool IsActive);

public sealed record CreateCommunityAuthorRestrictionCommand(
    Guid AuthorId,
    UserId ModeratorId,
    string? Duration,
    string? ReasonCode,
    string? Note) : ICommand<CommunityAuthorRestrictionResponse>;

public sealed class CreateCommunityAuthorRestrictionCommandHandler(ICommunityDbContext dbContext)
    : ICommandHandler<CreateCommunityAuthorRestrictionCommand, CommunityAuthorRestrictionResponse>
{
    public async Task<Result<CommunityAuthorRestrictionResponse>> Handle(
        CreateCommunityAuthorRestrictionCommand request,
        CancellationToken cancellationToken)
    {
        if (!TryParseDuration(request.Duration, out CommunityRestrictionDuration duration))
            return Result<CommunityAuthorRestrictionResponse>.Failure(
                CommunityModerationErrors.InvalidRestrictionDuration);

        if (!CommunityRestrictionReasonCodes.IsValid(request.ReasonCode))
            return Result<CommunityAuthorRestrictionResponse>.Failure(
                CommunityModerationErrors.InvalidRestrictionReason);

        string? normalizedNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (CommunityRestrictionReasonCodes.RequiresNote(request.ReasonCode) && normalizedNote is null)
            return Result<CommunityAuthorRestrictionResponse>.Failure(
                CommunityModerationErrors.InvalidRestrictionReason);

        var authorId = new UserId(request.AuthorId);
        DateTime now = DateTime.UtcNow;

        await using var transaction = await dbContext
            .BeginCommunityAuthorRestrictionTransactionAsync(authorId, cancellationToken);

        bool alreadyActive = await CommunityAuthorRestrictionGuard.IsAuthorRestrictedAsync(
            dbContext, authorId, now, cancellationToken);
        if (alreadyActive)
            return Result<CommunityAuthorRestrictionResponse>.Failure(
                CommunityModerationErrors.RestrictionAlreadyActive);

        CommunityAuthorRestriction restriction;
        try
        {
            restriction = CommunityAuthorRestriction.Create(
                authorId, request.ModeratorId, duration, request.ReasonCode, normalizedNote, now);
        }
        catch (DomainException)
        {
            return Result<CommunityAuthorRestrictionResponse>.Failure(
                CommunityModerationErrors.InvalidRestrictionReason);
        }

        dbContext.CommunityAuthorRestrictions.Add(restriction);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);

        return Result<CommunityAuthorRestrictionResponse>.Success(ToResponse(restriction));
    }

    private static bool TryParseDuration(string? value, out CommunityRestrictionDuration duration)
    {
        if (value is not null &&
            Enum.TryParse(value.Trim(), ignoreCase: true, out CommunityRestrictionDuration parsed) &&
            parsed is CommunityRestrictionDuration.Hours24
                or CommunityRestrictionDuration.Days7
                or CommunityRestrictionDuration.Days30)
        {
            duration = parsed;
            return true;
        }

        duration = default;
        return false;
    }

    internal static CommunityAuthorRestrictionResponse ToResponse(CommunityAuthorRestriction restriction) =>
        new(
            restriction.Id.Value,
            restriction.AuthorId.Value,
            restriction.ModeratorId.Value,
            restriction.Duration.ToString(),
            restriction.ReasonCode,
            restriction.Note,
            restriction.StartsOnUtc,
            restriction.ExpiresOnUtc,
            restriction.RevokedBy?.Value,
            restriction.RevokedOnUtc,
            restriction.RevocationReason,
            restriction.IsActiveAt(DateTime.UtcNow));
}

public sealed record RevokeCommunityAuthorRestrictionCommand(
    Guid RestrictionId,
    UserId ModeratorId,
    string? Reason) : ICommand<CommunityAuthorRestrictionResponse>;

public sealed class RevokeCommunityAuthorRestrictionCommandHandler(ICommunityDbContext dbContext)
    : ICommandHandler<RevokeCommunityAuthorRestrictionCommand, CommunityAuthorRestrictionResponse>
{
    public async Task<Result<CommunityAuthorRestrictionResponse>> Handle(
        RevokeCommunityAuthorRestrictionCommand request,
        CancellationToken cancellationToken)
    {
        string reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0 ||
            reason.Length > CommunityAuthorRestriction.MaximumRevocationReasonLength)
            return Result<CommunityAuthorRestrictionResponse>.Failure(
                CommunityModerationErrors.InvalidRevocationReason);

        var restriction = await dbContext.CommunityAuthorRestrictions
            .SingleOrDefaultAsync(
                r => r.Id == new CommunityAuthorRestrictionId(request.RestrictionId),
                cancellationToken);

        if (restriction is null)
            return Result<CommunityAuthorRestrictionResponse>.Failure(
                CommunityModerationErrors.RestrictionNotFound);

        try
        {
            restriction.Revoke(request.ModeratorId, reason, DateTime.UtcNow);
        }
        catch (DomainException)
        {
            return Result<CommunityAuthorRestrictionResponse>.Failure(
                CommunityModerationErrors.RestrictionAlreadyRevoked);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<CommunityAuthorRestrictionResponse>.Success(
            CreateCommunityAuthorRestrictionCommandHandler.ToResponse(restriction));
    }
}
