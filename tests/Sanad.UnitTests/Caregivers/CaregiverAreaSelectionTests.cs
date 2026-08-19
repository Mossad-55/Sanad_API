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

    [Fact]
    public void RemoveArea_ShouldRemoveFinalAreaDuringOnboarding()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        Area area = CreateArea();

        caregiver.SelectArea(area);

        caregiver.RemoveArea(area.Id);

        Assert.Empty(
            caregiver.AreaSelections);

        Assert.Equal(
            CaregiverStatus.PendingVerification,
            caregiver.Status);

        Assert.True(
            caregiver.UpdatedOnUtc >=
            caregiver.CreatedOnUtc);
    }

    [Fact]
    public void RemoveArea_ShouldAllowActiveCaregiverToKeepOneArea()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        Area firstArea = CreateArea();
        Area secondArea = CreateArea();

        caregiver.SelectArea(firstArea);
        caregiver.SelectArea(secondArea);
        caregiver.Activate();

        caregiver.RemoveArea(firstArea.Id);

        var remainingSelection =
            Assert.Single(
                caregiver.AreaSelections);

        Assert.Equal(
            secondArea.Id,
            remainingSelection.Id);

        Assert.Equal(
            CaregiverStatus.Active,
            caregiver.Status);
    }

    [Fact]
    public void RemoveArea_ShouldRejectFinalAreaForActiveCaregiver()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        Area area = CreateArea();

        caregiver.SelectArea(area);
        caregiver.Activate();

        DateTime updatedOnUtcBeforeRemoval =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.RemoveArea(
                area.Id));

        var selection =
            Assert.Single(
                caregiver.AreaSelections);

        Assert.Equal(
            area.Id,
            selection.Id);

        Assert.Equal(
            CaregiverStatus.Active,
            caregiver.Status);

        Assert.Equal(
            updatedOnUtcBeforeRemoval,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void RemoveArea_ShouldRejectUnselectedArea()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        Area selectedArea = CreateArea();
        Area unselectedArea = CreateArea();

        caregiver.SelectArea(selectedArea);

        DateTime updatedOnUtcBeforeRemoval =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.RemoveArea(
                unselectedArea.Id));

        var selection =
            Assert.Single(
                caregiver.AreaSelections);

        Assert.Equal(
            selectedArea.Id,
            selection.Id);

        Assert.Equal(
            updatedOnUtcBeforeRemoval,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void RemoveArea_ShouldRejectEmptyAreaId()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.RemoveArea(
                AreaId.Empty));

        Assert.Empty(
            caregiver.AreaSelections);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void RemoveArea_ShouldAllowSuspendedCaregiverToRemoveFinalArea()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        Area area = CreateArea();

        caregiver.SelectArea(area);
        caregiver.Activate();
        caregiver.Suspend();

        caregiver.RemoveArea(area.Id);

        Assert.Empty(
            caregiver.AreaSelections);

        Assert.Equal(
            CaregiverStatus.Suspended,
            caregiver.Status);
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