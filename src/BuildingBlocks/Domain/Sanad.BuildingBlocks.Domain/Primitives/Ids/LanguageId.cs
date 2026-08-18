namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct LanguageId(Guid Value)
{
    public static LanguageId New() => new(Guid.CreateVersion7());
    public static LanguageId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}