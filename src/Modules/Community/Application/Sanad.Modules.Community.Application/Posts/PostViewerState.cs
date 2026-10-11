using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Interactions;
using Sanad.Modules.Community.Domain.Posts;

namespace Sanad.Modules.Community.Application.Posts;

public sealed record PostFeedItem(Post Post, bool IsLiked, bool IsFavorited, int? CurrentUserRating);

public enum CommunityPostsSort
{
    Newest = 0,
    Oldest = 1,
    MostLiked = 2
}

internal static class PostViewerState
{
    public static async Task<IReadOnlyDictionary<Guid, (bool Liked, bool Favorited, int? Rating)>> LoadAsync(
        ICommunityDbContext dbContext,
        Guid viewerUserId,
        IReadOnlyList<Post> posts,
        CancellationToken cancellationToken)
    {
        if (viewerUserId == Guid.Empty || posts.Count == 0)
            return new Dictionary<Guid, (bool, bool, int?)>();

        List<Guid> postIds = posts.Select(p => p.Id.Value).ToList();
        var user = new UserId(viewerUserId);

        var interactions = await dbContext.Interactions
            .AsNoTracking()
            .Where(i => i.TargetType == CommunityInteractionTarget.Post &&
                        i.UserId == user &&
                        postIds.Contains(i.TargetId))
            .Select(i => new { i.TargetId, i.Kind })
            .ToListAsync(cancellationToken);

        var ratings = await dbContext.Ratings
            .AsNoTracking()
            .Where(r => r.UserId == user && postIds.Contains(r.PostId.Value))
            .Select(r => new { PostId = r.PostId.Value, r.RatingValue })
            .ToListAsync(cancellationToken);

        var liked = interactions
            .Where(i => i.Kind == CommunityInteractionKind.Like)
            .Select(i => i.TargetId)
            .ToHashSet();
        var favorited = interactions
            .Where(i => i.Kind == CommunityInteractionKind.Favorite)
            .Select(i => i.TargetId)
            .ToHashSet();
        var ratingByPost = new Dictionary<Guid, int?>();
        foreach (var rating in ratings)
        {
            ratingByPost[rating.PostId] =
                int.TryParse(rating.RatingValue, out int value) ? value : null;
        }

        var state = new Dictionary<Guid, (bool, bool, int?)>();
        foreach (Guid postId in postIds)
        {
            state[postId] = (liked.Contains(postId), favorited.Contains(postId),
                ratingByPost.TryGetValue(postId, out int? rating) ? rating : null);
        }

        return state;
    }

    public static IReadOnlyList<PostFeedItem> Attach(
        IReadOnlyList<Post> posts,
        IReadOnlyDictionary<Guid, (bool Liked, bool Favorited, int? Rating)> state) =>
        posts
            .Select(p => state.TryGetValue(p.Id.Value, out var s)
                ? new PostFeedItem(p, s.Liked, s.Favorited, s.Rating)
                : new PostFeedItem(p, false, false, null))
            .ToList();
}
