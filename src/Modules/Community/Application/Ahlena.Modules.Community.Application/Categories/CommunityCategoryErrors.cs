using Ahlena.BuildingBlocks.Application.Results;

namespace Ahlena.Modules.Community.Application.Categories;

public static class CommunityCategoryErrors
{
    public static readonly Error Invalid =
        new("Community.Category.Invalid", "Category names are required and must not exceed 200 characters.");

    public static readonly Error CategoryNotFound =
        new("Community.CategoryNotFound", "Community category not found.");

    public static readonly Error CategoryInactive =
        new("Community.CategoryInactive", "The selected community category is inactive.");

    public static readonly Error InvalidCategory =
        new("Community.InvalidCategory", "The selected community category does not exist.");

    public static readonly Error CategoryInUse =
        new("Community.CategoryInUse", "The category is referenced by community posts and cannot be deleted.");
}
