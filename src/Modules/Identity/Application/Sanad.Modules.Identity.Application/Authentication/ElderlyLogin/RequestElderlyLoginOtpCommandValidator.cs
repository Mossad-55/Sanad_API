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
            @"\A\+[1-9][0-9]{1,14}\z")
        .WithMessage(
            "Phone number must use E.164 format.");
    }
}