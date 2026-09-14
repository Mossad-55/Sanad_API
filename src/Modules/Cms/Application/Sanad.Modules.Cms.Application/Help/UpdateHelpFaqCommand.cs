using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.Modules.Cms.Domain.Help;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Application.Help;

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
