namespace Sanad.Modules.Cms.Domain.Landing;

public readonly record struct FamilyHomeSectionId(Guid Value)
{
    public static FamilyHomeSectionId Empty => new(Guid.Empty);
    public static FamilyHomeSectionId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}