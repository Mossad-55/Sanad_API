using FluentValidation;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Cms.Application.Splash;

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