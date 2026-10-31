using FluentValidation;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Cms.Application.Splash;

public sealed class DeleteSplashScreenCommandValidator :
    AbstractValidator<DeleteSplashScreenCommand>
{
    public DeleteSplashScreenCommandValidator()
    {
        RuleFor(command =>
                command.Id)
            .NotEqual(SplashScreenId.Empty);
    }
}