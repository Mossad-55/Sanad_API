using FluentValidation;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Application.Legal;

public sealed class PublishLegalDocumentCommandValidator
    : AbstractValidator<PublishLegalDocumentCommand>
{
    public PublishLegalDocumentCommandValidator()
    {
        RuleFor(command => command.Id)
            .NotEqual(LegalDocumentId.Empty);
    }
}
