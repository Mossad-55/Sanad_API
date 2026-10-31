using FluentValidation;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Application.Users;

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
