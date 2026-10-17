using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Application.Abstractions.Data;
using Sanad.Modules.Community.Domain.Posts;

namespace Sanad.Modules.Community.Application.Posts;

public sealed record PublishCommunityPostCommand(Guid PostId, UserId ModeratorId) : ICommand<bool>;

public sealed record RejectCommunityPostCommand(
    Guid PostId,
    UserId ModeratorId,
    string? ReasonCode,
    string? Note) : ICommand<bool>;

/// <summary>
/// FUR-07 moderation failure codes. The controller forces the 404/409 status
/// for the not-found and invalid-state cases; the invalid-reason code is the
/// caller's own malformed input and lands on the shared mapper's 400 default.
/// </summary>
public static class CommunityModerationErrors
{
    public static readonly Error InvalidRejectionReason =
        new("Community.InvalidRejectionReason",
            "The rejection reason code is invalid or a required note is missing.");

    public static readonly Error InvalidPostStatus =
        new("Community.InvalidPostStatus", "The post status filter is invalid.");

    public static readonly Error InvalidReportStatus =
        new("Community.InvalidReportStatus", "The report status filter is invalid.");

    public static readonly Error ReportNotFound =
        new("Community.ReportNotFound", "Community report not found.");

    public static readonly Error ReportAlreadyResolved =
        new("Community.ReportAlreadyResolved",
            "The Community report is already resolved.");

    public static readonly Error InvalidReportReason =
        new("Community.InvalidReportReason",
            "The report reason code is invalid or a required note is missing.");

    public static readonly Error CannotReportOwnPost =
        new("Community.CannotReportOwnPost",
            "An author cannot report their own post.");

    public static readonly Error ReportNotAllowed =
        new("Community.ReportNotAllowed",
            "Only published posts can be reported.");

    public static readonly Error InvalidReportResolution =
        new("Community.InvalidReportResolution",
            "The report resolution must be ActionTaken or Dismissed.");

    public static readonly Error InvalidReportResolutionNote =
        new("Community.InvalidReportResolutionNote",
            "The report resolution note must not exceed 1000 characters.");

    public static readonly Error InvalidRestrictionDuration =
        new("Community.InvalidRestrictionDuration",
            "The restriction duration must be Hours24, Days7, or Days30.");

    public static readonly Error InvalidRestrictionReason =
        new("Community.InvalidRestrictionReason",
            "The restriction reason code is invalid or a required note is missing.");

    public static readonly Error RestrictionNotFound =
        new("Community.RestrictionNotFound", "Community restriction not found.");

    public static readonly Error RestrictionAlreadyActive =
        new("Community.RestrictionAlreadyActive",
            "The author already has an active Community restriction.");

    public static readonly Error RestrictionAlreadyRevoked =
        new("Community.RestrictionAlreadyRevoked",
            "The Community restriction is already revoked.");

    public static readonly Error InvalidRevocationReason =
        new("Community.InvalidRevocationReason",
            "A revocation reason between 1 and 1000 characters is required.");

    public static readonly Error AuthorRestricted =
        new("Community.AuthorRestricted",
            "This account is restricted from creating Community content.");
}

public sealed class PublishCommunityPostCommandHandler(ICommunityDbContext dbContext)
    : ICommandHandler<PublishCommunityPostCommand, bool>
{
    public async Task<Result<bool>> Handle(PublishCommunityPostCommand request, CancellationToken cancellationToken)
    {
        var post = await dbContext.Posts.FindAsync([new CommunityPostId(request.PostId)], cancellationToken);
        if (post is null)
            return Result<bool>.Failure(new Error("Community.PostNotFound", "Community post not found."));

        try { post.Publish(request.ModeratorId, DateTime.UtcNow); }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException exception)
        {
            return Result<bool>.Failure(new Error("Community.InvalidModerationOperation", exception.Message));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}

public sealed class RejectCommunityPostCommandHandler(ICommunityDbContext dbContext)
    : ICommandHandler<RejectCommunityPostCommand, bool>
{
    public async Task<Result<bool>> Handle(RejectCommunityPostCommand request, CancellationToken cancellationToken)
    {
        var post = await dbContext.Posts.FindAsync([new CommunityPostId(request.PostId)], cancellationToken);
        if (post is null)
            return Result<bool>.Failure(new Error("Community.PostNotFound", "Community post not found."));

        if (!CommunityRejectionReasonCodes.IsValid(request.ReasonCode))
            return Result<bool>.Failure(CommunityModerationErrors.InvalidRejectionReason);

        string? normalizedNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if ((CommunityRejectionReasonCodes.RequiresNote(request.ReasonCode) && normalizedNote is null) ||
            normalizedNote?.Length > Post.MaximumNoteLength)
            return Result<bool>.Failure(CommunityModerationErrors.InvalidRejectionReason);

        try { post.Reject(request.ModeratorId, request.ReasonCode, normalizedNote, DateTime.UtcNow); }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException exception)
        {
            return Result<bool>.Failure(new Error("Community.InvalidModerationOperation", exception.Message));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
