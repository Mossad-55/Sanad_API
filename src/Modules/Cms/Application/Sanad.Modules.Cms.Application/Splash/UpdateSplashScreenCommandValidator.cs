using FluentValidation;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Cms.Domain.Splash;

namespace Sanad.Modules.Cms.Application.Splash;

public sealed class UpdateSplashScreenCommandValidator :
    AbstractValidator<UpdateSplashScreenCommand>
{
    public UpdateSplashScreenCommandValidator()
    {
        RuleFor(command =>
                command.Id)
            .NotEqual(SplashScreenId.Empty);

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