using FluentValidation;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Cms.Application.Splash;

public sealed class PublishSplashScreenCommandValidator :
    AbstractValidator<PublishSplashScreenCommand>
{
    public PublishSplashScreenCommandValidator()
    {
        RuleFor(command =>
                command.Id)
            .NotEqual(SplashScreenId.Empty);
    }
}