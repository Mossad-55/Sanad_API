using FluentValidation;
using Ahlena.Modules.Cms.Domain.Legal;

namespace Ahlena.Modules.Cms.Application.Legal;

public sealed class PublishLegalDocumentCommandValidator
    : AbstractValidator<PublishLegalDocumentCommand>
{
    public PublishLegalDocumentCommandValidator()
    {
        RuleFor(command => command.Id)
            .NotEqual(LegalDocumentId.Empty);
    }
}
