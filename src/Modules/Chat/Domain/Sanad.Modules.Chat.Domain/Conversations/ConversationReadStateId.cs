namespace Sanad.Modules.Chat.Domain.Conversations;

public readonly record struct ConversationReadStateId(Guid Value)
{
    public static ConversationReadStateId New() => new(Guid.CreateVersion7());
    public static ConversationReadStateId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
