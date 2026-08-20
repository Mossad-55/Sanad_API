using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class MedicalHomeVisitWindowTests
{
    [Fact]
    public void Create_ShouldCreateSameDayWindow()
    {
        MedicalHomeVisitWindow window =
            MedicalHomeVisitWindow.Create(
                DayOfWeek.Saturday,
                new TimeOnly(9, 0),
                new TimeOnly(13, 0));

        Assert.Equal(
            DayOfWeek.Saturday,
            window.DayOfWeek);

        Assert.Equal(
            new TimeOnly(9, 0),
            window.StartTime);

        Assert.Equal(
            new TimeOnly(13, 0),
            window.EndTime);

        Assert.Equal(
            TimeSpan.FromHours(4),
            window.Duration);
    }

    [Fact]
    public void Create_ShouldSupportCustomMinutes()
    {
        MedicalHomeVisitWindow window =
            MedicalHomeVisitWindow.Create(
                DayOfWeek.Sunday,
                new TimeOnly(9, 30),
                new TimeOnly(12, 45));

        Assert.Equal(
            TimeSpan.FromHours(3) +
            TimeSpan.FromMinutes(15),
            window.Duration);
    }

    [Fact]
    public void Create_ShouldRejectEqualTimes()
    {
        Assert.Throws<DomainException>(
            () => MedicalHomeVisitWindow.Create(
                DayOfWeek.Saturday,
                new TimeOnly(9, 0),
                new TimeOnly(9, 0)));
    }

    [Fact]
    public void Create_ShouldRejectEndBeforeStart()
    {
        Assert.Throws<DomainException>(
            () => MedicalHomeVisitWindow.Create(
                DayOfWeek.Saturday,
                new TimeOnly(20, 0),
                new TimeOnly(2, 0)));
    }

    [Fact]
    public void Create_ShouldRejectInvalidDayOfWeek()
    {
        Assert.Throws<DomainException>(
            () => MedicalHomeVisitWindow.Create(
                (DayOfWeek)999,
                new TimeOnly(9, 0),
                new TimeOnly(13, 0)));
    }

    [Fact]
    public void EqualWindows_ShouldHaveValueEquality()
    {
        MedicalHomeVisitWindow first =
            MedicalHomeVisitWindow.Create(
                DayOfWeek.Saturday,
                new TimeOnly(9, 0),
                new TimeOnly(13, 0));

        MedicalHomeVisitWindow second =
            MedicalHomeVisitWindow.Create(
                DayOfWeek.Saturday,
                new TimeOnly(9, 0),
                new TimeOnly(13, 0));

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentWindows_ShouldNotBeEqual()
    {
        MedicalHomeVisitWindow first =
            MedicalHomeVisitWindow.Create(
                DayOfWeek.Saturday,
                new TimeOnly(9, 0),
                new TimeOnly(13, 0));

        MedicalHomeVisitWindow second =
            MedicalHomeVisitWindow.Create(
                DayOfWeek.Saturday,
                new TimeOnly(10, 0),
                new TimeOnly(14, 0));

        Assert.NotEqual(first, second);
    }
}