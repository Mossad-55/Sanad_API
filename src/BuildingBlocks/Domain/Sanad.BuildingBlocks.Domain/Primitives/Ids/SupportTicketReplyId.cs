namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct SupportTicketReplyId(Guid Value) : IComparable<SupportTicketReplyId>, IComparable
{
    public static SupportTicketReplyId New() => new(Guid.CreateVersion7());
    public static SupportTicketReplyId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();

    public int CompareTo(SupportTicketReplyId other) => Value.CompareTo(other.Value);
    public int CompareTo(object? obj) => obj is SupportTicketReplyId other ? CompareTo(other) : throw new ArgumentException("Invalid type", nameof(obj));
}