namespace Sanad.Modules.Cms.Domain.Library;

public readonly record struct LibraryCategoryId(Guid Value)
{
    public static LibraryCategoryId Empty => new(Guid.Empty);
    public static LibraryCategoryId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}
