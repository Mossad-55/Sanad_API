namespace Sanad.Modules.Community.Domain.Blocks;

public readonly record struct CommunityAuthorBlockId(Guid Value)
{
    public static CommunityAuthorBlockId New() => new(Guid.CreateVersion7());
    public static CommunityAuthorBlockId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
