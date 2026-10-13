namespace Sanad.Modules.Chat.Domain.Outbox;

public readonly record struct ChatOutboxItemId(Guid Value)
{
    public static ChatOutboxItemId New() => new(Guid.CreateVersion7());
    public static ChatOutboxItemId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
