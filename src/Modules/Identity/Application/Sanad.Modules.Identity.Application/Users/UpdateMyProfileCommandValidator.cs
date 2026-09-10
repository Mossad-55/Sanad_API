using FluentValidation;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Users;

public sealed class UpdateMyProfileCommandValidator :
    AbstractValidator<UpdateMyProfileCommand>
{
    public UpdateMyProfileCommandValidator()
    {
        RuleFor(command =>
                command.CurrentUserId)
            .NotEqual(
                UserId.Empty);

        RuleFor(command =>
                command.ArabicFullName)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(200)
            .When(command =>
                command.ArabicFullName is not null);

        RuleFor(command =>
                command.EnglishFullName)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(200)
            .When(command =>
                command.EnglishFullName is not null);

        RuleFor(command =>
                command.Email)
            .NotEmpty()
            .MaximumLength(256)
            .EmailAddress()
            .When(command =>
                command.Email is not null);

        RuleFor(command =>
                command.PhoneNumber)
            .NotEmpty()
            .Matches(
                @"\A\+[1-9][0-9]{1,14}\z")
            .WithMessage(
                "Phone number must use E.164 format.")
            .When(command =>
                command.PhoneNumber is not null);
    }
}
