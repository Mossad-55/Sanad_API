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

        Assert.NotNull(caregiver.CompanionSchedule);
        Assert.Empty(
            caregiver.CompanionSchedule.Windows);
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
    public void AddCompanionAvailabilityWindow_ShouldAddWindow()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        caregiver.AddCompanionAvailabilityWindow(
            DayOfWeek.Saturday,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0));

        CompanionAvailabilityWindow window =
            Assert.Single(
                caregiver.CompanionSchedule!.Windows);

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
    public void AddCompanionAvailabilityWindow_ShouldRejectMedicalCaregiver()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        Assert.Throws<DomainException>(
            () => caregiver
                .AddCompanionAvailabilityWindow(
                    DayOfWeek.Saturday,
                    new TimeOnly(8, 0),
                    new TimeOnly(16, 0)));

        Assert.Null(caregiver.CompanionSchedule);
    }

    [Fact]
    public void AddCompanionAvailabilityWindow_ShouldBeAtomic_WhenOverlapping()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        caregiver.AddCompanionAvailabilityWindow(
            DayOfWeek.Saturday,
            new TimeOnly(8, 0),
            new TimeOnly(14, 0));

        CompanionWeeklySchedule originalSchedule =
            caregiver.CompanionSchedule!;

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver
                .AddCompanionAvailabilityWindow(
                    DayOfWeek.Saturday,
                    new TimeOnly(12, 0),
                    new TimeOnly(16, 0)));

        Assert.Same(
            originalSchedule,
            caregiver.CompanionSchedule);

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
            DayOfWeek.Saturday,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0));

        caregiver.RemoveCompanionAvailabilityWindow(
            DayOfWeek.Saturday,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0));

        Assert.Empty(
            caregiver.CompanionSchedule!.Windows);
    }

    [Fact]
    public void RemoveCompanionAvailabilityWindow_ShouldRejectFinalWindowWhenActive()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        caregiver.AddCompanionAvailabilityWindow(
            DayOfWeek.Saturday,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0));

        caregiver.Activate();

        caregiver.BecomeAvailable(
            CreateCurrentDate());

        CompanionWeeklySchedule originalSchedule =
            caregiver.CompanionSchedule!;

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver
                .RemoveCompanionAvailabilityWindow(
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
            DayOfWeek.Saturday,
            new TimeOnly(8, 0),
            new TimeOnly(12, 0));

        caregiver.AddCompanionAvailabilityWindow(
            DayOfWeek.Saturday,
            new TimeOnly(14, 0),
            new TimeOnly(18, 0));

        caregiver.Activate();

        caregiver.BecomeAvailable(
            CreateCurrentDate());

        caregiver.RemoveCompanionAvailabilityWindow(
            DayOfWeek.Saturday,
            new TimeOnly(8, 0),
            new TimeOnly(12, 0));

        Assert.Single(
            caregiver.CompanionSchedule!.Windows);

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);
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