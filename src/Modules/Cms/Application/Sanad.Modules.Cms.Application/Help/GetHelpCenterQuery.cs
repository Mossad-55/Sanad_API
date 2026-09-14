using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Application.Help;

/// <summary>
/// Signed-in Help Center read: active FAQs for the audience derived from the
/// JWT, plus the one global support contact when configured. The read is
/// non-destructive: an unseeded CMS still returns a successful empty shape.
/// </summary>
public sealed record GetHelpCenterQuery(
    LegalAudience Audience)
    : IQuery<HelpCenterResponse>;
