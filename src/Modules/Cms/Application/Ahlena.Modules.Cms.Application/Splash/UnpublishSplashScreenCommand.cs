using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Cms.Application.Splash;

public sealed record UnpublishSplashScreenCommand(
    SplashScreenId Id)
    : ICommand<SplashScreenResponse>;