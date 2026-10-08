namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct CaregiverPayoutId(Guid Value)
{
    public static CaregiverPayoutId New() => new(Guid.CreateVersion7());
    public static CaregiverPayoutId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
