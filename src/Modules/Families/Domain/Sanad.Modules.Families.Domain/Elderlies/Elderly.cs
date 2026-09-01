using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Domain.Elderlies.Events;
using Sanad.BuildingBlocks.Domain.Enums;

namespace Sanad.Modules.Families.Domain.Elderlies;

public sealed class Elderly : AggregateRoot<ElderlyId>
{
    public const int MaximumDetailedAddressLength = 500;
    public const int MaximumHealthNotesLength = 2000;
    public const int MaximumProfileImageKeyLength = 500;

    private Elderly(
        ElderlyId id,
        UserId ownerUserId,
        UserId identityUserId,
        FamilyId familyId,
        FullName arabicFullName,
        FullName englishFullName,
        Gender gender,
        DateOnly dateOfBirth,
        string? profileImageUrl,
        string? detailedAddress,
        string? healthNotes)
        : base(id)
    {
        OwnerUserId = ownerUserId;
        IdentityUserId = identityUserId;
        FamilyId = familyId;
        ArabicFullName = arabicFullName;
        EnglishFullName = englishFullName;
        Gender = gender;
        DateOfBirth = dateOfBirth;
        ProfileImageUrl = profileImageUrl;
        DetailedAddress = detailedAddress;
        HealthNotes = healthNotes;

        CreatedOnUtc = DateTime.UtcNow;
        UpdatedOnUtc = DateTime.UtcNow;

        RaiseDomainEvent(new ElderlyCreatedDomainEvent(Id));
    }

    private Elderly()
    {
    }

    public UserId OwnerUserId { get; private set; }
    public FamilyId FamilyId { get; private set; }
    public UserId IdentityUserId { get; private set; }
    public FullName ArabicFullName { get; private set; } = default!;
    public FullName EnglishFullName { get; private set; } = default!;
    public Gender Gender { get; private set; }
    public DateOnly DateOfBirth { get; private set; }
    public string? ProfileImageUrl { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    public string? DetailedAddress { get; private set; }
    public string? HealthNotes { get; private set; }

    public static Elderly Create(
        UserId ownerUserId,
        UserId identityUserId,
        FamilyId familyId,
        FullName arabicFullName,
        FullName englishFullName,
        Gender gender,
        DateOnly dateOfBirth,
        string? profileImageUrl = null,
        string? detailedAddress = null,
        string? healthNotes = null)
    {
        if (identityUserId == UserId.Empty)
        {
            throw new DomainException(
                "Eldery identity user is required.");
        }

        if (dateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            throw new DomainException("Date of birth cannot be in the future.");
        }

        return new Elderly(
            ElderlyId.New(),
            ownerUserId,
            identityUserId,
            familyId,
            arabicFullName,
            englishFullName,
            gender,
            dateOfBirth,
            profileImageUrl,
            NormalizeOptional(detailedAddress, MaximumDetailedAddressLength, "Detailed address"),
            NormalizeOptional(healthNotes, MaximumHealthNotesLength, "Health notes"));
    }

    public void UpdateProfile(
        FullName arabicFullName,
        FullName englishFullName,
        Gender gender,
        DateOnly dateOfBirth,
        string? profileImageUrl,
        string? detailedAddress,
        string? healthNotes)
    {
        if (dateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            throw new DomainException(
                "Date of birth cannot be in the future.");
        }
        ArabicFullName = arabicFullName;
        EnglishFullName = englishFullName;
        Gender = gender;
        DateOfBirth = dateOfBirth;
        ProfileImageUrl = profileImageUrl;

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