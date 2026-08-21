namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct UserExternalLoginId(Guid Value)
{
    public static UserExternalLoginId New() => new(Guid.CreateVersion7());

    public static UserExternalLoginId Empty => new(Guid.Empty);

    public override string ToString() => Value.ToString();
}