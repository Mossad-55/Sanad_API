using FluentValidation;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Cms.Application.Splash;

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