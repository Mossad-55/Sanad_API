using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Posts;

namespace Sanad.Modules.Community.Application.Uploads;

public sealed record CommunityImageFileResponse(
    string ContentType,
    Stream Content);

public sealed record GetCommunityImageFileQuery(
    Guid ImageId,
    UserId ActorUserId,
    bool IsModerator) : IQuery<CommunityImageFileResponse>;

public static class CommunityImageFileErrors
{
    public static readonly Error NotFound =
        new(
            "Community.Image.NotFound",
            "Community image was not found.");
}

public sealed class GetCommunityImageFileQueryHandler : IQueryHandler<GetCommunityImageFileQuery, CommunityImageFileResponse>
{
    private readonly ICommunityDbContext _dbContext;
    private readonly IFileStorage _storage;

    public GetCommunityImageFileQueryHandler(
        ICommunityDbContext dbContext,
        IFileStorage storage)
    {
        _dbContext = dbContext;
        _storage = storage;
    }

    public async Task<Result<CommunityImageFileResponse>> Handle(
        GetCommunityImageFileQuery request,
        CancellationToken cancellationToken)
    {
        var record = await _dbContext.CommunityImages.FindAsync(
            [new CommunityImageId(request.ImageId)], cancellationToken);

        if (record is null)
        {
            return Result<CommunityImageFileResponse>.Failure(
                CommunityImageFileErrors.NotFound);
        }

        if (!request.IsModerator && record.UploadedBy != request.ActorUserId)
        {
            string attachedUrl = CommunityImageUrls.For(record.Id);
            bool published = await _dbContext.Posts.AsNoTracking().AnyAsync(
                post => post.Status == PostStatus.Published && post.ImageUrl == attachedUrl,
                cancellationToken);

            if (!published)
            {
                return Result<CommunityImageFileResponse>.Failure(
                    CommunityImageFileErrors.NotFound);
            }
        }

        var opened = await _storage.OpenReadAsync(record.StorageKey, cancellationToken);
        if (opened.IsFailure)
        {
            return Result<CommunityImageFileResponse>.Failure(
                CommunityImageFileErrors.NotFound);
        }

        return Result<CommunityImageFileResponse>.Success(
            new CommunityImageFileResponse(
                opened.Value.ContentType,
                opened.Value.Content));
    }
}
