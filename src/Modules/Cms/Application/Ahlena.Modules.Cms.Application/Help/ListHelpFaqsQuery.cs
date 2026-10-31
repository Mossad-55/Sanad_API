using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.Modules.Cms.Domain.Legal;

namespace Ahlena.Modules.Cms.Application.Help;

public sealed record ListHelpFaqsQuery(
    LegalAudience? Audience,
    bool? IsActive)
    : IQuery<IReadOnlyList<HelpFaqResponse>>;
