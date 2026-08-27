using Sanad.Modules.Cms.Domain.Splash;

namespace Sanad.Modules.Cms.Application.Splash;

internal static class SplashScreenMappings
{
    public static SplashScreenResponse ToResponse(
        this SplashScreen screen)
    {
        return new SplashScreenResponse(
            screen.Id,
            screen.InternalName,
            screen.ArabicTitle,
            screen.EnglishTitle,
            screen.ArabicDescription,
            screen.EnglishDescription,
            screen.ArabicButtonText,
            screen.EnglishButtonText,
            screen.ImagePath,
            screen.BackgroundColor,
            screen.DisplayOrder,
            screen.Status);
    }

    public static SplashScreenPublicItem ToPublicItem(
        this SplashScreen screen)
    {
        return new SplashScreenPublicItem(
            screen.Id,
            screen.ArabicTitle,
            screen.EnglishTitle,
            screen.ArabicDescription,
            screen.EnglishDescription,
            screen.ArabicButtonText,
            screen.EnglishButtonText,
            screen.ImagePath,
            screen.BackgroundColor,
            screen.DisplayOrder);
    }
}