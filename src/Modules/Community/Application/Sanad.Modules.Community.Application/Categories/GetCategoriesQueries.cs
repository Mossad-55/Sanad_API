using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Community.Domain.Categories;

namespace Sanad.Modules.Community.Application.Categories;

public sealed record CategoryResponse(Guid Id, string ArabicName, string EnglishName);

public sealed record AdminCategoryResponse(
    Guid Id,
    string ArabicName,
    string EnglishName,
    bool IsActive,
    DateTime CreatedOnUtc,
    DateTime? UpdatedOnUtc);

public sealed record GetActiveCategoriesQuery : IQuery<IReadOnlyList<CategoryResponse>>;

public sealed class GetActiveCategoriesQueryHandler(ICommunityDbContext dbContext)
    : IQueryHandler<GetActiveCategoriesQuery, IReadOnlyList<CategoryResponse>>
{
    public async Task<Result<IReadOnlyList<CategoryResponse>>> Handle(
        GetActiveCategoriesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<CategoryResponse> categories = await dbContext.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.CreatedOnUtc)
            .ThenBy(c => c.Id)
            .Select(c => new CategoryResponse(c.Id.Value, c.ArabicName, c.EnglishName))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<CategoryResponse>>.Success(categories);
    }
}

public sealed record GetCommunityCategoriesAdminQuery : IQuery<IReadOnlyList<AdminCategoryResponse>>;

public sealed class GetCommunityCategoriesAdminQueryHandler(ICommunityDbContext dbContext)
    : IQueryHandler<GetCommunityCategoriesAdminQuery, IReadOnlyList<AdminCategoryResponse>>
{
    public async Task<Result<IReadOnlyList<AdminCategoryResponse>>> Handle(
        GetCommunityCategoriesAdminQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<AdminCategoryResponse> categories = await dbContext.Categories
            .AsNoTracking()
            .OrderBy(c => c.CreatedOnUtc)
            .ThenBy(c => c.Id)
            .Select(c => new AdminCategoryResponse(
                c.Id.Value, c.ArabicName, c.EnglishName, c.IsActive, c.CreatedOnUtc, c.UpdatedOnUtc))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<AdminCategoryResponse>>.Success(categories);
    }
}
