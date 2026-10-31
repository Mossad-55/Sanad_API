using Ahlena.Modules.Cms.Domain.Help;
using Ahlena.Modules.Cms.Domain.Legal;

namespace Ahlena.Modules.Cms.Application.Help;

/// <summary>
/// Admin-facing FAQ record (active and inactive records included).
/// </summary>
public sealed record HelpFaqResponse(
    HelpFaqId Id,
    LegalAudience Audience,
    string ArabicQuestion,
    string EnglishQuestion,
    string ArabicAnswer,
    string EnglishAnswer,
    int DisplayOrder,
    bool IsActive,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc);
