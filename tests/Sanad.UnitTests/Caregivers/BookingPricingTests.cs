using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Caregivers.Application.Discovery;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Xunit;

namespace Sanad.UnitTests.Caregivers;

public sealed class BookingPricingTests
{
    [Fact]
    public void CalculatePrice_MedicalHomeVisit_CalculatesAccuratePlatformFee()
    {
        Caregiver caregiver = Caregiver.Create(UserId.New(), CaregiverType.Medical);
        caregiver.UpdateMedicalPricing(200m, 800m, 1200m, 2000m);

        BookingPriceSnapshot snapshot = BookingPricingService.CalculatePrice(
            caregiver,
            BookingShiftType.HomeVisit,
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            15.00m);

        Assert.Equal(200.00m, snapshot.BaseCaregiverFee);
        Assert.Equal(15.00m, snapshot.PlatformFeePercentage);
        Assert.Equal(30.00m, snapshot.PlatformFeeAmount);
        Assert.Equal(230.00m, snapshot.TotalPayableAmount);
        Assert.Equal("EGP", snapshot.Currency);
    }

    [Fact]
    public void CalculatePrice_CompanionHourly_CalculatesExactHoursAndFee()
    {
        Caregiver caregiver = Caregiver.Create(UserId.New(), CaregiverType.Companion);
        caregiver.UpdateCompanionPricing(50m, 350m, 500m);

        // 3 hours duration (10:00 to 13:00) -> 3 * 50 = 150 EGP
        BookingPriceSnapshot snapshot = BookingPricingService.CalculatePrice(
            caregiver,
            BookingShiftType.Hourly,
            new TimeOnly(10, 0),
            new TimeOnly(13, 0),
            15.00m);

        Assert.Equal(150.00m, snapshot.BaseCaregiverFee);
        Assert.Equal(15.00m, snapshot.PlatformFeePercentage);
        Assert.Equal(22.50m, snapshot.PlatformFeeAmount);
        Assert.Equal(172.50m, snapshot.TotalPayableAmount);
    }
}