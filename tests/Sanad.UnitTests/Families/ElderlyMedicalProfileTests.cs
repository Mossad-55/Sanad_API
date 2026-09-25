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
    public void MedicalHistory_ShouldDeriveYearFromFullProcedureDate()
    {
        var procedureDate = new DateOnly(2021, 6, 14);

        var entry = MedicalHistoryEntry.Create(
            null,
            "Hip Replacement",
            "Right hip arthroplasty",
            procedureDate);

        Assert.Equal(2021, entry.Year);
        Assert.Equal(procedureDate, entry.ProcedureDate);
    }

    [Fact]
    public void MedicalHistory_ShouldAcceptLegacyYearOnlyEntry()
    {
        var entry = MedicalHistoryEntry.Create(2018, "Gallbladder Removal");

        Assert.Equal(2018, entry.Year);
        Assert.Null(entry.ProcedureDate);
    }

    [Fact]
    public void MedicalHistory_ShouldAcceptMatchingYearAndProcedureDate()
    {
        var entry = MedicalHistoryEntry.Create(
            2020,
            "Knee Replacement",
            procedureDate: new DateOnly(2020, 1, 20));

        Assert.Equal(2020, entry.Year);
        Assert.Equal(new DateOnly(2020, 1, 20), entry.ProcedureDate);
    }

    [Fact]
    public void MedicalHistory_ShouldRejectMismatchingYearAndProcedureDate()
    {
        var exception = Assert.Throws<DomainException>(() => MedicalHistoryEntry.Create(
            2020,
            "Knee Replacement",
            procedureDate: new DateOnly(2021, 1, 20)));

        Assert.Contains("year must match", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Update_ShouldReplaceHistoryEntriesIncludingProcedureDate()
    {
        var profile = ElderlyMedicalProfile.Create(
            BloodType.OPositive,
            172,
            75.5m,
            medicalHistory: [MedicalHistoryEntry.Create(2018, "Old Procedure")]);

        profile.Update(
            BloodType.OPositive,
            172,
            75.5m,
            [],
            [],
            [MedicalHistoryEntry.Create(
                null,
                "New Procedure",
                procedureDate: new DateOnly(2022, 9, 3))]);

        var entry = Assert.Single(profile.MedicalHistory);
        Assert.Equal("New Procedure", entry.Title);
        Assert.Equal(2022, entry.Year);
        Assert.Equal(new DateOnly(2022, 9, 3), entry.ProcedureDate);
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
