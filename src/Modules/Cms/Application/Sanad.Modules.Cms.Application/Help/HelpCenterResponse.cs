using Sanad.Modules.Cms.Domain.Help;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Application.Help;

/// <summary>
/// Signed-in Help Center read surface: the active FAQ list for the audience
/// derived from the JWT, plus the one global support contact when CMS has
/// configured it. The read never fails just because nothing is seeded.
/// </summary>
public sealed record HelpCenterResponse(
    IReadOnlyList<HelpCenterFaqItem> Faqs,
    HelpCenterSupportContact? SupportContact);

public sealed record HelpCenterFaqItem(
    HelpFaqId Id,
    LegalAudience Audience,
    string ArabicQuestion,
    string EnglishQuestion,
    string ArabicAnswer,
    string EnglishAnswer,
    int DisplayOrder);

public sealed record HelpCenterSupportContact(
    string SupportPhone,
    string SupportEmail);
