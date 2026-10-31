namespace Ahlena.Modules.Chat.Domain.Changes;

public readonly record struct ChatChangeId(Guid Value)
{
    public static ChatChangeId New() => new(Guid.CreateVersion7());
    public static ChatChangeId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
