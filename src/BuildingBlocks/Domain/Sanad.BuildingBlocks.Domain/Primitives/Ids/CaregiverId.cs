namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct CaregiverId(Guid Value)
{
    public static CaregiverId New()
        => new(Guid.CreateVersion7());

    public override string ToString()
        => Value.ToString();
}