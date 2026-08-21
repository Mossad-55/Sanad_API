using FluentValidation;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Authentication.Registration;

namespace Sanad.Modules.Identity.Application.Authentication.Password;

public sealed class ChangePasswordCommandValidator :
    AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(command =>
                command.CurrentUserId)
            .NotEqual(
                UserId.Empty);

        RuleFor(command =>
                command.CurrentPassword)
            .NotEmpty();

        RuleFor(command =>
                command.NewPassword)
            .NotEmpty()
            .MinimumLength(10)
            .MaximumLength(
                RegisterUserCommandValidator
                    .MaximumPasswordLength)
            .Matches("[A-Z]")
            .WithMessage(
                "Password must contain an uppercase letter.")
            .Matches("[a-z]")
            .WithMessage(
                "Password must contain a lowercase letter.")
            .Matches("[0-9]")
            .WithMessage(
                "Password must contain a number.");
    }
}