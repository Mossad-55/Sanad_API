using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Cms.Application.Legal;

/// <summary>
/// Errors shared by every CMS app content surface (legal pages and the
/// Help Center). Admin account types are not app audiences and never get an
/// invented document.
/// </summary>
public static class ContentErrors
{
    public static readonly Error UnsupportedAudience =
        new(
            "Cms.Content.UnsupportedAudience",
            "This content surface serves app accounts only.");
}
