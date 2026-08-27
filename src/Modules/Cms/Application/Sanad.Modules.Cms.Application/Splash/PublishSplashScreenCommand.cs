using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Cms.Application.Splash;

public sealed record PublishSplashScreenCommand(
    SplashScreenId Id)
    : ICommand<SplashScreenResponse>;