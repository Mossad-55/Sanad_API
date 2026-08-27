namespace Sanad.API.Controllers.Requests;

public sealed record CreateSplashScreenRequest(
    string InternalName,
    string ArabicTitle,
    string EnglishTitle,
    string ArabicDescription,
    string EnglishDescription,
    string ArabicButtonText,
    string EnglishButtonText,
    string BackgroundColor,
    int DisplayOrder);