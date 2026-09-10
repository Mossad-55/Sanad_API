using FluentValidation;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Users;

public sealed class UpdateMyUiLanguageCommandValidator :
    AbstractValidator<UpdateMyUiLanguageCommand>
{
    public UpdateMyUiLanguageCommandValidator()
    {
        RuleFor(command =>
                command.CurrentUserId)
            .NotEqual(
                UserId.Empty);

        RuleFor(command =>
                command.UiLanguage)
            .IsInEnum();
    }
}
