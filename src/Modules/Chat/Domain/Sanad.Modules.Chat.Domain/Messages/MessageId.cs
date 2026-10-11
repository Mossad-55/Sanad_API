namespace Sanad.Modules.Chat.Domain.Messages;

public readonly record struct MessageId(Guid Value)
{
    public static MessageId New() => new(Guid.CreateVersion7());
    public static MessageId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
