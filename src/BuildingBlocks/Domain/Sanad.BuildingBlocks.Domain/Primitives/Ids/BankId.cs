namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct BankId(Guid Value)
{
    public static BankId New() => new(Guid.CreateVersion7());
    public static BankId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
