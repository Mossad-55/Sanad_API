using FluentValidation;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Authentication.Sessions;

public sealed class LogoutAllSessionsCommandValidator :
    AbstractValidator<LogoutAllSessionsCommand>
{
    public LogoutAllSessionsCommandValidator()
    {
        RuleFor(command =>
                command.CurrentUserId)
            .NotEqual(
                UserId.Empty);
    }
}