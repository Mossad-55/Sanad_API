namespace Sanad.API.Controllers.Requests;

public sealed record UpdateSplashScreenRequest(
    string ArabicTitle,
    string EnglishTitle,
    string ArabicDescription,
    string EnglishDescription,
    string ArabicButtonText,
    string EnglishButtonText,
    string ImagePath,
    string BackgroundColor,
    int DisplayOrder);