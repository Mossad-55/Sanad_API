using FluentValidation;

namespace Sanad.Modules.Identity.Application.Authentication.ElderlyLogin;

public sealed class RequestElderlyLoginOtpCommandValidator :
    AbstractValidator<RequestElderlyLoginOtpCommand>
{
    public RequestElderlyLoginOtpCommandValidator()
    {
        RuleFor(command =>
            command.PhoneNumber)
        .NotEmpty()
        .Matches(
            @"^\+[1-9]\d{1,14}$")
        .WithMessage(
            "Phone number must use E.164 format.");
    }
}