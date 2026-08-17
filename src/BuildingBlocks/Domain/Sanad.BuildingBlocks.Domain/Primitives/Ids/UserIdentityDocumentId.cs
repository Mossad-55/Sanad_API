namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct UserIdentityDocumentId(Guid Value)
{
    public static UserIdentityDocumentId New() => new(Guid.CreateVersion7());

    public static UserIdentityDocumentId Empty => new(Guid.Empty);

    public override string ToString() => Value.ToString(); 
}