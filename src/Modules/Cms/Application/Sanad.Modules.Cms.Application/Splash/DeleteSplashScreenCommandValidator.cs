using FluentValidation;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Cms.Application.Splash;

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