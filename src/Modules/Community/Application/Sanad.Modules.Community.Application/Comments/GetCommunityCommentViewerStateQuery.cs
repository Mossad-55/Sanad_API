using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Interactions;

namespace Sanad.Modules.Community.Application.Comments;

/// <summary>
/// One page's persisted per-viewer state for the comment thread: the caller's
/// likes and the comments that already have at least one reply. It carries no
/// author identity — the caller is resolved from the JWT and the reply
/// existence is a structural fact used only to mirror the delete rule.
/// </summary>
public sealed record CommunityCommentViewerState(
    IReadOnlySet<Guid> LikedCommentIds,
    IReadOnlySet<Guid> LikedReplyIds,
    IReadOnlySet<Guid> CommentIdsWithReplies)
{
    public static CommunityCommentViewerState Empty { get; } =
        new(new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
}

/// <summary>
/// The batched viewer state of one comments page (comments and replies at
/// once): two bounded set queries per page, never one per row.
/// </summary>
public sealed record GetCommunityCommentViewerStateQuery(
    IReadOnlyCollection<Guid> CommentIds,
    IReadOnlyCollection<Guid> ReplyIds,
    Guid ViewerUserId) : IQuery<CommunityCommentViewerState>;

public sealed class GetCommunityCommentViewerStateQueryHandler(ICommunityDbContext dbContext)
    : IQueryHandler<GetCommunityCommentViewerStateQuery, CommunityCommentViewerState>
{
    public async Task<Result<CommunityCommentViewerState>> Handle(
        GetCommunityCommentViewerStateQuery request,
        CancellationToken cancellationToken)
    {
        Guid[] commentIds = (request.CommentIds ?? []).ToArray();
        Guid[] replyIds = (request.ReplyIds ?? []).ToArray();

        if (request.ViewerUserId == Guid.Empty || commentIds.Length + replyIds.Length == 0)
        {
            return Result<CommunityCommentViewerState>.Success(
                CommunityCommentViewerState.Empty);
        }

        var viewerUserId = new UserId(request.ViewerUserId);
        Guid[] targetIds = commentIds.Concat(replyIds).Distinct().ToArray();

        // The caller's persisted likes on this page. Comment and reply likes
        // share the Comment target type; the ids tell them apart.
        List<Guid> likedTargetIds = await dbContext.Interactions
            .AsNoTracking()
            .Where(interaction =>
                interaction.TargetType == CommunityInteractionTarget.Comment &&
                interaction.Kind == CommunityInteractionKind.Like &&
                interaction.UserId == viewerUserId &&
                targetIds.Contains(interaction.TargetId))
            .Select(interaction => interaction.TargetId)
            .ToListAsync(cancellationToken);

        var liked = likedTargetIds.ToHashSet();
        var commentIdSet = commentIds.ToHashSet();
        var replyIdSet = replyIds.ToHashSet();

        HashSet<Guid> likedCommentIds = liked
            .Where(commentIdSet.Contains)
            .ToHashSet();
        HashSet<Guid> likedReplyIds = liked
            .Where(replyIdSet.Contains)
            .ToHashSet();

        // A comment with replies cannot be deleted by the public route (the
        // delete handler answers CommentHasReplies). One bounded query for the
        // page, so canDelete on every item mirrors the real mutation rule.
        List<CommunityCommentId> typedCommentIds =
            commentIds.Select(id => new CommunityCommentId(id)).ToList();
        List<CommunityCommentId> commentIdsWithReplies = await dbContext.Replies
            .AsNoTracking()
            .Where(reply => typedCommentIds.Contains(reply.CommentId))
            .Select(reply => reply.CommentId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return Result<CommunityCommentViewerState>.Success(
            new CommunityCommentViewerState(
                likedCommentIds,
                likedReplyIds,
                commentIdsWithReplies.Select(id => id.Value).ToHashSet()));
    }
}
