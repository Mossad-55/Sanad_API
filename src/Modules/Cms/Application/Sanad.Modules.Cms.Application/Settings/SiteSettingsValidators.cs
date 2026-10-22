using FluentValidation;
using Sanad.Modules.Cms.Domain.Settings;

namespace Sanad.Modules.Cms.Application.Settings;

/// <summary>
/// The bounded shape rules of the admin authoring request. The link target
/// rules and the content rules are enforced by the command handler, because
/// they are domain rules and not request-shape rules.
/// </summary>
public sealed class UpdateSiteSettingsCommandValidator :
    AbstractValidator<UpdateSiteSettingsCommand>
{
    public UpdateSiteSettingsCommandValidator()
    {
        RuleFor(command => command.NavbarLinks)
            .Must(links => links is null ||
                links.Count <= SiteSettings.MaximumLinkCount)
            .WithMessage(
                $"At most {SiteSettings.MaximumLinkCount} navbar links are allowed.");

        RuleForEach(command => command.NavbarLinks)
            .ChildRules(link =>
            {
                link.RuleFor(item => item.Key)
                    .NotEmpty()
                    .MaximumLength(SiteSettings.MaximumLinkKeyLength);
                link.RuleFor(item => item.ArabicLabel)
                    .NotEmpty()
                    .MaximumLength(SiteSettings.MaximumLinkLabelLength);
                link.RuleFor(item => item.EnglishLabel)
                    .NotEmpty()
                    .MaximumLength(SiteSettings.MaximumLinkLabelLength);
                link.RuleFor(item => item.Url)
                    .NotEmpty()
                    .MaximumLength(SiteSettings.MaximumLinkUrlLength);
            });

        RuleFor(command => command.Seo)
            .ChildRules(seo =>
            {
                // The SEO block is optional, so the child rules run against a
                // possibly absent value; an absent block has nothing to check.
                seo.RuleFor(item => item!.ArabicTitle)
                    .MaximumLength(SiteSettings.MaximumSeoTitleLength);
                seo.RuleFor(item => item!.EnglishTitle)
                    .MaximumLength(SiteSettings.MaximumSeoTitleLength);
                seo.RuleFor(item => item!.ArabicDescription)
                    .MaximumLength(SiteSettings.MaximumSeoDescriptionLength);
                seo.RuleFor(item => item!.EnglishDescription)
                    .MaximumLength(SiteSettings.MaximumSeoDescriptionLength);
            });
    }
}
