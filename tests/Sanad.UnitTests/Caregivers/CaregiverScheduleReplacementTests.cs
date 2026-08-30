using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverScheduleReplacementTests
{
    [Fact]
    public void ReplaceMedicalSchedule_ShouldBuildFreshScheduleFromInputs()
    {
        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                CaregiverType.Medical);

        caregiver.ReplaceMedicalSchedule(
        [
            new MedicalShiftInput(
                DayOfWeek.Sunday,
                MedicalShiftType.EightHourMorning)
        ],
        [
            new MedicalHomeVisitWindowInput(
                DayOfWeek.Monday,
                new TimeOnly(9, 0),
                new TimeOnly(12, 0))
        ]);

        Assert.NotNull(caregiver.MedicalSchedule);
        Assert.Single(caregiver.MedicalSchedule!.Shifts);
        Assert.Single(caregiver.MedicalSchedule.HomeVisitWindows);
    }

    [Fact]
    public void ReplaceMedicalSchedule_ShouldRejectDayMixingShiftsAndHomeVisits()
    {
        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                CaregiverType.Medical);

        Assert.Throws<DomainException>(() =>
            caregiver.ReplaceMedicalSchedule(
            [
                new MedicalShiftInput(
                    DayOfWeek.Sunday,
                    MedicalShiftType.TwelveHourDay)
            ],
            [
                new MedicalHomeVisitWindowInput(
                    DayOfWeek.Sunday,
                    new TimeOnly(18, 0),
                    new TimeOnly(20, 0))
            ]));
    }

    [Fact]
    public void ReplaceMedicalSchedule_ShouldRejectEmptyScheduleForActiveCaregiver()
    {
        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                CaregiverType.Medical);

        caregiver.TransitionToActive();

        Assert.Throws<DomainException>(() =>
            caregiver.ReplaceMedicalSchedule(
                [],
                []));
    }
}