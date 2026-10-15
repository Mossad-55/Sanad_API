using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Cms.Application.Library;

public static class LibraryErrors
{
    public static readonly Error CategoryNotFound =
        new("Cms.LibraryCategory.NotFound", "Library category not found.");

    public static readonly Error CategoryInvalid =
        new("Cms.LibraryCategory.Invalid", "Category names are required and must not exceed 200 characters.");

    public static readonly Error CategoryInUse =
        new("Cms.LibraryCategory.InUse", "The category is referenced by library content and cannot be deleted.");

    public static readonly Error ContentNotFound =
        new("Cms.LibraryContent.NotFound", "Library content was not found.");

    public static readonly Error ContentNotPublished =
        new("Cms.LibraryContent.NotPublished", "The requested library content is not published.");

    public static readonly Error ContentInvalid =
        new("Cms.LibraryContent.Invalid", "Library content failed validation.");

    public static readonly Error ContentInvalidOperation =
        new("Cms.LibraryContent.InvalidOperation", "The requested library content operation is invalid.");

    public static readonly Error InvalidCategory =
        new("Cms.Library.InvalidCategory", "The selected library category does not exist.");

    public static readonly Error CategoryInactive =
        new("Cms.Library.CategoryInactive", "The selected library category is inactive.");

    public static readonly Error InvalidType =
        new("Cms.Library.InvalidType", "The library content type is invalid.");

    public static readonly Error InvalidSections =
        new("Cms.Library.InvalidSections", "The sections content is invalid.");
}
