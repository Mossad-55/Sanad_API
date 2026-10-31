using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions.Storage;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Community.Application.Abstractions.Data;
using Ahlena.Modules.Community.Domain.Posts;

namespace Ahlena.Modules.Community.Application.Posts;

public sealed record CommunityAuthorPhotoResponse(
    string ContentType,
    Stream Content);

/// <summary>
/// Authorized download of a Community author's avatar (the S3 approved photo
/// route, refund decision lock #32), for any Normal JWT.
/// <para>
/// The author must be a real, non-anonymous Community author: the identity is
/// resolved through the same stored author id the post/comment reads already
/// expose, and an author who writes under anonymity is served nothing. The
/// anonymity decision is resolved here, server-side, from the stored
/// <c>is_anonymous</c> flag of the author's published posts — so the route
/// never depends on the caller passing (or withholding) an anonymity flag.
/// </para>
/// <para>
/// The response is the image bytes only. The stored Identity avatar key is a
/// private storage reference and is never returned on the wire. A missing
/// author, an author with no avatar, an author whose only posts are anonymous
/// and an unresolvable avatar file all answer the same generic 404.
/// </para>
/// </summary>
public sealed record GetCommunityAuthorPhotoQuery(
    UserId AuthorId) : IQuery<CommunityAuthorPhotoResponse>;

public static class CommunityAuthorPhotoErrors
{
    public static readonly Error NotFound =
        new(
            "Community.Author.Photo.NotFound",
            "Community author photo was not found.");
}

public sealed class GetCommunityAuthorPhotoQueryHandler : IQueryHandler<GetCommunityAuthorPhotoQuery, CommunityAuthorPhotoResponse>
{
    private readonly ICommunityDbContext _dbContext;
    private readonly IFileStorage _fileStorage;

    public GetCommunityAuthorPhotoQueryHandler(
        ICommunityDbContext dbContext,
        IFileStorage fileStorage)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
    }

    public async Task<Result<CommunityAuthorPhotoResponse>> Handle(
        GetCommunityAuthorPhotoQuery request,
        CancellationToken cancellationToken)
    {
        // Server-side resolution, one bounded query per rule; a caller-supplied
        // hint is never accepted, so the decision can never be weakened by the
        // request. Two gates:
        // 1. Real Community authorship — at least one Published post. An
        //    arbitrary Identity user with no Community activity is served
        //    nothing, exactly like the caregiver route serves nothing to a
        //    caregiver their discovery predicate would never return.
        // 2. Anonymity — an author who has ever written anonymously (any post
        //    state, conservatively) is never served an avatar that would
        //    identify them.
        bool hasPublishedPost = await _dbContext.Posts
            .AsNoTracking()
            .AnyAsync(
                post => post.AuthorId == request.AuthorId
                    && post.Status == PostStatus.Published
                    && !post.IsAnonymous,
                cancellationToken);

        if (!hasPublishedPost)
        {
            return Result<CommunityAuthorPhotoResponse>.Failure(CommunityAuthorPhotoErrors.NotFound);
        }

        bool authoredAnonymously = await _dbContext.Posts
            .AsNoTracking()
            .AnyAsync(
                post => post.AuthorId == request.AuthorId && post.IsAnonymous,
                cancellationToken);

        if (authoredAnonymously)
        {
            return Result<CommunityAuthorPhotoResponse>.Failure(CommunityAuthorPhotoErrors.NotFound);
        }

        IReadOnlyDictionary<Guid, CommunityAuthorName> names =
            await _dbContext.GetCommunityAuthorNamesAsync(
                new[] { request.AuthorId.Value },
                cancellationToken);

        if (!names.TryGetValue(request.AuthorId.Value, out CommunityAuthorName? author) ||
            string.IsNullOrWhiteSpace(author.AvatarKey))
        {
            return Result<CommunityAuthorPhotoResponse>.Failure(CommunityAuthorPhotoErrors.NotFound);
        }

        Result<PrivateFileContent> file = await _fileStorage.OpenReadAsync(
            author.AvatarKey,
            cancellationToken);

        if (file.IsFailure)
        {
            return Result<CommunityAuthorPhotoResponse>.Failure(CommunityAuthorPhotoErrors.NotFound);
        }

        return Result<CommunityAuthorPhotoResponse>.Success(
            new CommunityAuthorPhotoResponse(file.Value.ContentType, file.Value.Content));
    }
}
