using FluentValidation;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Users;

public sealed class UpdateMyNotificationPreferencesCommandValidator :
    AbstractValidator<UpdateMyNotificationPreferencesCommand>
{
    public UpdateMyNotificationPreferencesCommandValidator()
    {
        RuleFor(command =>
                command.CurrentUserId)
            .NotEqual(
                UserId.Empty);
    }
}
