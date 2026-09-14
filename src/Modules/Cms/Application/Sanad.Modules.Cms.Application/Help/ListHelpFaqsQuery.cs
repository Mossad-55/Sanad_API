using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Application.Help;

public sealed record ListHelpFaqsQuery(
    LegalAudience? Audience,
    bool? IsActive)
    : IQuery<IReadOnlyList<HelpFaqResponse>>;
