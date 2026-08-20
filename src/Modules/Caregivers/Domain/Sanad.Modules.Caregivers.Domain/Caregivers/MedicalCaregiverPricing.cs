using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class MedicalCaregiverPricing : ValueObject
{
    private MedicalCaregiverPricing()
    {
    }

    private MedicalCaregiverPricing(
        decimal homeVisitPrice,
        decimal eightHourShiftPrice,
        decimal twelveHourShiftPrice,
        decimal twentyFourHourShiftPrice)
    {
        HomeVisitPrice = homeVisitPrice;
        EightHourShiftPrice = eightHourShiftPrice;
        TwelveHourShiftPrice = twelveHourShiftPrice;
        TwentyFourHourShiftPrice =
            twentyFourHourShiftPrice;
    }

    public decimal HomeVisitPrice { get; private set; }

    public decimal EightHourShiftPrice
    {
        get;
        private set;
    }

    public decimal TwelveHourShiftPrice
    {
        get;
        private set;
    }

    public decimal TwentyFourHourShiftPrice
    {
        get;
        private set;
    }

    internal static MedicalCaregiverPricing Create(
        decimal homeVisitPrice,
        decimal eightHourShiftPrice,
        decimal twelveHourShiftPrice,
        decimal twentyFourHourShiftPrice)
    {
        ValidatePrice(
            homeVisitPrice,
            "Home Visit price");

        ValidatePrice(
            eightHourShiftPrice,
            "8-hour shift price");

        ValidatePrice(
            twelveHourShiftPrice,
            "12-hour shift price");

        ValidatePrice(
            twentyFourHourShiftPrice,
            "24-hour shift price");

        return new MedicalCaregiverPricing(
            homeVisitPrice,
            eightHourShiftPrice,
            twelveHourShiftPrice,
            twentyFourHourShiftPrice);
    }

    protected override IEnumerable<object?>
        GetEqualityComponents()
    {
        yield return HomeVisitPrice;
        yield return EightHourShiftPrice;
        yield return TwelveHourShiftPrice;
        yield return TwentyFourHourShiftPrice;
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