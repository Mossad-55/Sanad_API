namespace Sanad.Modules.Cms.Domain.Legal;

/// <summary>
/// Lifecycle: Draft -> Published -> Archived. Publishing a new draft archives
/// the previous published version; archived versions are retained forever.
/// </summary>
public enum LegalDocumentStatus
{
    Draft = 1,
    Published = 2,
    Archived = 3
}
