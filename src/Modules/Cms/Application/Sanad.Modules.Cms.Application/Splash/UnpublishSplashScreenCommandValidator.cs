using FluentValidation;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Cms.Application.Splash;

public sealed class UnpublishSplashScreenCommandValidator :
    AbstractValidator<UnpublishSplashScreenCommand>
{
    public UnpublishSplashScreenCommandValidator()
    {
        RuleFor(command =>
                command.Id)
            .NotEqual(SplashScreenId.Empty);
    }
}