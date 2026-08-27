using Sanad.BuildingBlocks.Application.CQRS;

namespace Sanad.Modules.Cms.Application.Splash;

public sealed record GetPublishedSplashScreensQuery()
    : IQuery<IReadOnlyList<SplashScreenPublicItem>>;