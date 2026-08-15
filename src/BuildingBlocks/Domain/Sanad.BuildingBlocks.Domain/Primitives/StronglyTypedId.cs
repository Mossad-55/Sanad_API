namespace Sanad.BuildingBlocks.Domain.Primitives;

public abstract record StronglyTypedId(Guid Value) : IStronglyTypedId;