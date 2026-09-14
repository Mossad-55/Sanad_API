using FluentValidation;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Application.Legal;

public sealed class UpdateLegalDocumentSectionsCommandValidator
    : AbstractValidator<UpdateLegalDocumentSectionsCommand>
{
    public UpdateLegalDocumentSectionsCommandValidator()
    {
        RuleFor(command => command.Id)
            .NotEqual(LegalDocumentId.Empty);

        RuleFor(command => command.Sections)
            .NotEmpty()
            .WithMessage(
                "A legal document requires at least one section.")
            .Must(sections =>
                sections is null ||
                sections.Count <= LegalDocument.MaximumSectionCount)
            .WithMessage(
                $"A legal document cannot contain more than " +
                $"{LegalDocument.MaximumSectionCount} sections.");

        RuleForEach(command => command.Sections)
            .SetValidator(new LegalSectionInputValidator());
    }
}
