using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.Modules.Cms.Domain.Legal;

namespace Ahlena.Modules.Cms.Application.Help;

/// <summary>
/// Signed-in Help Center read: active FAQs for the audience derived from the
/// JWT, plus the one global support contact when configured. The read is
/// non-destructive: an unseeded CMS still returns a successful empty shape.
/// </summary>
public sealed record GetHelpCenterQuery(
    LegalAudience Audience)
    : IQuery<HelpCenterResponse>;
