using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverServiceSelectionTests
{
    [Theory]
    [InlineData(CaregiverType.Medical)]
    [InlineData(CaregiverType.Companion)]
    public void SelectService_ShouldAddMatchingActiveService(
        CaregiverType caregiverType)
    {
        Caregiver caregiver =
            CreateCaregiver(caregiverType);

        Service service =
            CreateService(
                caregiverType,
                isActive: true);

        caregiver.SelectService(service);

        var selection =
            Assert.Single(
                caregiver.ServiceSelections);

        Assert.Equal(
            service.Id,
            selection.Id);

        Assert.True(
            caregiver.UpdatedOnUtc >=
            caregiver.CreatedOnUtc);
    }

    [Fact]
    public void SelectService_ShouldAllowMultipleDifferentServices()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        Service firstService =
            CreateService(
                CaregiverType.Medical,
                isActive: true);

        Service secondService =
            CreateService(
                CaregiverType.Medical,
                isActive: true);

        caregiver.SelectService(firstService);
        caregiver.SelectService(secondService);

        Assert.Equal(
            2,
            caregiver.ServiceSelections.Count);

        Assert.Contains(
            caregiver.ServiceSelections,
            selection =>
                selection.Id == firstService.Id);

        Assert.Contains(
            caregiver.ServiceSelections,
            selection =>
                selection.Id == secondService.Id);
    }

    [Fact]
    public void SelectService_ShouldRejectInactiveService()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        Service service =
            CreateService(
                CaregiverType.Medical,
                isActive: false);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.SelectService(
                service));

        Assert.Empty(
            caregiver.ServiceSelections);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Theory]
    [InlineData(
        CaregiverType.Medical,
        CaregiverType.Companion)]
    [InlineData(
        CaregiverType.Companion,
        CaregiverType.Medical)]
    public void SelectService_ShouldRejectMismatchedCaregiverType(
        CaregiverType caregiverType,
        CaregiverType serviceType)
    {
        Caregiver caregiver =
            CreateCaregiver(caregiverType);

        Service service =
            CreateService(
                serviceType,
                isActive: true);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.SelectService(
                service));

        Assert.Empty(
            caregiver.ServiceSelections);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void SelectService_ShouldRejectDuplicateService()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        Service service =
            CreateService(
                CaregiverType.Medical,
                isActive: true);

        caregiver.SelectService(service);

        DateTime updatedOnUtcAfterSelection =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.SelectService(
                service));

        var selection =
            Assert.Single(
                caregiver.ServiceSelections);

        Assert.Equal(
            service.Id,
            selection.Id);

        Assert.Equal(
            updatedOnUtcAfterSelection,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void SelectService_ShouldRejectNullService()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<ArgumentNullException>(
            () => caregiver.SelectService(
                null!));

        Assert.Empty(
            caregiver.ServiceSelections);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void RemoveService_ShouldRemoveFinalServiceDuringOnboarding()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        Service service =
            CreateService(
                CaregiverType.Medical,
                isActive: true);

        caregiver.SelectService(service);

        caregiver.RemoveService(service.Id);

        Assert.Empty(
            caregiver.ServiceSelections);

        Assert.Equal(
            CaregiverStatus.Onboarding,
            caregiver.Status);

        Assert.True(
            caregiver.UpdatedOnUtc >=
            caregiver.CreatedOnUtc);
    }

    [Fact]
    public void RemoveService_ShouldAllowActiveCaregiverToKeepOneService()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        Service firstService =
            CreateService(
                CaregiverType.Medical,
                isActive: true);

        Service secondService =
            CreateService(
                CaregiverType.Medical,
                isActive: true);

        caregiver.SelectService(firstService);
        caregiver.SelectService(secondService);
        caregiver.TransitionToActive();

        caregiver.RemoveService(firstService.Id);

        var remainingSelection =
            Assert.Single(
                caregiver.ServiceSelections);

        Assert.Equal(
            secondService.Id,
            remainingSelection.Id);

        Assert.Equal(
            CaregiverStatus.Active,
            caregiver.Status);
    }

    [Fact]
    public void RemoveService_ShouldRejectFinalServiceForActiveCaregiver()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        Service service =
            CreateService(
                CaregiverType.Medical,
                isActive: true);

        caregiver.SelectService(service);
        caregiver.TransitionToActive();

        DateTime updatedOnUtcBeforeRemoval =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.RemoveService(
                service.Id));

        var selection =
            Assert.Single(
                caregiver.ServiceSelections);

        Assert.Equal(
            service.Id,
            selection.Id);

        Assert.Equal(
            CaregiverStatus.Active,
            caregiver.Status);

        Assert.Equal(
            updatedOnUtcBeforeRemoval,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void RemoveService_ShouldRejectUnselectedService()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        Service selectedService =
            CreateService(
                CaregiverType.Medical,
                isActive: true);

        Service unselectedService =
            CreateService(
                CaregiverType.Medical,
                isActive: true);

        caregiver.SelectService(selectedService);

        DateTime updatedOnUtcBeforeRemoval =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.RemoveService(
                unselectedService.Id));

        var selection =
            Assert.Single(
                caregiver.ServiceSelections);

        Assert.Equal(
            selectedService.Id,
            selection.Id);

        Assert.Equal(
            updatedOnUtcBeforeRemoval,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void RemoveService_ShouldRejectEmptyServiceId()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.RemoveService(
                ServiceId.Empty));

        Assert.Empty(
            caregiver.ServiceSelections);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void RemoveService_ShouldAllowSuspendedCaregiverToRemoveFinalService()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        Service service =
            CreateService(
                CaregiverType.Medical,
                isActive: true);

        caregiver.SelectService(service);
        caregiver.TransitionToActive();
        caregiver.TransitionToSuspended();

        caregiver.RemoveService(service.Id);

        Assert.Empty(
            caregiver.ServiceSelections);

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

    private static Service CreateService(
        CaregiverType caregiverType,
        bool isActive)
    {
        return Service.Create(
            "خدمة رعاية",
            "Care Service",
            "icons/care-service.svg",
            caregiverType,
            isActive);
    }
}