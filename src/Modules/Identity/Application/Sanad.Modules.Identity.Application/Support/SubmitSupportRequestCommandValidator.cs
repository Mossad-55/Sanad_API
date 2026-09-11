using FluentValidation;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Support;

namespace Sanad.Modules.Identity.Application.Support;

public sealed class SubmitSupportRequestCommandValidator :
    AbstractValidator<SubmitSupportRequestCommand>
{
    public SubmitSupportRequestCommandValidator()
    {
        RuleFor(command =>
                command.CurrentUserId)
            .NotEqual(
                UserId.Empty);

        RuleFor(command =>
                command.Subject)
            .NotEmpty()
            .MinimumLength(
                SupportTicket.MinimumSubjectLength)
            .MaximumLength(
                SupportTicket.MaximumSubjectLength);

        RuleFor(command =>
                command.Message)
            .NotEmpty()
            .MinimumLength(
                SupportTicket.MinimumMessageLength)
            .MaximumLength(
                SupportTicket.MaximumMessageLength);
    }
}
