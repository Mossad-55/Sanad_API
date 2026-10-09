namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct CaregiverPayoutAccountReviewId(Guid Value)
{
    public static CaregiverPayoutAccountReviewId New() => new(Guid.CreateVersion7());
    public static CaregiverPayoutAccountReviewId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
