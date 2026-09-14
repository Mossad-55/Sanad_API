using Sanad.Modules.Cms.Domain.Help;

namespace Sanad.Modules.Cms.Application.Help;

internal static class HelpCenterMappings
{
    public static HelpFaqResponse ToResponse(
        this HelpFaq faq)
    {
        return new HelpFaqResponse(
            faq.Id,
            faq.Audience,
            faq.ArabicQuestion,
            faq.EnglishQuestion,
            faq.ArabicAnswer,
            faq.EnglishAnswer,
            faq.DisplayOrder,
            faq.IsActive,
            faq.CreatedOnUtc,
            faq.UpdatedOnUtc);
    }

    public static HelpCenterFaqItem ToAppItem(
        this HelpFaq faq)
    {
        return new HelpCenterFaqItem(
            faq.Id,
            faq.Audience,
            faq.ArabicQuestion,
            faq.EnglishQuestion,
            faq.ArabicAnswer,
            faq.EnglishAnswer,
            faq.DisplayOrder);
    }

    public static SupportContactResponse ToResponse(
        this SupportContact contact)
    {
        return new SupportContactResponse(
            contact.SupportPhone,
            contact.SupportEmail,
            contact.CreatedOnUtc,
            contact.UpdatedOnUtc);
    }
}
