using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Community.Domain.Posts;

/// <summary>
/// One lifetime-unique viewer record for a Community post. The pair
/// (<see cref="PostId"/>, <see cref="ViewerUserId"/>) is unique, so a viewer
/// counts at most once per post for the post's lifetime; the row is written in
/// the same save as the post's <see cref="Post.ViewsCount"/> increment. It is
/// an identity-free counter fact: it never stores or exposes a viewer's name
/// and is never returned by a read.
/// </summary>
public sealed class PostView : Entity<CommunityPostViewId>
{
    private PostView()
    {
    }

    private PostView(
        CommunityPostViewId id,
        CommunityPostId postId,
        UserId viewerUserId,
        DateTime viewedOnUtc)
        : base(id)
    {
        if (postId == CommunityPostId.Empty)
            throw new ArgumentException("A Community post view requires a post.", nameof(postId));
        if (viewerUserId == UserId.Empty)
            throw new ArgumentException("A Community post view requires a viewer.", nameof(viewerUserId));
        if (viewedOnUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Community post view timestamps must be UTC.", nameof(viewedOnUtc));

        PostId = postId;
        ViewerUserId = viewerUserId;
        ViewedOnUtc = viewedOnUtc;
    }

    public CommunityPostId PostId { get; private set; }
    public UserId ViewerUserId { get; private set; }
    public DateTime ViewedOnUtc { get; private set; }

    public static PostView Create(
        CommunityPostId postId,
        UserId viewerUserId,
        DateTime viewedOnUtc) =>
        new(CommunityPostViewId.New(), postId, viewerUserId, viewedOnUtc);
}
