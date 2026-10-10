using Sanad.Modules.Community.Application.Abstractions.Data;
using Sanad.Modules.Community.Application.Categories;
using Sanad.Modules.Community.Domain.Categories;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Community.Application.Posts;

public sealed class CreatePostCommandHandler : ICommandHandler<CreatePostCommand, Post>
{
    private readonly ICommunityDbContext _dbContext;

    public CreatePostCommandHandler(ICommunityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<Post>> Handle(CreatePostCommand request, CancellationToken cancellationToken)
    {
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

        var post = Post.Create(
            request.TitleArabic,
            request.TitleEnglish,
            request.ContentArabic,
            request.ContentEnglish,
            request.ImageUrl,
            request.IsAnonymous,
            new UserId(request.AuthorId),
            DateTime.UtcNow,
            categoryId);

        _dbContext.Posts.Add(post);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return post;
    }
}
