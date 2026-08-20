using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverCompanionScheduleTests
{
    [Fact]
    public void Create_ShouldInitializeEmptyCompanionSchedule()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        CompanionWeeklySchedule schedule =
            Assert.IsType<CompanionWeeklySchedule>(
                caregiver.CompanionSchedule);

        Assert.Empty(schedule.Windows);
    }

    [Fact]
    public void Create_ShouldNotInitializeCompanionScheduleForMedical()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        Assert.Null(caregiver.CompanionSchedule);
    }

    [Fact]
    public void AddCompanionAvailabilityWindow_ShouldAddProductWindow()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        caregiver.AddCompanionAvailabilityWindow(
            CompanionBookingType.EightHourDay,
            DayOfWeek.Saturday,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0));

        CompanionWeeklySchedule schedule =
            Assert.IsType<CompanionWeeklySchedule>(
                caregiver.CompanionSchedule);

        CompanionAvailabilityWindow window =
            Assert.Single(schedule.Windows);

        Assert.Equal(
            CompanionBookingType.EightHourDay,
            window.BookingType);

        Assert.Equal(
            DayOfWeek.Saturday,
            window.DayOfWeek);

        Assert.Equal(
            new TimeOnly(8, 0),
            window.StartTime);

        Assert.Equal(
            new TimeOnly(16, 0),
            window.EndTime);
    }

    [Fact]
    public void AddCompanionAvailabilityWindow_ShouldAllowDifferentProducts()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        caregiver.AddCompanionAvailabilityWindow(
            CompanionBookingType.Hourly,
            DayOfWeek.Saturday,
            new TimeOnly(8, 0),
            new TimeOnly(12, 0));

        caregiver.AddCompanionAvailabilityWindow(
            CompanionBookingType.Overnight,
            DayOfWeek.Saturday,
            new TimeOnly(20, 0),
            new TimeOnly(8, 0));

        CompanionWeeklySchedule schedule =
            Assert.IsType<CompanionWeeklySchedule>(
                caregiver.CompanionSchedule);

        Assert.Equal(
            2,
            schedule.Windows.Count);
    }

    [Fact]
    public void AddCompanionAvailabilityWindow_ShouldRejectMedicalCaregiver()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver
                .AddCompanionAvailabilityWindow(
                    CompanionBookingType.EightHourDay,
                    DayOfWeek.Saturday,
                    new TimeOnly(8, 0),
                    new TimeOnly(16, 0)));

        Assert.Null(caregiver.CompanionSchedule);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void AddCompanionAvailabilityWindow_ShouldBeAtomic_WhenOverlapping()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        caregiver.AddCompanionAvailabilityWindow(
            CompanionBookingType.EightHourDay,
            DayOfWeek.Saturday,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0));

        CompanionWeeklySchedule originalSchedule =
            Assert.IsType<CompanionWeeklySchedule>(
                caregiver.CompanionSchedule);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver
                .AddCompanionAvailabilityWindow(
                    CompanionBookingType.Hourly,
                    DayOfWeek.Saturday,
                    new TimeOnly(12, 0),
                    new TimeOnly(18, 0)));

        CompanionWeeklySchedule scheduleAfterFailure =
            Assert.IsType<CompanionWeeklySchedule>(
                caregiver.CompanionSchedule);

        Assert.Same(
            originalSchedule,
            scheduleAfterFailure);

        Assert.Single(
            scheduleAfterFailure.Windows);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void RemoveCompanionAvailabilityWindow_ShouldRemoveDuringOnboarding()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        caregiver.AddCompanionAvailabilityWindow(
            CompanionBookingType.Hourly,
            DayOfWeek.Saturday,
            new TimeOnly(8, 0),
            new TimeOnly(12, 0));

        caregiver.RemoveCompanionAvailabilityWindow(
            CompanionBookingType.Hourly,
            DayOfWeek.Saturday,
            new TimeOnly(8, 0),
            new TimeOnly(12, 0));

        CompanionWeeklySchedule schedule =
            Assert.IsType<CompanionWeeklySchedule>(
                caregiver.CompanionSchedule);

        Assert.Empty(schedule.Windows);
    }

    [Fact]
    public void RemoveCompanionAvailabilityWindow_ShouldRequireMatchingProduct()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        caregiver.AddCompanionAvailabilityWindow(
            CompanionBookingType.Hourly,
            DayOfWeek.Saturday,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0));

        Assert.Throws<DomainException>(
            () => caregiver
                .RemoveCompanionAvailabilityWindow(
                    CompanionBookingType.EightHourDay,
                    DayOfWeek.Saturday,
                    new TimeOnly(8, 0),
                    new TimeOnly(16, 0)));

        CompanionWeeklySchedule schedule =
            Assert.IsType<CompanionWeeklySchedule>(
                caregiver.CompanionSchedule);

        Assert.Single(schedule.Windows);
    }

    [Fact]
    public void RemoveCompanionAvailabilityWindow_ShouldRejectFinalWindowWhenActive()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        caregiver.AddCompanionAvailabilityWindow(
            CompanionBookingType.EightHourDay,
            DayOfWeek.Saturday,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0));

        caregiver.Activate();

        caregiver.BecomeAvailable(
            CreateCurrentDate());

        CompanionWeeklySchedule originalSchedule =
            Assert.IsType<CompanionWeeklySchedule>(
                caregiver.CompanionSchedule);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver
                .RemoveCompanionAvailabilityWindow(
                    CompanionBookingType.EightHourDay,
                    DayOfWeek.Saturday,
                    new TimeOnly(8, 0),
                    new TimeOnly(16, 0)));

        CompanionWeeklySchedule scheduleAfterFailure =
            Assert.IsType<CompanionWeeklySchedule>(
                caregiver.CompanionSchedule);

        Assert.Same(
            originalSchedule,
            scheduleAfterFailure);

        Assert.Single(
            scheduleAfterFailure.Windows);

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void RemoveCompanionAvailabilityWindow_ShouldAllowOneOfMultipleWhenActive()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        caregiver.AddCompanionAvailabilityWindow(
            CompanionBookingType.Hourly,
            DayOfWeek.Saturday,
            new TimeOnly(8, 0),
            new TimeOnly(12, 0));

        caregiver.AddCompanionAvailabilityWindow(
            CompanionBookingType.Overnight,
            DayOfWeek.Saturday,
            new TimeOnly(20, 0),
            new TimeOnly(8, 0));

        caregiver.Activate();

        caregiver.BecomeAvailable(
            CreateCurrentDate());

        caregiver.RemoveCompanionAvailabilityWindow(
            CompanionBookingType.Hourly,
            DayOfWeek.Saturday,
            new TimeOnly(8, 0),
            new TimeOnly(12, 0));

        CompanionWeeklySchedule schedule =
            Assert.IsType<CompanionWeeklySchedule>(
                caregiver.CompanionSchedule);

        CompanionAvailabilityWindow remaining =
            Assert.Single(schedule.Windows);

        Assert.Equal(
            CompanionBookingType.Overnight,
            remaining.BookingType);

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);
    }

    [Fact]
    public void AddCompanionAvailabilityWindow_ShouldKeepActiveCaregiverAvailable()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        caregiver.AddCompanionAvailabilityWindow(
            CompanionBookingType.Hourly,
            DayOfWeek.Saturday,
            new TimeOnly(8, 0),
            new TimeOnly(12, 0));

        caregiver.Activate();

        caregiver.BecomeAvailable(
            CreateCurrentDate());

        caregiver.AddCompanionAvailabilityWindow(
            CompanionBookingType.Overnight,
            DayOfWeek.Saturday,
            new TimeOnly(20, 0),
            new TimeOnly(8, 0));

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);
    }

    [Fact]
    public void RemoveCompanionAvailabilityWindow_ShouldRejectMedicalCaregiver()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        Assert.Throws<DomainException>(
            () => caregiver
                .RemoveCompanionAvailabilityWindow(
                    CompanionBookingType.Hourly,
                    DayOfWeek.Saturday,
                    new TimeOnly(8, 0),
                    new TimeOnly(12, 0)));

        Assert.Null(caregiver.CompanionSchedule);
    }

    private static Caregiver CreateCaregiver(
        CaregiverType caregiverType)
    {
        return Caregiver.Create(
            UserId.New(),
            caregiverType);
    }

    private static DateOnly CreateCurrentDate()
    {
        return new DateOnly(
            2026,
            8,
            20);
    }
}