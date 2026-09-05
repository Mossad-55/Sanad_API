using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Application.Discovery;

public static class BookingPricingService
{
    public const decimal DefaultPlatformCommissionPercentage = 15.00m;

    public static BookingPriceSnapshot CalculatePrice(
        Caregiver caregiver,
        BookingShiftType shiftType,
        TimeOnly startTime,
        TimeOnly endTime,
        decimal commissionPercentage = DefaultPlatformCommissionPercentage)
    {
        decimal baseRate = 0m;

        if (caregiver.Type == CaregiverType.Medical)
        {
            if (caregiver.MedicalPricing is null)
            {
                throw new DomainException("Caregiver has not configured medical pricing.");
            }

            baseRate = shiftType switch
            {
                BookingShiftType.HomeVisit => caregiver.MedicalPricing.HomeVisitPrice,
                BookingShiftType.EightHourShift => caregiver.MedicalPricing.EightHourShiftPrice,
                BookingShiftType.TwelveHourShift => caregiver.MedicalPricing.TwelveHourShiftPrice,
                BookingShiftType.TwentyFourHourShift => caregiver.MedicalPricing.TwentyFourHourShiftPrice,
                _ => throw new DomainException($"Shift type '{shiftType}' is not supported for Medical caregivers.")
            };
        }
        else if (caregiver.Type == CaregiverType.Companion)
        {
            if (caregiver.CompanionPricing is null)
            {
                throw new DomainException("Caregiver has not configured companion pricing.");
            }

            baseRate = shiftType switch
            {
                BookingShiftType.Hourly => CalculateHourlyTotal(caregiver.CompanionPricing.HourlyPrice, startTime, endTime),
                BookingShiftType.EightHourShift => caregiver.CompanionPricing.EightHourDayPrice,
                BookingShiftType.TwelveHourShift => caregiver.CompanionPricing.OvernightPrice,
                _ => throw new DomainException($"Shift type '{shiftType}' is not supported for Companion caregivers.")
            };
        }

        return BookingPriceSnapshot.Calculate(baseRate, commissionPercentage);
    }

    private static decimal CalculateHourlyTotal(decimal hourlyPrice, TimeOnly startTime, TimeOnly endTime)
    {
        double hours;
        if (endTime > startTime)
        {
            hours = (endTime - startTime).TotalHours;
        }
        else
        {
            hours = (TimeSpan.FromHours(24) - (startTime - endTime)).TotalHours;
        }

        if (hours <= 0)
        {
            throw new DomainException("Booking duration must be greater than zero hours.");
        }

        return decimal.Round(hourlyPrice * (decimal)hours, 2, MidpointRounding.ToEven);
    }
}