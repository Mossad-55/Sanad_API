namespace Sanad.API.Controllers.Requests;

using Sanad.Modules.Cms.Domain.Wellness;

public sealed record CreateWellnessTipRequest(string ArabicTitle, string EnglishTitle, string Category, string SectionsJson);
public sealed record UpdateWellnessTipRequest(string ArabicTitle, string EnglishTitle, string Category, string? SectionsJson);
public sealed record WellnessTipSectionRequest(int DisplayOrder, string ArabicText, string EnglishText);
