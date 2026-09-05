namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct PaymentTransactionId(Guid Value)
{
    public static PaymentTransactionId New() => new(Guid.CreateVersion7());
    public static PaymentTransactionId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}