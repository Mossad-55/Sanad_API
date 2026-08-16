namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct VerificationRequestId(Guid Value)
{
    public static VerificationRequestId New()
        => new(Guid.CreateVersion7());

    public static VerificationRequestId Empty
        => new(Guid.Empty);

    public override string ToString()
        => Value.ToString();
}