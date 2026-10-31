namespace Ahlena.Modules.Cms.Domain.Landing;

public readonly record struct FamilyHomeSectionHistoryId(Guid Value)
{
    public static FamilyHomeSectionHistoryId Empty => new(Guid.Empty);
    public static FamilyHomeSectionHistoryId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}