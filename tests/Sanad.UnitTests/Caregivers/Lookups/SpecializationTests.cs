using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.UnitTests.Caregivers.Lookups;

public sealed class SpecializationTests
{
    [Theory]
    [InlineData(CaregiverType.Medical)]
    [InlineData(CaregiverType.Companion)]
    public void Create_ShouldCreateActiveSpecialization(
        CaregiverType caregiverType)
    {
        Specialization specialization =
            Specialization.Create(
                "رعاية كبار السن",
                "Elderly Care",
                true,
                caregiverType);

        Assert.NotEqual(
            SpecializationId.Empty,
            specialization.Id);

        Assert.Equal(
            "رعاية كبار السن",
            specialization.ArabicName);

        Assert.Equal(
            "Elderly Care",
            specialization.EnglishName);

        Assert.Equal(
            caregiverType,
            specialization.CaregiverType);

        Assert.True(specialization.IsActive);

        Assert.Equal(
            specialization.CreatedOnUtc,
            specialization.UpdatedOnUtc);
    }

    [Fact]
    public void Create_ShouldTrimNames()
    {
        Specialization specialization =
            Specialization.Create(
                "  رعاية كبار السن  ",
                "  Elderly Care  ",
                true,
                CaregiverType.Companion);

        Assert.Equal(
            "رعاية كبار السن",
            specialization.ArabicName);

        Assert.Equal(
            "Elderly Care",
            specialization.EnglishName);
    }

    [Theory]
    [InlineData(null, "Elderly Care")]
    [InlineData("", "Elderly Care")]
    [InlineData("   ", "Elderly Care")]
    [InlineData("رعاية كبار السن", null)]
    [InlineData("رعاية كبار السن", "")]
    [InlineData("رعاية كبار السن", "   ")]
    public void Create_ShouldRejectMissingName(
        string? arabicName,
        string? englishName)
    {
        Assert.Throws<DomainException>(
            () => Specialization.Create(
                arabicName!,
                englishName!,
                true,
                CaregiverType.Companion));
    }

    [Fact]
    public void Create_ShouldRejectNameThatIsTooLong()
    {
        string longName = new(
            'A',
            Specialization.MaximumNameLength + 1);

        Assert.Throws<DomainException>(
            () => Specialization.Create(
                "رعاية كبار السن",
                longName,
                false,
                CaregiverType.Companion));
    }

    [Fact]
    public void Create_ShouldRejectInvalidCaregiverType()
    {
        Assert.Throws<DomainException>(
            () => Specialization.Create(
                "رعاية كبار السن",
                "Elderly Care",
                true,
                (CaregiverType)999));
    }

    [Fact]
    public void UpdateNames_ShouldPreserveCaregiverType()
    {
        Specialization specialization =
            Specialization.Create(
                "رعاية كبار السن",
                "Elderly Care",
                true,
                CaregiverType.Medical);

        specialization.UpdateNames(
            "تمريض كبار السن",
            "Elderly Nursing");

        Assert.Equal(
            "تمريض كبار السن",
            specialization.ArabicName);

        Assert.Equal(
            "Elderly Nursing",
            specialization.EnglishName);

        Assert.Equal(
            CaregiverType.Medical,
            specialization.CaregiverType);
    }

    [Fact]
    public void UpdateNames_ShouldBeAtomic()
    {
        Specialization specialization =
            Specialization.Create(
                "رعاية كبار السن",
                "Elderly Care",
                true,
                CaregiverType.Companion);

        DateTime originalUpdatedOnUtc =
            specialization.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => specialization.UpdateNames(
                "دعم الحركة",
                ""));

        Assert.Equal(
            "رعاية كبار السن",
            specialization.ArabicName);

        Assert.Equal(
            "Elderly Care",
            specialization.EnglishName);

        Assert.Equal(
            originalUpdatedOnUtc,
            specialization.UpdatedOnUtc);
    }

    [Fact]
    public void ActivateAndDeactivate_ShouldBeIdempotent()
    {
        Specialization specialization =
            Specialization.Create(
                "رعاية كبار السن",
                "Elderly Care",
                false,
                CaregiverType.Companion);

        specialization.Deactivate();

        Assert.False(specialization.IsActive);

        DateTime deactivatedOnUtc =
            specialization.UpdatedOnUtc;

        specialization.Deactivate();

        Assert.Equal(
            deactivatedOnUtc,
            specialization.UpdatedOnUtc);

        specialization.Activate();

        Assert.True(specialization.IsActive);

        DateTime activatedOnUtc =
            specialization.UpdatedOnUtc;

        specialization.Activate();

        Assert.Equal(
            activatedOnUtc,
            specialization.UpdatedOnUtc);
    }
}