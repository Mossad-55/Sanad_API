namespace Sanad.BuildingBlocks.Domain.Abstractions;

public abstract record DomainEvent : IDomainEvent
{
    protected DomainEvent()
    {
        Id = Guid.CreateVersion7();
        OccurredOnUtc = DateTime.UtcNow;
    }

    public Guid Id { get; }

    public DateTime OccurredOnUtc { get; }
}