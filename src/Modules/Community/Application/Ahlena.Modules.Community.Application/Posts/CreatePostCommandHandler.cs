using Ahlena.Modules.Community.Application.Abstractions.Data;
using Ahlena.Modules.Community.Application.Categories;
using Ahlena.Modules.Community.Domain.Categories;
using Ahlena.Modules.Community.Domain.Posts;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Community.Application.Posts;

public sealed class CreatePostCommandHandler : ICommandHandler<CreatePostCommand, Post>
{
    private readonly ICommunityDbContext _dbContext;

    public CreatePostCommandHandler(ICommunityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<Post>> Handle(CreatePostCommand request, CancellationToken cancellationToken)
    {
        var authorId = new UserId(request.AuthorId);
        if (await CommunityAuthorRestrictionGuard.IsAuthorRestrictedAsync(
                _dbContext, authorId, DateTime.UtcNow, cancellationToken))
        {
            return Result<Post>.Failure(CommunityModerationErrors.AuthorRestricted);
        }

        CommunityCategoryId? categoryId = null;
        if (request.CategoryId is Guid rawCategoryId)
        {
            var category = await _dbContext.Categories
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    c => c.Id == new CommunityCategoryId(rawCategoryId),
                    cancellationToken);

            if (category is null)
                return Result<Post>.Failure(CommunityCategoryErrors.InvalidCategory);
            if (!category.IsActive)
                return Result<Post>.Failure(CommunityCategoryErrors.CategoryInactive);

            categoryId = category.Id;
        }

        // A new post may reference only an uploaded Community image asset the
        // author is allowed to reference — never an arbitrary external URL or
        // another user's private asset.
        if (await CommunityImageReferenceValidator.ValidateAsync(
                _dbContext,
                currentImageUrl: null,
                request.ImageUrl,
                request.AuthorId,
                cancellationToken) is { IsFailure: true } imageResult)
        {
            return Result<Post>.Failure(imageResult.Error);
        }

        var post = Post.Create(
            request.TitleArabic,
            request.TitleEnglish,
            request.ContentArabic,
            request.ContentEnglish,
            request.ImageUrl,
            request.IsAnonymous,
            authorId,
            DateTime.UtcNow,
            categoryId);

        _dbContext.Posts.Add(post);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return post;
    }
}
