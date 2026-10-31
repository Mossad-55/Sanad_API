using FluentValidation;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Application.Authentication.Sessions;

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