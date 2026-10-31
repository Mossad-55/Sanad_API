using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.Modules.Cms.Domain.Help;
using Ahlena.Modules.Cms.Domain.Legal;

namespace Ahlena.Modules.Cms.Application.Help;

public sealed record UpdateHelpFaqCommand(
    HelpFaqId Id,
    LegalAudience Audience,
    string ArabicQuestion,
    string EnglishQuestion,
    string ArabicAnswer,
    string EnglishAnswer,
    int DisplayOrder,
    bool IsActive)
    : ICommand<HelpFaqResponse>;
