namespace Ahlena.Modules.Cms.Domain.Landing;

public readonly record struct LandingSectionId(Guid Value)
{
    public static LandingSectionId Empty => new(Guid.Empty);
    public static LandingSectionId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}
