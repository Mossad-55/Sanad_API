using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.Modules.Community.Domain.Categories;

namespace Ahlena.Modules.Community.Application.Categories;

public sealed record CreateCategoryCommand(string ArabicName, string EnglishName, bool IsActive = true)
    : ICommand<AdminCategoryResponse>;

public sealed class CreateCategoryCommandHandler(ICommunityDbContext dbContext)
    : ICommandHandler<CreateCategoryCommand, AdminCategoryResponse>
{
    public async Task<Result<AdminCategoryResponse>> Handle(
        CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        CommunityCategory category;
        try
        {
            category = CommunityCategory.Create(
                request.ArabicName, request.EnglishName, request.IsActive, DateTime.UtcNow);
        }
        catch (DomainException)
        {
            return Result<AdminCategoryResponse>.Failure(CommunityCategoryErrors.Invalid);
        }

        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<AdminCategoryResponse>.Success(ToAdminResponse(category));
    }

    internal static AdminCategoryResponse ToAdminResponse(CommunityCategory category) => new(
        category.Id.Value, category.ArabicName, category.EnglishName,
        category.IsActive, category.CreatedOnUtc, category.UpdatedOnUtc);
}

public sealed record UpdateCategoryCommand(
    Guid Id,
    string ArabicName,
    string EnglishName,
    bool IsActive) : ICommand<AdminCategoryResponse>;

public sealed class UpdateCategoryCommandHandler(ICommunityDbContext dbContext)
    : ICommandHandler<UpdateCategoryCommand, AdminCategoryResponse>
{
    public async Task<Result<AdminCategoryResponse>> Handle(
        UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await dbContext.Categories
            .FirstOrDefaultAsync(c => c.Id == new CommunityCategoryId(request.Id), cancellationToken);
        if (category is null)
            return Result<AdminCategoryResponse>.Failure(CommunityCategoryErrors.CategoryNotFound);

        try
        {
            category.Update(request.ArabicName, request.EnglishName, request.IsActive, DateTime.UtcNow);
        }
        catch (DomainException)
        {
            return Result<AdminCategoryResponse>.Failure(CommunityCategoryErrors.Invalid);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<AdminCategoryResponse>.Success(
            CreateCategoryCommandHandler.ToAdminResponse(category));
    }
}

public sealed record DeleteCategoryCommand(Guid Id) : ICommand<bool>;

public sealed class DeleteCategoryCommandHandler(ICommunityDbContext dbContext)
    : ICommandHandler<DeleteCategoryCommand, bool>
{
    public async Task<Result<bool>> Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await dbContext.Categories
            .FirstOrDefaultAsync(c => c.Id == new CommunityCategoryId(request.Id), cancellationToken);
        if (category is null)
            return Result<bool>.Failure(CommunityCategoryErrors.CategoryNotFound);

        bool inUse = await dbContext.Posts
            .AnyAsync(p => p.CategoryId == category.Id, cancellationToken);
        if (inUse)
            return Result<bool>.Failure(CommunityCategoryErrors.CategoryInUse);

        dbContext.Categories.Remove(category);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
