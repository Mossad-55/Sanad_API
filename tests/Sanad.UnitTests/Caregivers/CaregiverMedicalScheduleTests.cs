using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverMedicalScheduleTests
{
    [Fact]
    public void Create_ShouldInitializeEmptyMedicalSchedule()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        MedicalWeeklySchedule schedule =
            Assert.IsType<MedicalWeeklySchedule>(
                caregiver.MedicalSchedule);

        Assert.Empty(schedule.Shifts);
        Assert.Empty(schedule.HomeVisitWindows);
        Assert.False(schedule.HasAvailability);

        Assert.Null(caregiver.CompanionSchedule);
    }

    [Fact]
    public void Create_ShouldNotInitializeMedicalScheduleForCompanion()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        Assert.Null(caregiver.MedicalSchedule);

        Assert.IsType<CompanionWeeklySchedule>(
            caregiver.CompanionSchedule);
    }

    [Fact]
    public void AddMedicalShift_ShouldAddShift()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        caregiver.AddMedicalShift(
            DayOfWeek.Saturday,
            MedicalShiftType.TwelveHourNight);

        MedicalWeeklySchedule schedule =
            Assert.IsType<MedicalWeeklySchedule>(
                caregiver.MedicalSchedule);

        MedicalShiftAvailability shift =
            Assert.Single(schedule.Shifts);

        Assert.Equal(
            DayOfWeek.Saturday,
            shift.DayOfWeek);

        Assert.Equal(
            MedicalShiftType.TwelveHourNight,
            shift.ShiftType);
    }

    [Fact]
    public void AddMedicalHomeVisitWindow_ShouldAddWindow()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        caregiver.AddMedicalHomeVisitWindow(
            DayOfWeek.Saturday,
            new TimeOnly(9, 0),
            new TimeOnly(12, 0));

        MedicalWeeklySchedule schedule =
            Assert.IsType<MedicalWeeklySchedule>(
                caregiver.MedicalSchedule);

        MedicalHomeVisitWindow window =
            Assert.Single(
                schedule.HomeVisitWindows);

        Assert.Equal(
            DayOfWeek.Saturday,
            window.DayOfWeek);

        Assert.Equal(
            new TimeOnly(9, 0),
            window.StartTime);

        Assert.Equal(
            new TimeOnly(12, 0),
            window.EndTime);
    }

    [Fact]
    public void AddMedicalShift_ShouldRejectCompanionCaregiver()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.AddMedicalShift(
                DayOfWeek.Saturday,
                MedicalShiftType.EightHourMorning));

        Assert.Null(caregiver.MedicalSchedule);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void AddMedicalHomeVisitWindow_ShouldRejectCompanionCaregiver()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver
                .AddMedicalHomeVisitWindow(
                    DayOfWeek.Saturday,
                    new TimeOnly(9, 0),
                    new TimeOnly(12, 0)));

        Assert.Null(caregiver.MedicalSchedule);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void AddMedicalHomeVisitWindow_ShouldBeAtomic_WhenDayHasShift()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        caregiver.AddMedicalShift(
            DayOfWeek.Saturday,
            MedicalShiftType.EightHourMorning);

        MedicalWeeklySchedule originalSchedule =
            Assert.IsType<MedicalWeeklySchedule>(
                caregiver.MedicalSchedule);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver
                .AddMedicalHomeVisitWindow(
                    DayOfWeek.Saturday,
                    new TimeOnly(18, 0),
                    new TimeOnly(20, 0)));

        MedicalWeeklySchedule scheduleAfterFailure =
            Assert.IsType<MedicalWeeklySchedule>(
                caregiver.MedicalSchedule);

        Assert.Same(
            originalSchedule,
            scheduleAfterFailure);

        Assert.Single(scheduleAfterFailure.Shifts);
        Assert.Empty(
            scheduleAfterFailure.HomeVisitWindows);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void AddMedicalShift_ShouldBeAtomic_WhenDayHasHomeVisits()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        caregiver.AddMedicalHomeVisitWindow(
            DayOfWeek.Saturday,
            new TimeOnly(9, 0),
            new TimeOnly(12, 0));

        MedicalWeeklySchedule originalSchedule =
            Assert.IsType<MedicalWeeklySchedule>(
                caregiver.MedicalSchedule);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.AddMedicalShift(
                DayOfWeek.Saturday,
                MedicalShiftType.EightHourEvening));

        MedicalWeeklySchedule scheduleAfterFailure =
            Assert.IsType<MedicalWeeklySchedule>(
                caregiver.MedicalSchedule);

        Assert.Same(
            originalSchedule,
            scheduleAfterFailure);

        Assert.Empty(scheduleAfterFailure.Shifts);

        Assert.Single(
            scheduleAfterFailure.HomeVisitWindows);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void RemoveMedicalShift_ShouldRemoveDuringOnboarding()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        caregiver.AddMedicalShift(
            DayOfWeek.Saturday,
            MedicalShiftType.EightHourMorning);

        caregiver.RemoveMedicalShift(
            DayOfWeek.Saturday,
            MedicalShiftType.EightHourMorning);

        MedicalWeeklySchedule schedule =
            Assert.IsType<MedicalWeeklySchedule>(
                caregiver.MedicalSchedule);

        Assert.Empty(schedule.Shifts);
        Assert.False(schedule.HasAvailability);
    }

    [Fact]
    public void RemoveMedicalHomeVisitWindow_ShouldRemoveDuringOnboarding()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        caregiver.AddMedicalHomeVisitWindow(
            DayOfWeek.Saturday,
            new TimeOnly(9, 0),
            new TimeOnly(12, 0));

        caregiver.RemoveMedicalHomeVisitWindow(
            DayOfWeek.Saturday,
            new TimeOnly(9, 0),
            new TimeOnly(12, 0));

        MedicalWeeklySchedule schedule =
            Assert.IsType<MedicalWeeklySchedule>(
                caregiver.MedicalSchedule);

        Assert.Empty(schedule.HomeVisitWindows);
        Assert.False(schedule.HasAvailability);
    }

    [Fact]
    public void RemoveMedicalShift_ShouldRejectFinalAvailabilityWhenActive()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        caregiver.AddMedicalShift(
            DayOfWeek.Saturday,
            MedicalShiftType.EightHourMorning);

        MakeMedicalCaregiverCompliantAndAvailable(
            caregiver);

        MedicalWeeklySchedule originalSchedule =
            Assert.IsType<MedicalWeeklySchedule>(
                caregiver.MedicalSchedule);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.RemoveMedicalShift(
                DayOfWeek.Saturday,
                MedicalShiftType.EightHourMorning));

        MedicalWeeklySchedule scheduleAfterFailure =
            Assert.IsType<MedicalWeeklySchedule>(
                caregiver.MedicalSchedule);

        Assert.Same(
            originalSchedule,
            scheduleAfterFailure);

        Assert.Single(scheduleAfterFailure.Shifts);

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void RemoveMedicalHomeVisitWindow_ShouldRejectFinalAvailabilityWhenActive()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        caregiver.AddMedicalHomeVisitWindow(
            DayOfWeek.Saturday,
            new TimeOnly(9, 0),
            new TimeOnly(12, 0));

        MakeMedicalCaregiverCompliantAndAvailable(
            caregiver);

        MedicalWeeklySchedule originalSchedule =
            Assert.IsType<MedicalWeeklySchedule>(
                caregiver.MedicalSchedule);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver
                .RemoveMedicalHomeVisitWindow(
                    DayOfWeek.Saturday,
                    new TimeOnly(9, 0),
                    new TimeOnly(12, 0)));

        MedicalWeeklySchedule scheduleAfterFailure =
            Assert.IsType<MedicalWeeklySchedule>(
                caregiver.MedicalSchedule);

        Assert.Same(
            originalSchedule,
            scheduleAfterFailure);

        Assert.Single(
            scheduleAfterFailure.HomeVisitWindows);

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void RemoveMedicalAvailability_ShouldAllowOneOfMultipleWhenActive()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        caregiver.AddMedicalShift(
            DayOfWeek.Saturday,
            MedicalShiftType.EightHourMorning);

        caregiver.AddMedicalHomeVisitWindow(
            DayOfWeek.Sunday,
            new TimeOnly(9, 0),
            new TimeOnly(12, 0));

        MakeMedicalCaregiverCompliantAndAvailable(
            caregiver);

        caregiver.RemoveMedicalShift(
            DayOfWeek.Saturday,
            MedicalShiftType.EightHourMorning);

        MedicalWeeklySchedule schedule =
            Assert.IsType<MedicalWeeklySchedule>(
                caregiver.MedicalSchedule);

        Assert.Empty(schedule.Shifts);

        Assert.Single(
            schedule.HomeVisitWindows);

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);
    }

    [Fact]
    public void AddMedicalAvailability_ShouldKeepActiveCaregiverAvailable()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        caregiver.AddMedicalShift(
            DayOfWeek.Saturday,
            MedicalShiftType.EightHourMorning);

        MakeMedicalCaregiverCompliantAndAvailable(
            caregiver);

        caregiver.AddMedicalHomeVisitWindow(
            DayOfWeek.Sunday,
            new TimeOnly(9, 0),
            new TimeOnly(12, 0));

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);
    }

    [Fact]
    public void RemoveMedicalShift_ShouldRejectCompanionCaregiver()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        Assert.Throws<DomainException>(
            () => caregiver.RemoveMedicalShift(
                DayOfWeek.Saturday,
                MedicalShiftType.EightHourMorning));

        Assert.Null(caregiver.MedicalSchedule);
    }

    [Fact]
    public void RemoveMedicalHomeVisitWindow_ShouldRejectCompanionCaregiver()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        Assert.Throws<DomainException>(
            () => caregiver
                .RemoveMedicalHomeVisitWindow(
                    DayOfWeek.Saturday,
                    new TimeOnly(9, 0),
                    new TimeOnly(12, 0)));

        Assert.Null(caregiver.MedicalSchedule);
    }

    private static void MakeMedicalCaregiverCompliantAndAvailable(
        Caregiver caregiver)
    {
        DateOnly currentDate =
            CreateCurrentDate();

        caregiver.AddCertificate(
            CaregiverCertificateType.PracticeLicense,
            "certificates/practice-license.jpg",
            expiryDate: null,
            currentDate);

        caregiver.AddCertificate(
            CaregiverCertificateType.GraduationCertificate,
            "certificates/graduation.jpg",
            expiryDate: null,
            currentDate);

        foreach (CaregiverCertificate certificate
                 in caregiver.Certificates)
        {
            caregiver.VerifyCertificate(
                certificate.Id);
        }

        caregiver.Activate();

        caregiver.BecomeAvailable(
            currentDate);
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