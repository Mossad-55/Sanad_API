using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class CaregiverPricing : ValueObject
{
    private CaregiverPricing()
    {
    }

    private CaregiverPricing(
        decimal? hourlyRate,
        decimal? dailyRate,
        decimal? overnightRate,
        decimal? homeVisitRate,
        decimal? eightHourShiftRate,
        decimal? twelveHourShiftRate,
        decimal? twentyFourHourShiftRate)
    {
        HourlyRate = hourlyRate;
        DailyRate = dailyRate;
        OvernightRate = overnightRate;

        HomeVisitRate = homeVisitRate;
        EightHourShiftRate = eightHourShiftRate;
        TwelveHourShiftRate = twelveHourShiftRate;
        TwentyFourHourShiftRate = twentyFourHourShiftRate;
    }

    public decimal? HourlyRate { get; private set; }

    public decimal? DailyRate { get; private set; }

    public decimal? OvernightRate { get; private set; }

    public decimal? HomeVisitRate { get; private set; }

    public decimal? EightHourShiftRate { get; private set; }

    public decimal? TwelveHourShiftRate { get; private set; }

    public decimal? TwentyFourHourShiftRate { get; private set; }

    public static CaregiverPricing Create(
        decimal? hourlyRate,
        decimal? dailyRate,
        decimal? overnightRate,
        decimal? homeVisitRate,
        decimal? eightHourShiftRate,
        decimal? twelveHourShiftRate,
        decimal? twentyFourHourShiftRate)
    {
        decimal?[] prices =
        [
            hourlyRate,
            dailyRate,
            overnightRate,
            homeVisitRate,
            eightHourShiftRate,
            twelveHourShiftRate,
            twentyFourHourShiftRate
        ];

        foreach (decimal? price in prices)
        {
            if (price is < 0)
            {
                throw new DomainException("Price cannot be negative.");
            }
        }

        return new CaregiverPricing(
            hourlyRate,
            dailyRate,
            overnightRate,
            homeVisitRate,
            eightHourShiftRate,
            twelveHourShiftRate,
            twentyFourHourShiftRate);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return HourlyRate;
        yield return DailyRate;
        yield return OvernightRate;

        yield return HomeVisitRate;
        yield return EightHourShiftRate;
        yield return TwelveHourShiftRate;
        yield return TwentyFourHourShiftRate;
    }
}