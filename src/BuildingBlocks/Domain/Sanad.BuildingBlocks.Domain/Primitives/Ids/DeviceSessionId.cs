namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct DeviceSessionId(Guid Value)
{
    public static DeviceSessionId New() => new(Guid.CreateVersion7());
    public static DeviceSessionId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}