using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Cms.Application.Legal;

/// <summary>
/// Errors shared by every CMS app content surface (legal pages and the
/// Help Center). Admin account types are not app audiences and never get an
/// invented document.
/// </summary>
public static class ContentErrors
{
    /// <summary>
    /// Coded 400 for a malformed or undefined public legal document type. The
    /// anonymous public legal route constructs it locally with
    /// <c>BadRequestWithCode</c> (the same style as the app legal routes'
    /// <c>Cms.Content.InvalidAudience</c>), so it needs no
    /// <c>ResultProblemDetailsMapper</c> row.
    /// </summary>
    public const string InvalidDocumentTypeCode =
        "Cms.Content.InvalidDocumentType";

    public static readonly Error UnsupportedAudience =
        new(
            "Cms.Content.UnsupportedAudience",
            "This content surface serves app accounts only.");
}
