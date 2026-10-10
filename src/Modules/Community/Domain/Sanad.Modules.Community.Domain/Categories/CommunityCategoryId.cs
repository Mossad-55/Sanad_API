namespace Sanad.Modules.Community.Domain.Categories;

public readonly record struct CommunityCategoryId(Guid Value)
{
    public static CommunityCategoryId Empty => new(Guid.Empty);
    public static CommunityCategoryId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}
