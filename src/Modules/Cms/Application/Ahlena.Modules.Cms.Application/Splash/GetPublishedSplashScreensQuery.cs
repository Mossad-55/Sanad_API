using Ahlena.BuildingBlocks.Application.CQRS;

namespace Ahlena.Modules.Cms.Application.Splash;

public sealed record GetPublishedSplashScreensQuery()
    : IQuery<IReadOnlyList<SplashScreenPublicItem>>;