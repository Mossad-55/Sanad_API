namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct BookingId(Guid Value)
{
    public static BookingId New() => new(Guid.CreateVersion7());
    public static BookingId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}