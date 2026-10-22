namespace Sanad.Modules.Cms.Domain.Settings;

/// <summary>
/// The identity of the one site-settings row. The value is fixed, so every
/// write targets the same key and a concurrent first write can never create a
/// second row.
/// </summary>
public readonly record struct SiteSettingsId(int Value)
{
    /// <summary>
    /// The one and only key of the single site-settings row. Persistence
    /// enforces the same value with a check constraint, so the table can
    /// never hold a second row.
    /// </summary>
    public const int SingletonKey = 1;

    public static SiteSettingsId Singleton { get; } = new(SingletonKey);

    public static SiteSettingsId Empty => new(0);

    public override string ToString() => Value.ToString();
}
