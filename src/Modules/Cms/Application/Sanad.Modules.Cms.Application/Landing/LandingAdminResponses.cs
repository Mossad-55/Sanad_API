namespace Sanad.Modules.Cms.Application.Landing;

public sealed record LandingAdminSectionResponse(
    Guid Id,
    string Type,
    string Status,
    string ArabicTitle,
    string EnglishTitle,
    string? ArabicBody,
    string? EnglishBody,
    string? ContentJson,
    string? ImagePath,
    int DisplayOrder,
    bool IsVisible,
    Guid? SourceId,
    int DanglingRefCount,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc,
    DateTime? PublishedOnUtc);

public sealed record LandingAdminPageResponse(
    IReadOnlyList<LandingAdminSectionResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);
