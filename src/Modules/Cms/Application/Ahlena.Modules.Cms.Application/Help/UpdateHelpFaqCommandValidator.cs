using FluentValidation;
using Ahlena.Modules.Cms.Domain.Help;
using Ahlena.Modules.Cms.Domain.Legal;

namespace Ahlena.Modules.Cms.Application.Help;

public sealed class UpdateHelpFaqCommandValidator
    : AbstractValidator<UpdateHelpFaqCommand>
{
    public UpdateHelpFaqCommandValidator()
    {
        RuleFor(command => command.Id)
            .NotEqual(HelpFaqId.Empty);

        RuleFor(command => command.Audience)
            .Must(audience => audience.IsHelpAudience())
            .WithMessage(
                "Audience must be 1 (Family), 2 (MedicalCaregiver), " +
                "3 (CompanionCaregiver), or 4 (Elderly).");

        RuleFor(command => command.ArabicQuestion)
            .NotEmpty()
            .MaximumLength(HelpFaq.MaximumQuestionLength);

        RuleFor(command => command.EnglishQuestion)
            .NotEmpty()
            .MaximumLength(HelpFaq.MaximumQuestionLength);

        RuleFor(command => command.ArabicAnswer)
            .NotEmpty()
            .MaximumLength(HelpFaq.MaximumAnswerLength);

        RuleFor(command => command.EnglishAnswer)
            .NotEmpty()
            .MaximumLength(HelpFaq.MaximumAnswerLength);

        RuleFor(command => command.DisplayOrder)
            .GreaterThanOrEqualTo(0);
    }
}
