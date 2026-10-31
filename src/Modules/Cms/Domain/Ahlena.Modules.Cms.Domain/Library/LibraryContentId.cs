namespace Ahlena.Modules.Cms.Domain.Library;

public readonly record struct LibraryContentId(Guid Value)
{
    public static LibraryContentId Empty => new(Guid.Empty);
    public static LibraryContentId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}
