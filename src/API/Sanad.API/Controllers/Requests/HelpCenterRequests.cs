using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.API.Controllers.Requests;

/// <summary>
/// SET-13 admin Help Center requests. Audience is one of the four app
/// account types; support contact is one global pair.
/// </summary>
public sealed record CreateHelpFaqRequest(
    LegalAudience Audience,
    string ArabicQuestion,
    string EnglishQuestion,
    string ArabicAnswer,
    string EnglishAnswer,
    int DisplayOrder,
    bool IsActive);

public sealed record UpdateHelpFaqRequest(
    LegalAudience Audience,
    string ArabicQuestion,
    string EnglishQuestion,
    string ArabicAnswer,
    string EnglishAnswer,
    int DisplayOrder,
    bool IsActive);

public sealed record UpdateSupportContactRequest(
    string SupportPhone,
    string SupportEmail);
