using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Families;

public sealed class FamilyMember : Entity<UserId>
{
    private FamilyMember()
    {
    }

    private FamilyMember(
        UserId userId,
        UserId addedByUserId,
        FamilyRelationshipType relationshipType,
        FamilyRole role)
        : base(userId)
    {
        AddedByUserId = addedByUserId;
        RelationshipType = relationshipType;
        Role = role;
        JoinedOnUtc = DateTime.UtcNow;
    }

    public UserId AddedByUserId { get; private set; }

    public FamilyRelationshipType RelationshipType { get; private set; }

    public FamilyRole Role { get; private set; }

    public DateTime JoinedOnUtc { get; private set; }

    public static FamilyMember Create(
        UserId userId,
        UserId addedByUserId,
        FamilyRelationshipType relationshipType,
        FamilyRole role)
    {
        return new FamilyMember(
            userId,
            addedByUserId,
            relationshipType,
            role);
    }

    public void ChangeRole(FamilyRole role)
    {
        Role = role;
    }
}