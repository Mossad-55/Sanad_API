using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverDetailedAddressTests
{
    [Theory]
    [InlineData(CaregiverType.Medical)]
    [InlineData(CaregiverType.Companion)]
    public void UpdateDetailedAddress_ShouldStoreTrimmedAddress(
        CaregiverType caregiverType)
    {
        Caregiver caregiver =
            CreateCaregiver(caregiverType);

        caregiver.UpdateDetailedAddress(
            "  15 Al-Nasr Street, Damanhur  ");

        Assert.Equal(
            "15 Al-Nasr Street, Damanhur",
            caregiver.DetailedAddress);

        Assert.True(
            caregiver.UpdatedOnUtc >=
            caregiver.CreatedOnUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateDetailedAddress_ShouldClearOptionalAddress(
        string? address)
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        caregiver.UpdateDetailedAddress(
            "15 Al-Nasr Street");

        caregiver.UpdateDetailedAddress(
            address);

        Assert.Null(caregiver.DetailedAddress);
    }

    [Fact]
    public void UpdateDetailedAddress_ShouldRejectLongAddressWithoutMutation()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        caregiver.UpdateDetailedAddress(
            "Original address");

        string originalAddress =
            caregiver.DetailedAddress!;

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        string longAddress = new(
            'A',
            Caregiver.MaximumDetailedAddressLength + 1);

        Assert.Throws<DomainException>(
            () => caregiver.UpdateDetailedAddress(
                longAddress));

        Assert.Equal(
            originalAddress,
            caregiver.DetailedAddress);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void UpdateDetailedAddress_ShouldKeepActiveCompanionAvailable()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        caregiver.TransitionToActive();

        caregiver.BecomeAvailable(
            CreateCurrentDate());

        caregiver.UpdateDetailedAddress(
            "15 Al-Nasr Street, Damanhur");

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);

        Assert.Equal(
            "15 Al-Nasr Street, Damanhur",
            caregiver.DetailedAddress);
    }

    private static Caregiver CreateCaregiver(
        CaregiverType caregiverType)
    {
        return Caregiver.Create(
            UserId.New(),
            caregiverType);
    }

    private static DateOnly CreateCurrentDate()
    {
        return new DateOnly(
            2026,
            8,
            20);
    }
}