namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct FamilyInvitationId(Guid Value)
{
    public static FamilyInvitationId New() =>
        new(Guid.CreateVersion7());

    public static FamilyInvitationId Empty =>
        new(Guid.Empty);

    public override string ToString() => Value.ToString();
}