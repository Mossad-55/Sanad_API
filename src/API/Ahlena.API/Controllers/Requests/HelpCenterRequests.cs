using Ahlena.Modules.Cms.Domain.Legal;

namespace Ahlena.API.Controllers.Requests;

/// <summary>
/// SET-13 admin Help Center requests. Audience is one of the four app
/// account types; support contact is one global pair whose two values are
/// independent and may each be null (which clears that field).
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
    string? SupportPhone,
    string? SupportEmail);
