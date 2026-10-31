using FluentValidation;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Application.Authentication.Sessions;

public sealed class LogoutCurrentSessionCommandValidator :
    AbstractValidator<LogoutCurrentSessionCommand>
{
    public LogoutCurrentSessionCommandValidator()
    {
        RuleFor(command =>
                command.DeviceSessionId)
            .NotEqual(
                DeviceSessionId.Empty);

        RuleFor(command =>
                command.CurrentUserId)
            .NotEqual(
                UserId.Empty);
    }
}