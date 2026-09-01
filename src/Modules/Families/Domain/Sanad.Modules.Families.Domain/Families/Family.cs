using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Families.Events;

namespace Sanad.Modules.Families.Domain.Families;

public sealed class Family : AggregateRoot<FamilyId>
{
    public const int MaximumNameLength = 100;

    private readonly List<FamilyMember> _members = [];

    private Family()
    {
    }

    private Family(
        FamilyId id,
        UserId ownerUserId,
        string name)
        : base(id)
    {
        OwnerUserId = ownerUserId;
        Name = name;

        CreatedOnUtc = DateTime.UtcNow;
        UpdatedOnUtc = DateTime.UtcNow;

        _members.Add(
            FamilyMember.Create(
                ownerUserId,
                ownerUserId,
                FamilyRelationshipType.Other,
                FamilyRole.Owner));

        RaiseDomainEvent(
            new FamilyCreatedDomainEvent(
                Id,
                OwnerUserId));
    }

    public string Name { get; private set; } = string.Empty;

    public UserId OwnerUserId { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    public IReadOnlyCollection<FamilyMember> Members => _members.AsReadOnly();

    public static Family Create(
        UserId ownerUserId,
        string? familyName = null)
    {
        var name = string.IsNullOrWhiteSpace(familyName)
            ? "My Family"
            : familyName.Trim();

        return new Family(
            FamilyId.New(),
            ownerUserId,
            name);
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Family name cannot be empty.");
        }

        string normalized = name.Trim();

        if (normalized.Length > MaximumNameLength)
        {
            throw new DomainException(
                $"Family name cannot exceed {MaximumNameLength} characters.");
        }

        Name = normalized;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void AddMember(FamilyMember member)
    {
        if (_members.Any(x => x.Id == member.Id))
        {
            throw new DomainException("Member already exists.");
        }

        _members.Add(member);
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void RemoveMember(UserId userId)
    {
        if (userId == OwnerUserId)
        {
            throw new DomainException("Cannot remove family owner.");
        }

        var member = _members.FirstOrDefault(x => x.Id == userId);

        if (member is null)
        {
            return;
        }

        _members.Remove(member);
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public FamilyRole? GetRole(UserId userId)
    {
        if (OwnerUserId == userId)
        {
            return FamilyRole.Owner;
        }

        return _members
            .FirstOrDefault(member => member.Id == userId)
            ?.Role;
    }
}