namespace Sanad.Modules.Community.Application.Comments;

using Sanad.BuildingBlocks.Domain.Primitives.Ids;

public sealed record GetCommentsQuery(Guid PostId, int Page, int PageSize) : IQuery<Comment[]>;

/// <summary>
/// The Community post that owns a comment thread, with the post's stored
/// anonymity flag and its author.
/// <para>
/// Community stores anonymity on the post only; a comment and a reply have no
/// anonymity column of their own. A comment or a reply is therefore presented
/// as anonymous exactly when it is written by the author of an anonymous post
/// inside that post's own thread — the one place where the post author's
/// identity would otherwise be exposed through their own words.
/// </para>
/// </summary>
public sealed record CommunityThreadPost(Guid PostId, bool IsAnonymous, Guid AuthorId);

public sealed record GetCommunityThreadPostByPostIdQuery(Guid PostId)
    : IQuery<CommunityThreadPost?>;

public sealed class GetCommunityThreadPostByPostIdQueryHandler(ICommunityDbContext dbContext)
    : IQueryHandler<GetCommunityThreadPostByPostIdQuery, CommunityThreadPost?>
{
    public async Task<Result<CommunityThreadPost?>> Handle(
        GetCommunityThreadPostByPostIdQuery request,
        CancellationToken cancellationToken)
    {
        Post? post = await dbContext.Posts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                post => post.Id == new CommunityPostId(request.PostId),
                cancellationToken);

        return Result<CommunityThreadPost?>.Success(
            post is null
                ? null
                : new CommunityThreadPost(
                    post.Id.Value,
                    post.IsAnonymous,
                    post.AuthorId.Value));
    }
}

public sealed record GetCommunityThreadPostByCommentIdQuery(Guid CommentId)
    : IQuery<CommunityThreadPost?>;

public sealed class GetCommunityThreadPostByCommentIdQueryHandler(ICommunityDbContext dbContext)
    : IQueryHandler<GetCommunityThreadPostByCommentIdQuery, CommunityThreadPost?>
{
    public async Task<Result<CommunityThreadPost?>> Handle(
        GetCommunityThreadPostByCommentIdQuery request,
        CancellationToken cancellationToken)
    {
        Comment? comment = await dbContext.Comments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                comment => comment.Id == new CommunityCommentId(request.CommentId),
                cancellationToken);

        if (comment is null)
        {
            return Result<CommunityThreadPost?>.Success(null);
        }

        Guid postId = comment.PostId.Value;

        Post? post = await dbContext.Posts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                post => post.Id == new CommunityPostId(postId),
                cancellationToken);

        return Result<CommunityThreadPost?>.Success(
            post is null
                ? null
                : new CommunityThreadPost(
                    post.Id.Value,
                    post.IsAnonymous,
                    post.AuthorId.Value));
    }
}
