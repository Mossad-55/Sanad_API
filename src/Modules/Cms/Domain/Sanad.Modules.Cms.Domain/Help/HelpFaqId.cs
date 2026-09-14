namespace Sanad.Modules.Cms.Domain.Help;

/// <summary>
/// Strongly typed id in the repository's shared id shape
/// (<c>readonly record struct</c> over a Guid).
/// </summary>
public readonly record struct HelpFaqId(Guid Value)
{
    public static HelpFaqId New() => new(Guid.CreateVersion7());
    public static HelpFaqId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
