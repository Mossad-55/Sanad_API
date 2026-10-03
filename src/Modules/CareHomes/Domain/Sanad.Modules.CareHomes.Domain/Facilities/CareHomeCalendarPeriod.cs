namespace Sanad.Modules.CareHomes.Domain.Facilities;

/// <summary>Calculates the end date of one Care Homes calendar month.</summary>
public static class CareHomeCalendarPeriod
{
    /// <summary>
    /// Returns the same calendar day in the next month, clamped to that month's
    /// final day when the start day does not exist in the next month.
    /// </summary>
    public static DateOnly CalculateEndDate(DateOnly startDate) => startDate.AddMonths(1);
}
