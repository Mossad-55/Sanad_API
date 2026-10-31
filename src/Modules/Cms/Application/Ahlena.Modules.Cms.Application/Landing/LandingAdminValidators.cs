using FluentValidation;
using Ahlena.Modules.Cms.Domain.Landing;

namespace Ahlena.Modules.Cms.Application.Landing;

public sealed class CreateLandingSectionCommandValidator :
    AbstractValidator<CreateLandingSectionCommand>
{
    public CreateLandingSectionCommandValidator()
    {
        RuleFor(command => command.Type).IsInEnum();
        RuleFor(command => command.ArabicTitle)
            .MaximumLength(LandingSection.MaximumTitleLength);
        RuleFor(command => command.EnglishTitle)
            .MaximumLength(LandingSection.MaximumTitleLength);
        RuleFor(command => command.ArabicBody)
            .MaximumLength(LandingSection.MaximumBodyLength);
        RuleFor(command => command.EnglishBody)
            .MaximumLength(LandingSection.MaximumBodyLength);
        RuleFor(command => command.ContentJson)
            .MaximumLength(LandingSection.MaximumContentJsonLength);
        RuleFor(command => command.ImagePath)
            .MaximumLength(LandingSection.MaximumImagePathLength);
        RuleFor(command => command.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateLandingSectionCommandValidator :
    AbstractValidator<UpdateLandingSectionCommand>
{
    public UpdateLandingSectionCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.ArabicTitle)
            .MaximumLength(LandingSection.MaximumTitleLength);
        RuleFor(command => command.EnglishTitle)
            .MaximumLength(LandingSection.MaximumTitleLength);
        RuleFor(command => command.ArabicBody)
            .MaximumLength(LandingSection.MaximumBodyLength);
        RuleFor(command => command.EnglishBody)
            .MaximumLength(LandingSection.MaximumBodyLength);
        RuleFor(command => command.ContentJson)
            .MaximumLength(LandingSection.MaximumContentJsonLength);
        RuleFor(command => command.ImagePath)
            .MaximumLength(LandingSection.MaximumImagePathLength);
        RuleFor(command => command.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class ReorderLandingSectionsCommandValidator :
    AbstractValidator<ReorderLandingSectionsCommand>
{
    public ReorderLandingSectionsCommandValidator()
    {
        RuleFor(command => command.Items).NotEmpty();
    }
}
