using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class MedicalCaregiverPricingTests
{
    [Fact]
    public void Create_ShouldStoreAllMedicalPrices()
    {
        MedicalCaregiverPricing pricing =
            MedicalCaregiverPricing.Create(
                homeVisitPrice: 200.00m,
                eightHourShiftPrice: 600.00m,
                twelveHourShiftPrice: 850.50m,
                twentyFourHourShiftPrice: 1500.00m);

        Assert.Equal(
            200.00m,
            pricing.HomeVisitPrice);

        Assert.Equal(
            600.00m,
            pricing.EightHourShiftPrice);

        Assert.Equal(
            850.50m,
            pricing.TwelveHourShiftPrice);

        Assert.Equal(
            1500.00m,
            pricing.TwentyFourHourShiftPrice);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_ShouldRejectNonPositiveHomeVisitPrice(
        int invalidPrice)
    {
        Assert.Throws<DomainException>(
            () => MedicalCaregiverPricing.Create(
                invalidPrice,
                600m,
                850m,
                1500m));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Create_ShouldRejectNonPositiveRequiredPrice(
        int pricePosition)
    {
        decimal homeVisit = 200m;
        decimal eightHours = 600m;
        decimal twelveHours = 850m;
        decimal twentyFourHours = 1500m;

        switch (pricePosition)
        {
            case 1:
                homeVisit = 0;
                break;

            case 2:
                eightHours = 0;
                break;

            case 3:
                twelveHours = 0;
                break;

            case 4:
                twentyFourHours = 0;
                break;
        }

        Assert.Throws<DomainException>(
            () => MedicalCaregiverPricing.Create(
                homeVisit,
                eightHours,
                twelveHours,
                twentyFourHours));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Create_ShouldRejectPriceWithMoreThanTwoDecimals(
        int pricePosition)
    {
        decimal homeVisit = 200m;
        decimal eightHours = 600m;
        decimal twelveHours = 850m;
        decimal twentyFourHours = 1500m;

        switch (pricePosition)
        {
            case 1:
                homeVisit = 200.001m;
                break;

            case 2:
                eightHours = 600.001m;
                break;

            case 3:
                twelveHours = 850.001m;
                break;

            case 4:
                twentyFourHours = 1500.001m;
                break;
        }

        Assert.Throws<DomainException>(
            () => MedicalCaregiverPricing.Create(
                homeVisit,
                eightHours,
                twelveHours,
                twentyFourHours));
    }

    [Fact]
    public void Create_ShouldAcceptTwoDecimalPlaces()
    {
        MedicalCaregiverPricing pricing =
            MedicalCaregiverPricing.Create(
                200.99m,
                600.50m,
                850.25m,
                1500.75m);

        Assert.Equal(
            200.99m,
            pricing.HomeVisitPrice);

        Assert.Equal(
            600.50m,
            pricing.EightHourShiftPrice);

        Assert.Equal(
            850.25m,
            pricing.TwelveHourShiftPrice);

        Assert.Equal(
            1500.75m,
            pricing.TwentyFourHourShiftPrice);
    }
}