namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct BookingCancellationFactId(Guid Value)
{
    public static BookingCancellationFactId New() => new(Guid.CreateVersion7());
    public static BookingCancellationFactId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
