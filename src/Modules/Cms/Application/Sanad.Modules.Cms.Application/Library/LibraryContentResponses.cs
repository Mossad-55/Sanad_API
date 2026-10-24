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
    string? VideoStorageKey,
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

/// <summary>
/// The single authorized app playback address of an uploaded library video.
/// The stored bytes are private, so the consumer never receives the storage key;
/// it follows this route, which re-checks publication, the caller's identity and
/// the premium entitlement before streaming the file (mirrors the Community
/// upload-URL precedent).
/// </summary>
public static class LibraryVideoUrls
{
    private const string Prefix = "/api/v1/library/content/";
    private const string Suffix = "/video";

    public static string For(Guid contentId) =>
        Prefix + contentId + Suffix;
}

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

        // Locked decision D6: an externally linked video URL is never a playback
        // source any more. The public field now carries the generated relative
        // address of the stored bytes, and stays null while no approved bytes
        // exist or while the caller is not entitled.
        string? videoUrl = canAccess && content.HasStoredVideo
            ? LibraryVideoUrls.For(content.Id.Value)
            : null;

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
            content.VideoStorageKey,
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
