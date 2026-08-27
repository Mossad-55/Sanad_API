namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct SplashScreenId(Guid Value)
{
    public static SplashScreenId New() => new(Guid.CreateVersion7());
    public static SplashScreenId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}