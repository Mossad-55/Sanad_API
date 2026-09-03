namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct ElderlyActivityLogId(Guid Value)
{
    public static ElderlyActivityLogId New() => new(Guid.CreateVersion7());
    public static ElderlyActivityLogId Empty => new(Guid.Empty);

    public override string ToString() => Value.ToString();
}