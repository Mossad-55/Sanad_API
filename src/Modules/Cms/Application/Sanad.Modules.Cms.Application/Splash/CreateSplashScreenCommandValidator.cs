using FluentValidation;
using Sanad.Modules.Cms.Domain.Splash;

namespace Sanad.Modules.Cms.Application.Splash;

public sealed class CreateSplashScreenCommandValidator :
    AbstractValidator<CreateSplashScreenCommand>
{
    public CreateSplashScreenCommandValidator()
    {
        RuleFor(command =>
                command.InternalName)
            .NotEmpty()
            .MaximumLength(
                SplashScreen.MaximumInternalNameLength);

        RuleFor(command =>
                command.ArabicTitle)
            .NotEmpty()
            .MaximumLength(
                SplashScreen.MaximumTitleLength);

        RuleFor(command =>
                command.EnglishTitle)
            .NotEmpty()
            .MaximumLength(
                SplashScreen.MaximumTitleLength);

        RuleFor(command =>
                command.ArabicDescription)
            .NotEmpty()
            .MaximumLength(
                SplashScreen.MaximumDescriptionLength);

        RuleFor(command =>
                command.EnglishDescription)
            .NotEmpty()
            .MaximumLength(
                SplashScreen.MaximumDescriptionLength);

        RuleFor(command =>
                command.ArabicButtonText)
            .NotEmpty()
            .MaximumLength(
                SplashScreen.MaximumButtonTextLength);

        RuleFor(command =>
                command.EnglishButtonText)
            .NotEmpty()
            .MaximumLength(
                SplashScreen.MaximumButtonTextLength);

        RuleFor(command =>
                command.ImagePath)
            .NotEmpty()
            .MaximumLength(
                SplashScreen.MaximumImagePathLength);

        RuleFor(command =>
                command.BackgroundColor)
            .NotEmpty()
            .Matches(@"\A#[0-9A-Fa-f]{6}\z")
            .WithMessage(
                "Background color must be a #RRGGBB hex value.");

        RuleFor(command =>
                command.DisplayOrder)
            .GreaterThanOrEqualTo(0);
    }
}