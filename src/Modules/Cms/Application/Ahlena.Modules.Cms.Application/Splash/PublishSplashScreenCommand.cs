using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Cms.Application.Splash;

public sealed record PublishSplashScreenCommand(
    SplashScreenId Id)
    : ICommand<SplashScreenResponse>;