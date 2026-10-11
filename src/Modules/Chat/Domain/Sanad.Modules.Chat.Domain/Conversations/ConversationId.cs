namespace Sanad.Modules.Chat.Domain.Conversations;

public readonly record struct ConversationId(Guid Value)
{
    public static ConversationId New() => new(Guid.CreateVersion7());
    public static ConversationId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
