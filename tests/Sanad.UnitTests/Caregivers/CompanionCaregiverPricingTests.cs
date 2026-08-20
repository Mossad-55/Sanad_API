using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class CompanionCaregiverPricingTests
{
    [Fact]
    public void Create_ShouldStoreAllCompanionPrices()
    {
        CompanionCaregiverPricing pricing =
            CompanionCaregiverPricing.Create(
                hourlyPrice: 75.00m,
                eightHourDayPrice: 500.50m,
                overnightPrice: 650.00m);

        Assert.Equal(
            75.00m,
            pricing.HourlyPrice);

        Assert.Equal(
            500.50m,
            pricing.EightHourDayPrice);

        Assert.Equal(
            650.00m,
            pricing.OvernightPrice);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Create_ShouldRejectNonPositiveRequiredPrice(
        int pricePosition)
    {
        decimal hourly = 75m;
        decimal day = 500m;
        decimal overnight = 650m;

        switch (pricePosition)
        {
            case 1:
                hourly = 0;
                break;

            case 2:
                day = 0;
                break;

            case 3:
                overnight = 0;
                break;
        }

        Assert.Throws<DomainException>(
            () => CompanionCaregiverPricing.Create(
                hourly,
                day,
                overnight));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Create_ShouldRejectPriceWithMoreThanTwoDecimals(
        int pricePosition)
    {
        decimal hourly = 75m;
        decimal day = 500m;
        decimal overnight = 650m;

        switch (pricePosition)
        {
            case 1:
                hourly = 75.001m;
                break;

            case 2:
                day = 500.001m;
                break;

            case 3:
                overnight = 650.001m;
                break;
        }

        Assert.Throws<DomainException>(
            () => CompanionCaregiverPricing.Create(
                hourly,
                day,
                overnight));
    }

    [Fact]
    public void Create_ShouldAcceptTwoDecimalPlaces()
    {
        CompanionCaregiverPricing pricing =
            CompanionCaregiverPricing.Create(
                75.99m,
                500.50m,
                650.25m);

        Assert.Equal(
            75.99m,
            pricing.HourlyPrice);

        Assert.Equal(
            500.50m,
            pricing.EightHourDayPrice);

        Assert.Equal(
            650.25m,
            pricing.OvernightPrice);
    }
}