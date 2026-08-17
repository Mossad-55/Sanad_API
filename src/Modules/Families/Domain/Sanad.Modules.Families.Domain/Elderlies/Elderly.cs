using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Domain.Elderlies.Events;

namespace Sanad.Modules.Families.Domain.Elderlies;

public sealed class Elderly : AggregateRoot<ElderlyId>
{
    private Elderly(
        ElderlyId id,
        UserId ownerUserId,
        FamilyId familyId,
        FullName fullName,
        Gender gender,
        DateOnly dateOfBirth,
        string? profileImageUrl)
        : base(id)
    {
        OwnerUserId = ownerUserId;
        FamilyId = familyId;
        FullName = fullName;
        Gender = gender;
        DateOfBirth = dateOfBirth;
        ProfileImageUrl = profileImageUrl;

        CreatedOnUtc = DateTime.UtcNow;
        UpdatedOnUtc = DateTime.UtcNow;

        RaiseDomainEvent(new ElderlyCreatedDomainEvent(Id));
    }

    private Elderly()
    {
    }

    public UserId OwnerUserId { get; private set; }
    public FamilyId FamilyId { get; private set; }

    public FullName FullName { get; private set; } = default!;

    public Gender Gender { get; private set; }

    public DateOnly DateOfBirth { get; private set; }

    public string? ProfileImageUrl { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    public static Elderly Create(
        UserId ownerUserId,
        FamilyId familyId,
        FullName fullName,
        Gender gender,
        DateOnly dateOfBirth,
        string? profileImageUrl = null)
    {
        if(dateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            throw new DomainException("Date of birth cannot be in the future.");
        }

        return new Elderly(
            ElderlyId.New(),
            ownerUserId,
            familyId,
            fullName,
            gender,
            dateOfBirth,
            profileImageUrl);
    }

    public void UpdateProfile(
        FullName fullName,
        Gender gender,
        DateOnly dateOfBirth,
        string? profileImageUrl)
    {
        FullName = fullName;
        Gender = gender;
        DateOfBirth = dateOfBirth;
        ProfileImageUrl = profileImageUrl;

        UpdatedOnUtc = DateTime.UtcNow;
    }
}