namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.CreateVersion7());

    public static UserId Empty => new(Guid.Empty);

    public override string ToString() => Value.ToString();
}