using FluentValidation;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Application.Legal;

public sealed class CreateLegalDocumentCommandValidator
    : AbstractValidator<CreateLegalDocumentCommand>
{
    public CreateLegalDocumentCommandValidator()
    {
        RuleFor(command => command.DocumentType)
            .IsInEnum()
            .WithMessage(
                "Document type must be 1 (PrivacyPolicy) or " +
                "2 (TermsAndConditions).");

        RuleFor(command => command.Audience)
            .IsInEnum()
            .WithMessage(
                "Audience must be 1 (Family), 2 (MedicalCaregiver), " +
                "3 (CompanionCaregiver), or 4 (Elderly).");

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

        RuleFor(command => command)
            .Must(command =>
                command.Sections is not null &&
                LegalDocumentShape
                    .Validate(
                        command.DocumentType,
                        LegalSectionInput.ToDrafts(command.Sections))
                    .Count == 0)
            .WithMessage(
                "Sections violate the document shape rules: at least one " +
                "section, unique positive display order, exactly one " +
                "UserRights section with Arabic and English bullets for a " +
                "privacy policy, and no UserRights section for terms.");
    }
}

public sealed class LegalSectionInputValidator
    : AbstractValidator<LegalSectionInput>
{
    public LegalSectionInputValidator()
    {
        RuleFor(section => section.SectionType)
            .IsInEnum()
            .WithMessage(
                "Section type must be 1 (Text) or 2 (UserRights).");

        RuleFor(section => section.DisplayOrder)
            .GreaterThan(0)
            .WithMessage(
                "Section display order must be a positive number.");

        RuleFor(section => section.ArabicTitle)
            .NotEmpty()
            .MaximumLength(LegalSection.MaximumTitleLength);

        RuleFor(section => section.EnglishTitle)
            .NotEmpty()
            .MaximumLength(LegalSection.MaximumTitleLength);

        RuleFor(section => section.ArabicDescription)
            .NotEmpty()
            .MaximumLength(LegalSection.MaximumDescriptionLength);

        RuleFor(section => section.EnglishDescription)
            .NotEmpty()
            .MaximumLength(LegalSection.MaximumDescriptionLength);

        RuleFor(section => section.ArabicBullets)
            .Must(bullets =>
                bullets is null ||
                bullets.Count <= LegalSection.MaximumBulletCount)
            .WithMessage(
                $"Arabic bullets cannot exceed " +
                $"{LegalSection.MaximumBulletCount} items.");

        RuleFor(section => section.EnglishBullets)
            .Must(bullets =>
                bullets is null ||
                bullets.Count <= LegalSection.MaximumBulletCount)
            .WithMessage(
                $"English bullets cannot exceed " +
                $"{LegalSection.MaximumBulletCount} items.");

        RuleForEach(section => section.ArabicBullets)
            .NotEmpty()
            .MaximumLength(LegalSection.MaximumBulletLength)
            .OverridePropertyName("arabicBullets");

        RuleForEach(section => section.EnglishBullets)
            .NotEmpty()
            .MaximumLength(LegalSection.MaximumBulletLength)
            .OverridePropertyName("englishBullets");
    }
}
