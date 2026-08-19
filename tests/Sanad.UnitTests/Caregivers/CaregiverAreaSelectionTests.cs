using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverAreaSelectionTests
{
    [Theory]
    [InlineData(CaregiverType.Medical)]
    [InlineData(CaregiverType.Companion)]
    public void SelectArea_ShouldAddActiveArea(
        CaregiverType caregiverType)
    {
        Caregiver caregiver =
            CreateCaregiver(caregiverType);

        Area area = CreateArea();

        caregiver.SelectArea(area);

        var selection =
            Assert.Single(
                caregiver.AreaSelections);

        Assert.Equal(
            area.Id,
            selection.Id);

        Assert.True(
            caregiver.UpdatedOnUtc >=
            caregiver.CreatedOnUtc);
    }

    [Fact]
    public void SelectArea_ShouldAllowMultipleDifferentAreas()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        Area firstArea = CreateArea();
        Area secondArea = CreateArea();

        caregiver.SelectArea(firstArea);
        caregiver.SelectArea(secondArea);

        Assert.Equal(
            2,
            caregiver.AreaSelections.Count);

        Assert.Contains(
            caregiver.AreaSelections,
            selection =>
                selection.Id == firstArea.Id);

        Assert.Contains(
            caregiver.AreaSelections,
            selection =>
                selection.Id == secondArea.Id);
    }

    [Fact]
    public void SelectArea_ShouldRejectInactiveArea()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        Area area = CreateArea();
        area.Deactivate();

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.SelectArea(
                area));

        Assert.Empty(
            caregiver.AreaSelections);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void SelectArea_ShouldRejectDuplicateArea()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        Area area = CreateArea();

        caregiver.SelectArea(area);

        DateTime updatedOnUtcAfterSelection =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.SelectArea(
                area));

        var selection =
            Assert.Single(
                caregiver.AreaSelections);

        Assert.Equal(
            area.Id,
            selection.Id);

        Assert.Equal(
            updatedOnUtcAfterSelection,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void SelectArea_ShouldRejectNullArea()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<ArgumentNullException>(
            () => caregiver.SelectArea(
                null!));

        Assert.Empty(
            caregiver.AreaSelections);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void SelectArea_ShouldAllowTenAreas()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        for (int areaNumber = 1;
             areaNumber <=
             Caregiver.MaximumAreaSelections;
             areaNumber++)
        {
            caregiver.SelectArea(
                CreateArea());
        }

        Assert.Equal(
            Caregiver.MaximumAreaSelections,
            caregiver.AreaSelections.Count);
    }

    [Fact]
    public void SelectArea_ShouldRejectEleventhArea()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        for (int areaNumber = 1;
             areaNumber <=
             Caregiver.MaximumAreaSelections;
             areaNumber++)
        {
            caregiver.SelectArea(
                CreateArea());
        }

        DateTime updatedOnUtcAtMaximum =
            caregiver.UpdatedOnUtc;

        Area eleventhArea = CreateArea();

        Assert.Throws<DomainException>(
            () => caregiver.SelectArea(
                eleventhArea));

        Assert.Equal(
            Caregiver.MaximumAreaSelections,
            caregiver.AreaSelections.Count);

        Assert.DoesNotContain(
            caregiver.AreaSelections,
            selection =>
                selection.Id == eleventhArea.Id);

        Assert.Equal(
            updatedOnUtcAtMaximum,
            caregiver.UpdatedOnUtc);
    }

    private static Caregiver CreateCaregiver(
        CaregiverType caregiverType)
    {
        return Caregiver.Create(
            UserId.New(),
            caregiverType);
    }

    private static Area CreateArea()
    {
        return Area.Create(
            CityId.New(),
            "منطقة خدمة",
            "Service Area");
    }
}