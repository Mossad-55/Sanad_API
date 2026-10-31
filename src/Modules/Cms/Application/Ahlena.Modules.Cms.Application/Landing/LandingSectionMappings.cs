using Ahlena.Modules.Cms.Domain.Landing;

namespace Ahlena.Modules.Cms.Application.Landing;

internal static class LandingSectionMappings
{
    public static string ToTypeKey(this LandingSectionType type) => type switch
    {
        LandingSectionType.Hero => "hero",
        LandingSectionType.PartnerLogos => "partner-logos",
        LandingSectionType.About => "about",
        LandingSectionType.Features => "features",
        LandingSectionType.ActivityCards => "activity-cards",
        LandingSectionType.Steps => "steps",
        LandingSectionType.DashboardPromo => "dashboard-promo",
        LandingSectionType.Testimonials => "testimonials",
        LandingSectionType.Faq => "faq",
        LandingSectionType.ContactForm => "contact-form",
        LandingSectionType.Footer => "footer",
        LandingSectionType.Stats => "stats",
        _ => throw new InvalidOperationException($"Unknown landing section type {(int)type}.")
    };

    public static LandingSectionPublicItem ToPublicItem(
        this LandingSection section,
        IReadOnlyList<TestimonialItem>? testimonials = null,
        IReadOnlyList<LandingFaqItem>? faqs = null) =>
        new(
            section.Id,
            section.Type.ToTypeKey(),
            section.ArabicTitle,
            section.EnglishTitle,
            string.IsNullOrWhiteSpace(section.ArabicBody) ? null : section.ArabicBody,
            string.IsNullOrWhiteSpace(section.EnglishBody) ? null : section.EnglishBody,
            string.IsNullOrWhiteSpace(section.ContentJson) ? null : section.ContentJson,
            string.IsNullOrWhiteSpace(section.ImagePath) ? null : section.ImagePath,
            section.DisplayOrder,
            testimonials,
            faqs);

    public static LandingAdminSectionResponse ToAdminResponse(
        this LandingSection section, int danglingRefCount = 0) =>
        new(
            section.Id.Value,
            section.Type.ToTypeKey(),
            section.Status.ToString(),
            section.ArabicTitle,
            section.EnglishTitle,
            string.IsNullOrWhiteSpace(section.ArabicBody) ? null : section.ArabicBody,
            string.IsNullOrWhiteSpace(section.EnglishBody) ? null : section.EnglishBody,
            string.IsNullOrWhiteSpace(section.ContentJson) ? null : section.ContentJson,
            string.IsNullOrWhiteSpace(section.ImagePath) ? null : section.ImagePath,
            section.DisplayOrder,
            section.IsVisible,
            section.SourceId?.Value,
            danglingRefCount,
            section.CreatedOnUtc,
            section.UpdatedOnUtc,
            section.PublishedOnUtc);
}
