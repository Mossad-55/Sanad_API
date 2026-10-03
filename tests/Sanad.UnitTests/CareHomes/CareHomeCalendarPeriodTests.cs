using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.UnitTests.CareHomes;

public sealed class CareHomeCalendarPeriodTests
{
    [Theory]
    [InlineData(2026, 10, 15, 2026, 11, 15)]
    [InlineData(2026, 1, 31, 2026, 2, 28)]
    [InlineData(2028, 1, 31, 2028, 2, 29)]
    [InlineData(2028, 2, 29, 2028, 3, 29)]
    [InlineData(2026, 1, 30, 2026, 2, 28)]
    [InlineData(2026, 3, 31, 2026, 4, 30)]
    [InlineData(2026, 12, 15, 2027, 1, 15)]
    public void CalculateEndDate_ReturnsSameDayInNextMonthOrNextMonthEnd(
        int startYear,
        int startMonth,
        int startDay,
        int expectedYear,
        int expectedMonth,
        int expectedDay)
    {
        DateOnly startDate = new(startYear, startMonth, startDay);

        DateOnly result = CareHomeCalendarPeriod.CalculateEndDate(startDate);

        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), result);
    }
}
