using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class CompanionCaregiverPricing :
    ValueObject
{
    private CompanionCaregiverPricing()
    {
    }

    private CompanionCaregiverPricing(
        decimal hourlyPrice,
        decimal eightHourDayPrice,
        decimal overnightPrice)
    {
        HourlyPrice = hourlyPrice;
        EightHourDayPrice = eightHourDayPrice;
        OvernightPrice = overnightPrice;
    }

    public decimal HourlyPrice { get; private set; }

    public decimal EightHourDayPrice
    {
        get;
        private set;
    }

    public decimal OvernightPrice
    {
        get;
        private set;
    }

    internal static CompanionCaregiverPricing Create(
        decimal hourlyPrice,
        decimal eightHourDayPrice,
        decimal overnightPrice)
    {
        ValidatePrice(
            hourlyPrice,
            "Hourly price");

        ValidatePrice(
            eightHourDayPrice,
            "8-hour day price");

        ValidatePrice(
            overnightPrice,
            "Overnight price");

        return new CompanionCaregiverPricing(
            hourlyPrice,
            eightHourDayPrice,
            overnightPrice);
    }

    protected override IEnumerable<object?>
        GetEqualityComponents()
    {
        yield return HourlyPrice;
        yield return EightHourDayPrice;
        yield return OvernightPrice;
    }

    private static void ValidatePrice(
        decimal price,
        string fieldName)
    {
        if (price <= 0)
        {
            throw new DomainException(
                $"{fieldName} must be greater than zero.");
        }

        decimal roundedPrice =
            decimal.Round(
                price,
                decimals: 2,
                MidpointRounding.ToEven);

        if (roundedPrice != price)
        {
            throw new DomainException(
                $"{fieldName} cannot have more than " +
                "two decimal places.");
        }
    }
}