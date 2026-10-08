namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct CaregiverPayoutAccountId(Guid Value)
{
    public static CaregiverPayoutAccountId New() => new(Guid.CreateVersion7());
    public static CaregiverPayoutAccountId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
