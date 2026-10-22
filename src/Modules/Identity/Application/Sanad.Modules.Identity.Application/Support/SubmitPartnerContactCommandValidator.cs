using FluentValidation;
using Sanad.Modules.Identity.Domain.Support;

namespace Sanad.Modules.Identity.Application.Support;

public sealed class SubmitPartnerContactCommandValidator :
    AbstractValidator<SubmitPartnerContactCommand>
{
    public SubmitPartnerContactCommandValidator()
    {
        RuleFor(command =>
                command.Name)
            .NotEmpty()
            .MinimumLength(
                PartnerContact.MinimumNameLength)
            .MaximumLength(
                PartnerContact.MaximumNameLength);

        RuleFor(command =>
                command.Company)
            .MaximumLength(
                PartnerContact.MaximumCompanyLength)
            .When(command => command.Company is not null);

        RuleFor(command =>
                command.Email)
            .NotEmpty()
            .MaximumLength(
                PartnerContact.MaximumEmailLength)
            .EmailAddress();

        RuleFor(command =>
                command.Subject)
            .MaximumLength(
                PartnerContact.MaximumSubjectLength)
            .When(command => command.Subject is not null);

        RuleFor(command =>
                command.Message)
            .NotEmpty()
            .MinimumLength(
                PartnerContact.MinimumMessageLength)
            .MaximumLength(
                PartnerContact.MaximumMessageLength);

        RuleFor(command =>
                command.IdempotencyKey)
            .NotEmpty()
            .MaximumLength(
                PartnerContact.MaximumIdempotencyKeyLength);

        RuleFor(command =>
                command.ClientKey)
            .NotEmpty()
            .MaximumLength(
                PartnerContact.MaximumClientKeyLength);
    }
}
