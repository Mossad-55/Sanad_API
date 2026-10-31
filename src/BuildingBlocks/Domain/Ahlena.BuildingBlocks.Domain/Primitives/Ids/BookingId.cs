namespace Ahlena.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct BookingId(Guid Value) : IComparable<BookingId>
{
    public static BookingId New() => new(Guid.CreateVersion7());
    public static BookingId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();

    /// <summary>
    /// Orders by the underlying primitive so a deterministic ordering key exists for both the
    /// relational provider (which orders the converted column) and CLR-side sorting of
    /// materialized rows (which requires comparable elements).
    /// </summary>
    public int CompareTo(BookingId other) => Value.CompareTo(other.Value);
}