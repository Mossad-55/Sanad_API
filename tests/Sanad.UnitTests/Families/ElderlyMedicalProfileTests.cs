using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Elderlies.Medical;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.UnitTests.Families;

public sealed class ElderlyMedicalProfileTests
{
    [Fact]
    public void Create_ShouldInstantiateMedicalProfile_WhenValid()
    {
        var profile = ElderlyMedicalProfile.Create(
            BloodType.OPositive,
            172,
            75.5m,
            ["Diabetes Type 2", "Hypertension"],
            [AllergyEntry.Create(AllergyCategory.Drug, "Penicillin", "Skin Rash")],
            [MedicalHistoryEntry.Create(2020, "Knee Replacement", "Left knee arthroplasty")]);

        Assert.Equal(BloodType.OPositive, profile.BloodType);
        Assert.Equal(172, profile.HeightCm);
        Assert.Equal(75.5m, profile.WeightKg);
        Assert.Equal(2, profile.ChronicConditions.Count);
        Assert.Single(profile.Allergies);
        Assert.Single(profile.MedicalHistory);
    }

    [Fact]
    public void Create_ShouldThrow_WhenHeightIsOutOfRange()
    {
        Assert.Throws<DomainException>(() => ElderlyMedicalProfile.Create(
            BloodType.APositive,
            40, // Below min 50
            70m));
    }

    [Fact]
    public void Create_ShouldThrow_WhenWeightIsOutOfRange()
    {
        Assert.Throws<DomainException>(() => ElderlyMedicalProfile.Create(
            BloodType.APositive,
            170,
            15m)); // Below min 20kg
    }

    [Fact]
    public void UpdateMedicalProfile_ShouldAttachProfileToElderly()
    {
        var elderly = Elderly.Create(
            UserId.New(),
            UserId.New(),
            FamilyId.New(),
            FamilyRelationshipType.Father,
            FullName.Create("أحمد علي"),
            FullName.Create("Ahmed Ali"),
            Gender.Male,
            new DateOnly(1950, 1, 1),
            new DateOnly(2026, 9, 2));

        var profile = ElderlyMedicalProfile.Create(
            BloodType.BPositive,
            165,
            68.0m,
            ["Hypertension"],
            [],
            []);

        elderly.UpdateMedicalProfile(profile);

        Assert.NotNull(elderly.MedicalProfile);
        Assert.Equal(BloodType.BPositive, elderly.MedicalProfile.BloodType);
    }
}