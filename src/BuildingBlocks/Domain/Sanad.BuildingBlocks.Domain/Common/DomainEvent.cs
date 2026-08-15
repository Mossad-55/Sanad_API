using Sanad.BuildingBlocks.Domain.Abstractions;

namespace Sanad.BuildingBlocks.Domain.Common;

public abstract record DomainEvent : IDomainEvent
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}