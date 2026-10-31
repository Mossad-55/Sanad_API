namespace Ahlena.Modules.Cms.Domain.Landing;

public readonly record struct LandingSectionHistoryId(Guid Value)
{
    public static LandingSectionHistoryId Empty => new(Guid.Empty);
    public static LandingSectionHistoryId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}
