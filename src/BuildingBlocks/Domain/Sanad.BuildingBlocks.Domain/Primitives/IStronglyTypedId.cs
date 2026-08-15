namespace Sanad.BuildingBlocks.Domain.Primitives;

public interface IStronglyTypedId
{
    Guid Value { get; }
}