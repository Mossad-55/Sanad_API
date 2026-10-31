namespace Ahlena.Modules.Chat.Domain.Blocks;

public readonly record struct BlockId(Guid Value)
{
    public static BlockId New() => new(Guid.CreateVersion7());
    public static BlockId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
