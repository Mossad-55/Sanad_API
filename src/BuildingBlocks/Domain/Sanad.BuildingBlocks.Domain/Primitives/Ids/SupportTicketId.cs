namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct SupportTicketId(Guid Value)
{
    public static SupportTicketId New() => new(Guid.CreateVersion7());
    public static SupportTicketId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
