namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct BookingReviewId(Guid Value)
{
    public static BookingReviewId Empty => new(Guid.Empty);
    public static BookingReviewId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
