using Sanad.BuildingBlocks.Application.CQRS;

namespace Sanad.Modules.Cms.Application.Splash;

public sealed record CreateSplashScreenCommand(
    string InternalName,
    string ArabicTitle,
    string EnglishTitle,
    string ArabicDescription,
    string EnglishDescription,
    string ArabicButtonText,
    string EnglishButtonText,
    string ImagePath,
    string BackgroundColor,
    int DisplayOrder)
    : ICommand<SplashScreenResponse>;