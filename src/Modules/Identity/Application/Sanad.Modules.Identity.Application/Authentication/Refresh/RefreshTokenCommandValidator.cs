using FluentValidation;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Authentication.Refresh;

public sealed class RefreshTokenCommandValidator :
    AbstractValidator<RefreshTokenCommand>
{
    public const int MaximumRefreshTokenLength = 4096;

    public RefreshTokenCommandValidator()
    {
        RuleFor(command =>
                command.DeviceSessionId)
            .NotEqual(
                DeviceSessionId.Empty);

        RuleFor(command =>
                command.RefreshToken)
            .NotEmpty()
            .MaximumLength(
                MaximumRefreshTokenLength);
    }
}