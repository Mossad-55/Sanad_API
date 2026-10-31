using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Cms.Domain.Splash;

namespace Ahlena.Modules.Cms.Application.Splash;

public sealed record SplashScreenResponse(
    SplashScreenId Id,
    string InternalName,
    string ArabicTitle,
    string EnglishTitle,
    string ArabicDescription,
    string EnglishDescription,
    string ArabicButtonText,
    string EnglishButtonText,
    string ImagePath,
    string BackgroundColor,
    int DisplayOrder,
    SplashPublicationStatus Status);