using Sanad.Modules.Families.Application.Assessments;

namespace Sanad.API.Controllers.Requests;

public sealed record CreateAssessmentQuestionRequest(
    int Order,
    string ArabicText,
    string EnglishText,
    bool IsRequired,
    bool IsActive,
    IReadOnlyList<AdminOptionInput> Options);

public sealed record UpdateAssessmentQuestionRequest(
    int Order,
    string ArabicText,
    string EnglishText,
    bool IsRequired,
    IReadOnlyList<AdminOptionInput> Options);

public sealed record CreateAssessmentTierRequest(
    int ScreenOrder,
    string ArabicTitle,
    string EnglishTitle,
    string ArabicSubtitle,
    string EnglishSubtitle,
    string BackgroundColor,
    string ArabicButtonText,
    string EnglishButtonText,
    int MinScore,
    int MaxScore,
    IReadOnlyList<string> ArabicRecommendations,
    IReadOnlyList<string> EnglishRecommendations,
    bool IsActive);

public sealed record UpdateAssessmentTierRequest(
    int ScreenOrder,
    string ArabicTitle,
    string EnglishTitle,
    string ArabicSubtitle,
    string EnglishSubtitle,
    string BackgroundColor,
    string ArabicButtonText,
    string EnglishButtonText,
    int MinScore,
    int MaxScore,
    IReadOnlyList<string> ArabicRecommendations,
    IReadOnlyList<string> EnglishRecommendations);