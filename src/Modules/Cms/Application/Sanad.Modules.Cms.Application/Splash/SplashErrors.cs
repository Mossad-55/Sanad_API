using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Cms.Application.Splash;

public static class SplashErrors
{
    public static readonly Error InternalNameAlreadyInUse =
        new(
            "Cms.Splash.InternalNameAlreadyInUse",
            "Internal name is already in use.");

    public static readonly Error NotFound =
        new(
            "Cms.Splash.NotFound",
            "Splash screen was not found.");
}