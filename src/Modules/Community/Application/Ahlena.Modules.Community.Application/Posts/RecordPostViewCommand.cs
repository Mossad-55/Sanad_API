using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Community.Domain.Posts;

namespace Ahlena.Modules.Community.Application.Posts;

/// <summary>
/// Records one post view for the authenticated caller and returns the current
/// counter. The authorization/visibility rule is the post-detail read rule:
/// the post must be <see cref="PostStatus.Published"/> and not written by an
/// author the caller blocked. An invisible post answers the same privacy
/// not-found as an unknown one (<c>null</c> → 404).
/// <para>
/// The post's own author is excluded from the counter, and every other viewer
/// counts at most once per post for the post's lifetime (the unique
/// post+viewer record). A repeat call returns the current count without a
/// second increment. The viewer row and the counter increment are saved in one
/// unit of work, so one never exists without the other.
/// </para>
/// </summary>
public sealed record RecordPostViewCommand(Guid PostId, Guid ViewerUserId)
    : ICommand<CommunityPostViewResult?>;

public sealed record CommunityPostViewResult(Guid PostId, int ViewsCount);

public sealed class RecordPostViewCommandHandler(ICommunityDbContext dbContext)
    : ICommandHandler<RecordPostViewCommand, CommunityPostViewResult?>
{
    public async Task<Result<CommunityPostViewResult?>> Handle(
        RecordPostViewCommand request,
        CancellationToken cancellationToken)
    {
        if (request.ViewerUserId == Guid.Empty)
        {
            return Result<CommunityPostViewResult?>.Success(null);
        }

        var viewerUserId = new UserId(request.ViewerUserId);
        var postId = new CommunityPostId(request.PostId);

        // Same visibility predicate as GetPostQueryHandler: Published posts
        // only, minus the authors the caller blocked. AsNoTracking is NOT used
        // here because a first view must increment the tracked aggregate.
        Post? post = await dbContext.Posts
            .Where(p =>
                p.Id == postId &&
                p.Status == PostStatus.Published)
            .Where(p => !dbContext.CommunityAuthorBlocks.Any(
                block =>
                    block.BlockerUserId == viewerUserId &&
                    block.BlockedAuthorUserId == p.AuthorId))
            .SingleOrDefaultAsync(cancellationToken);

        if (post is null)
        {
            return Result<CommunityPostViewResult?>.Success(null);
        }

        // The author's own read never counts. Anonymous posts are no
        // exception: the stored author id is compared server-side and is never
        // serialized.
        if (post.AuthorId == viewerUserId)
        {
            return Result<CommunityPostViewResult?>.Success(
                new CommunityPostViewResult(post.Id.Value, post.ViewsCount));
        }

        bool alreadyViewed = await dbContext.PostViews
            .AsNoTracking()
            .AnyAsync(
                view => view.PostId == postId && view.ViewerUserId == viewerUserId,
                cancellationToken);

        if (alreadyViewed)
        {
            return Result<CommunityPostViewResult?>.Success(
                new CommunityPostViewResult(post.Id.Value, post.ViewsCount));
        }

        dbContext.PostViews.Add(PostView.Create(postId, viewerUserId, DateTime.UtcNow));
        post.IncrementViews();

        try
        {
            // One SaveChanges: the dedup row and the counter increment commit
            // together (relational providers wrap the batch in a transaction).
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two simultaneous first views by the same caller: the unique
            // post+viewer index rejects the loser. If the winner's row is now
            // stored, this call simply reports the current count; any other
            // persistence failure still surfaces.
            bool storedByConcurrentRequest = await dbContext.PostViews
                .AsNoTracking()
                .AnyAsync(
                    view => view.PostId == postId && view.ViewerUserId == viewerUserId,
                    cancellationToken);

            if (!storedByConcurrentRequest)
            {
                throw;
            }

            int currentCount = await dbContext.Posts
                .AsNoTracking()
                .Where(p => p.Id == postId)
                .Select(p => p.ViewsCount)
                .SingleAsync(cancellationToken);

            return Result<CommunityPostViewResult?>.Success(
                new CommunityPostViewResult(post.Id.Value, currentCount));
        }

        return Result<CommunityPostViewResult?>.Success(
            new CommunityPostViewResult(post.Id.Value, post.ViewsCount));
    }
}
