using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Library;

namespace Sanad.Modules.Cms.Application.Library;

public sealed record LibraryCategoryResponse(Guid Id, string ArabicName, string EnglishName);

public sealed record AdminLibraryCategoryResponse(
    Guid Id,
    string ArabicName,
    string EnglishName,
    bool IsActive,
    DateTime CreatedOnUtc,
    DateTime? UpdatedOnUtc);

public sealed record CreateLibraryCategoryCommand(string ArabicName, string EnglishName, bool IsActive = true)
    : ICommand<AdminLibraryCategoryResponse>;

public sealed class CreateLibraryCategoryCommandHandler(ICmsDbContext dbContext)
    : ICommandHandler<CreateLibraryCategoryCommand, AdminLibraryCategoryResponse>
{
    public async Task<Result<AdminLibraryCategoryResponse>> Handle(
        CreateLibraryCategoryCommand request, CancellationToken cancellationToken)
    {
        LibraryCategory category;
        try
        {
            category = LibraryCategory.Create(
                request.ArabicName, request.EnglishName, request.IsActive, DateTime.UtcNow);
        }
        catch (DomainException)
        {
            return LibraryErrors.CategoryInvalid;
        }

        dbContext.LibraryCategories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToAdminResponse(category);
    }

    internal static AdminLibraryCategoryResponse ToAdminResponse(LibraryCategory category) => new(
        category.Id.Value, category.ArabicName, category.EnglishName,
        category.IsActive, category.CreatedOnUtc, category.UpdatedOnUtc);
}

public sealed record UpdateLibraryCategoryCommand(
    Guid Id,
    string ArabicName,
    string EnglishName,
    bool IsActive) : ICommand<AdminLibraryCategoryResponse>;

public sealed class UpdateLibraryCategoryCommandHandler(ICmsDbContext dbContext)
    : ICommandHandler<UpdateLibraryCategoryCommand, AdminLibraryCategoryResponse>
{
    public async Task<Result<AdminLibraryCategoryResponse>> Handle(
        UpdateLibraryCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await dbContext.LibraryCategories
            .FirstOrDefaultAsync(
                item => item.Id == new LibraryCategoryId(request.Id), cancellationToken);
        if (category is null)
            return LibraryErrors.CategoryNotFound;

        try
        {
            category.Update(
                request.ArabicName, request.EnglishName, request.IsActive, DateTime.UtcNow);
        }
        catch (DomainException)
        {
            return LibraryErrors.CategoryInvalid;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return CreateLibraryCategoryCommandHandler.ToAdminResponse(category);
    }
}

public sealed record DeleteLibraryCategoryCommand(Guid Id) : ICommand<bool>;

public sealed class DeleteLibraryCategoryCommandHandler(ICmsDbContext dbContext)
    : ICommandHandler<DeleteLibraryCategoryCommand, bool>
{
    public async Task<Result<bool>> Handle(
        DeleteLibraryCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await dbContext.LibraryCategories
            .FirstOrDefaultAsync(
                item => item.Id == new LibraryCategoryId(request.Id), cancellationToken);
        if (category is null)
            return LibraryErrors.CategoryNotFound;

        bool inUse = await dbContext.LibraryContents
            .AnyAsync(content => content.CategoryId == category.Id, cancellationToken);
        if (inUse)
            return LibraryErrors.CategoryInUse;

        dbContext.LibraryCategories.Remove(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }
}

public sealed record GetLibraryCategoriesQuery
    : IQuery<IReadOnlyList<LibraryCategoryResponse>>;

public sealed class GetLibraryCategoriesQueryHandler(ICmsDbContext dbContext)
    : IQueryHandler<GetLibraryCategoriesQuery, IReadOnlyList<LibraryCategoryResponse>>
{
    public async Task<Result<IReadOnlyList<LibraryCategoryResponse>>> Handle(
        GetLibraryCategoriesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<LibraryCategoryResponse> categories = await dbContext.LibraryCategories
            .AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.CreatedOnUtc)
            .ThenBy(category => category.Id.Value)
            .Select(category => new LibraryCategoryResponse(
                category.Id.Value, category.ArabicName, category.EnglishName))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<LibraryCategoryResponse>>.Success(categories);
    }
}

public sealed record GetLibraryAdminCategoriesQuery
    : IQuery<IReadOnlyList<AdminLibraryCategoryResponse>>;

public sealed class GetLibraryAdminCategoriesQueryHandler(ICmsDbContext dbContext)
    : IQueryHandler<GetLibraryAdminCategoriesQuery, IReadOnlyList<AdminLibraryCategoryResponse>>
{
    public async Task<Result<IReadOnlyList<AdminLibraryCategoryResponse>>> Handle(
        GetLibraryAdminCategoriesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<AdminLibraryCategoryResponse> categories = await dbContext.LibraryCategories
            .AsNoTracking()
            .OrderBy(category => category.CreatedOnUtc)
            .ThenBy(category => category.Id.Value)
            .Select(category => new AdminLibraryCategoryResponse(
                category.Id.Value, category.ArabicName, category.EnglishName,
                category.IsActive, category.CreatedOnUtc, category.UpdatedOnUtc))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<AdminLibraryCategoryResponse>>.Success(categories);
    }
}

internal static class LibraryCategoryLookup
{
    public static async Task<(Error? Error, LibraryCategoryId? CategoryId)> ValidateAsync(
        ICmsDbContext dbContext,
        Guid? categoryId,
        CancellationToken cancellationToken)
    {
        if (categoryId is not Guid rawCategoryId)
            return (null, null);

        var category = await dbContext.LibraryCategories
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == new LibraryCategoryId(rawCategoryId), cancellationToken);

        if (category is null)
            return (LibraryErrors.InvalidCategory, null);
        if (!category.IsActive)
            return (LibraryErrors.CategoryInactive, null);

        return (null, category.Id);
    }
}
