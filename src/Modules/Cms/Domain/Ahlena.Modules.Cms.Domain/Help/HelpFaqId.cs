namespace Ahlena.Modules.Cms.Domain.Help;

/// <summary>
/// Strongly typed id in the repository's shared id shape
/// (<c>readonly record struct</c> over a Guid).
/// </summary>
public readonly record struct HelpFaqId(Guid Value) : IComparable<HelpFaqId>
{
    public static HelpFaqId New() => new(Guid.CreateVersion7());
    public static HelpFaqId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();

    /// <summary>
    /// Orders by the underlying primitive so a deterministic ordering key exists for both the
    /// relational provider (which orders the converted column) and CLR-side sorting of
    /// materialized rows (which requires comparable elements) — the same pattern as the
    /// building blocks <c>BookingId</c>, adopted after the admin FAQ list failed on tied
    /// audience/display-order rows.
    /// </summary>
    public int CompareTo(HelpFaqId other) => Value.CompareTo(other.Value);
}
