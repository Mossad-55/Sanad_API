namespace Sanad.Modules.Cms.Domain.Legal;

/// <summary>
/// Strongly typed id in the repository's shared id shape
/// (<c>readonly record struct</c> over a Guid).
/// </summary>
public readonly record struct LegalDocumentId(Guid Value)
{
    public static LegalDocumentId New() => new(Guid.CreateVersion7());
    public static LegalDocumentId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
