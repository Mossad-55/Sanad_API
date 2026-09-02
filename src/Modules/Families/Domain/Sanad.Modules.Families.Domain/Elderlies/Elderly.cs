using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Domain.Elderlies.Events;
using Sanad.Modules.Families.Domain.Elderlies.Medical;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.Modules.Families.Domain.Elderlies;

public sealed class Elderly : AggregateRoot<ElderlyId>
{
    public const int MaximumDetailedAddressLength = 500;
    public const int MaximumHealthNotesLength = 2000;
    public const int MaximumProfileImageKeyLength = 500;

    private Elderly()
    {
    }

    private Elderly(
        ElderlyId id,
        UserId ownerUserId,
        UserId identityUserId,
        FamilyId familyId,
        FamilyRelationshipType relationshipType,
        FullName arabicFullName,
        FullName englishFullName,
        Gender gender,
        DateOnly dateOfBirth,
        string? profileImageKey,
        string? detailedAddress,
        string? healthNotes)
        : base(id)
    {
        OwnerUserId = ownerUserId;
        IdentityUserId = identityUserId;
        FamilyId = familyId;
        RelationshipType = relationshipType;
        ArabicFullName = arabicFullName;
        EnglishFullName = englishFullName;
        Gender = gender;
        DateOfBirth = dateOfBirth;
        ProfileImageKey = profileImageKey;
        DetailedAddress = detailedAddress;
        HealthNotes = healthNotes;

        CreatedOnUtc = DateTime.UtcNow;
        UpdatedOnUtc = DateTime.UtcNow;

        RaiseDomainEvent(new ElderlyCreatedDomainEvent(Id));
    }

    public UserId OwnerUserId { get; private set; }
    public UserId IdentityUserId { get; private set; }
    public FamilyId FamilyId { get; private set; }
    public FamilyRelationshipType RelationshipType { get; private set; }
    public FullName ArabicFullName { get; private set; } = default!;
    public FullName EnglishFullName { get; private set; } = default!;
    public Gender Gender { get; private set; }
    public DateOnly DateOfBirth { get; private set; }
    public string? ProfileImageKey { get; private set; }
    public string? DetailedAddress { get; private set; }
    public string? HealthNotes { get; private set; }
    public ElderlyMedicalProfile? MedicalProfile { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }

    public static Elderly Create(
        UserId ownerUserId,
        UserId identityUserId,
        FamilyId familyId,
        FamilyRelationshipType relationshipType,
        FullName arabicFullName,
        FullName englishFullName,
        Gender gender,
        DateOnly dateOfBirth,
        DateOnly currentDate,
        string? profileImageKey = null,
        string? detailedAddress = null,
        string? healthNotes = null)
    {
        if (identityUserId == UserId.Empty)
        {
            throw new DomainException(
                "Elderly identity user is required.");
        }

        if (!Enum.IsDefined(relationshipType))
        {
            throw new DomainException("Relationship type is invalid.");
        }

        if (!Enum.IsDefined(gender))
        {
            throw new DomainException("Gender is invalid.");
        }

        if (dateOfBirth > currentDate)
        {
            throw new DomainException(
                "Date of birth cannot be in the future.");
        }

        return new Elderly(
            ElderlyId.New(),
            ownerUserId,
            identityUserId,
            familyId,
            relationshipType,
            arabicFullName,
            englishFullName,
            gender,
            dateOfBirth,
            NormalizeOptional(
                profileImageKey,
                MaximumProfileImageKeyLength,
                "Profile image"),
            NormalizeOptional(
                detailedAddress,
                MaximumDetailedAddressLength,
                "Detailed address"),
            NormalizeOptional(
                healthNotes,
                MaximumHealthNotesLength,
                "Health notes"));
    }

    public void UpdateProfile(
        FamilyRelationshipType relationshipType,
        FullName arabicFullName,
        FullName englishFullName,
        Gender gender,
        DateOnly dateOfBirth,
        DateOnly currentDate,
        string? detailedAddress,
        string? healthNotes)
    {
        if (!Enum.IsDefined(relationshipType))
        {
            throw new DomainException("Relationship type is invalid");
        }

        if (!Enum.IsDefined(gender))
        {
            throw new DomainException("Gender is invalid.");
        }

        if (dateOfBirth > currentDate)
        {
            throw new DomainException(
                "Date of birth cannot be in the future.");
        }

        RelationshipType = relationshipType;
        ArabicFullName = arabicFullName;
        EnglishFullName = englishFullName;
        Gender = gender;
        DateOfBirth = dateOfBirth;

        DetailedAddress = NormalizeOptional(
            detailedAddress,
            MaximumDetailedAddressLength,
            "Detailed address");

        HealthNotes = NormalizeOptional(
            healthNotes,
            MaximumHealthNotesLength,
            "Health notes");

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void ChangePhoto(string? photoKey)
    {
        ProfileImageKey = NormalizeOptional(
            photoKey,
            MaximumProfileImageKeyLength,
            "Profile image");

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void UpdateMedicalProfile(
        ElderlyMedicalProfile medicalProfile)
    {
        ArgumentNullException.ThrowIfNull(medicalProfile);

        MedicalProfile = medicalProfile;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    private static string? NormalizeOptional(
        string? value,
        int maxLength,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();

        if (normalized.Length > maxLength)
        {
            throw new DomainException(
                $"{fieldName} cannot exceed {maxLength} characters.");
        }

        return normalized;
    }
}