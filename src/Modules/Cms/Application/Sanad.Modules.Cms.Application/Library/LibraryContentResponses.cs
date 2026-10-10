using Sanad.Modules.Cms.Domain.Library;

namespace Sanad.Modules.Cms.Application.Library;

public sealed record LibrarySectionResponse(
    int DisplayOrder,
    string ArabicTitle,
    string EnglishTitle,
    IReadOnlyList<string> ArabicBullets,
    IReadOnlyList<string> EnglishBullets);

public sealed record LibraryContentListItemResponse(
    Guid Id,
    string Type,
    string ArabicTitle,
    string EnglishTitle,
    string ArabicSummary,
    string EnglishSummary,
    string CoverImagePath,
    Guid? CategoryId,
    string? CategoryArabicName,
    string? CategoryEnglishName,
    bool IsPremium,
    bool CanAccess,
    bool IsLiked,
    bool IsSaved,
    int ViewCount,
    int LikesCount,
    int? DurationSeconds,
    DateTime CreatedOnUtc,
    DateTime? PublishedOnUtc);

public sealed record LibraryContentDetailResponse(
    Guid Id,
    string Type,
    string ArabicTitle,
    string EnglishTitle,
    string ArabicSummary,
    string EnglishSummary,
    string? VideoUrl,
    int? DurationSeconds,
    string CoverImagePath,
    Guid? CategoryId,
    string? CategoryArabicName,
    string? CategoryEnglishName,
    bool IsPremium,
    bool CanAccess,
    bool IsLiked,
    bool IsSaved,
    int ViewCount,
    int LikesCount,
    DateTime CreatedOnUtc,
    DateTime? PublishedOnUtc,
    IReadOnlyList<LibrarySectionResponse> Sections);

public sealed record LibraryAdminContentResponse(
    Guid Id,
    string Type,
    string Status,
    string ArabicTitle,
    string EnglishTitle,
    string ArabicSummary,
    string EnglishSummary,
    string? VideoUrl,
    int? DurationSeconds,
    string CoverImagePath,
    Guid? CategoryId,
    string? CategoryArabicName,
    string? CategoryEnglishName,
    bool IsPremium,
    int ViewCount,
    int LikesCount,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc,
    DateTime? PublishedOnUtc,
    IReadOnlyList<LibrarySectionResponse> Sections);

public sealed record LibraryAdminPageResponse(
    IReadOnlyList<LibraryAdminContentResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

internal static class LibraryContentMappings
{
    public static LibrarySectionResponse ToSectionResponse(this LibraryContentSection section) =>
        new(
            section.DisplayOrder,
            section.ArabicTitle,
            section.EnglishTitle,
            section.ArabicBullets.ToList(),
            section.EnglishBullets.ToList());

    public static LibraryContentListItemResponse ToListItem(
        this LibraryContent content,
        bool isLiked,
        bool isSaved,
        bool canAccess) => new(
        content.Id.Value,
        content.Type.ToString(),
        content.ArabicTitle,
        content.EnglishTitle,
        content.ArabicSummary,
        content.EnglishSummary,
        content.CoverImagePath,
        content.CategoryId?.Value,
        content.Category?.ArabicName,
        content.Category?.EnglishName,
        content.IsPremium,
        canAccess,
        isLiked,
        isSaved,
        content.ViewCount,
        content.LikesCount,
        content.DurationSeconds,
        content.CreatedOnUtc,
        content.PublishedOnUtc);

    public static LibraryContentDetailResponse ToDetail(
        this LibraryContent content,
        bool isLiked,
        bool isSaved,
        bool canAccess)
    {
        IReadOnlyList<LibrarySectionResponse> sections = canAccess
            ? content.Sections
                .OrderBy(section => section.DisplayOrder)
                .Select(section => section.ToSectionResponse())
                .ToList()
            : [];
        string? videoUrl = canAccess ? content.VideoUrl : null;

        return new LibraryContentDetailResponse(
            content.Id.Value,
            content.Type.ToString(),
            content.ArabicTitle,
            content.EnglishTitle,
            content.ArabicSummary,
            content.EnglishSummary,
            videoUrl,
            content.DurationSeconds,
            content.CoverImagePath,
            content.CategoryId?.Value,
            content.Category?.ArabicName,
            content.Category?.EnglishName,
            content.IsPremium,
            canAccess,
            isLiked,
            isSaved,
            content.ViewCount,
            content.LikesCount,
            content.CreatedOnUtc,
            content.PublishedOnUtc,
            sections);
    }

    public static LibraryAdminContentResponse ToAdminResponse(this LibraryContent content) =>
        new(
            content.Id.Value,
            content.Type.ToString(),
            content.Status.ToString(),
            content.ArabicTitle,
            content.EnglishTitle,
            content.ArabicSummary,
            content.EnglishSummary,
            content.VideoUrl,
            content.DurationSeconds,
            content.CoverImagePath,
            content.CategoryId?.Value,
            content.Category?.ArabicName,
            content.Category?.EnglishName,
            content.IsPremium,
            content.ViewCount,
            content.LikesCount,
            content.CreatedOnUtc,
            content.UpdatedOnUtc,
            content.PublishedOnUtc,
            content.Sections
                .OrderBy(section => section.DisplayOrder)
                .Select(section => section.ToSectionResponse())
                .ToList());
}
