using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Onboarding;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers.Onboarding;

public sealed class CaregiverSchedulesTests
{
    [Fact]
    public async Task UpdateMedicalSchedule_ShouldPersistShiftAndWindow()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        UserId userId = await BootstrapAsync(
            dbContext,
            CaregiverType.Medical);

        var handler =
            new UpdateMedicalScheduleCommandHandler(
                dbContext);

        var result =
            await handler.Handle(
                new UpdateMedicalScheduleCommand(
                    userId,
                    [new MedicalShiftItem(
                        DayOfWeek.Sunday,
                        MedicalShiftType.EightHourMorning)],
                    [new MedicalHomeVisitWindowItem(
                        DayOfWeek.Monday,
                        new TimeOnly(9, 0),
                        new TimeOnly(12, 0))]),
                default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.MedicalSchedule);
        Assert.Single(result.Value.MedicalSchedule!.Shifts);
        Assert.Single(result.Value.MedicalSchedule.HomeVisitWindows);
        Assert.Null(result.Value.CompanionSchedule);
    }

    [Fact]
    public async Task UpdateMedicalSchedule_ShouldRejectShiftAndWindowOnSameDay()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        UserId userId = await BootstrapAsync(
            dbContext,
            CaregiverType.Medical);

        var handler =
            new UpdateMedicalScheduleCommandHandler(
                dbContext);

        var result =
            await handler.Handle(
                new UpdateMedicalScheduleCommand(
                    userId,
                    [new MedicalShiftItem(
                        DayOfWeek.Sunday,
                        MedicalShiftType.TwelveHourDay)],
                    [new MedicalHomeVisitWindowItem(
                        DayOfWeek.Sunday,
                        new TimeOnly(18, 0),
                        new TimeOnly(20, 0))]),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.InvalidSchedule,
            result.Error);
    }

    [Fact]
    public async Task UpdateMedicalSchedule_ShouldRejectForCompanionCaregiver()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        UserId userId = await BootstrapAsync(
            dbContext,
            CaregiverType.Companion);

        var handler =
            new UpdateMedicalScheduleCommandHandler(
                dbContext);

        var result =
            await handler.Handle(
                new UpdateMedicalScheduleCommand(
                    userId,
                    [],
                    []),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.WrongCaregiverType,
            result.Error);
    }

    [Fact]
    public async Task UpdateCompanionSchedule_ShouldPersistWindow()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        UserId userId = await BootstrapAsync(
            dbContext,
            CaregiverType.Companion);

        var handler =
            new UpdateCompanionScheduleCommandHandler(
                dbContext);

        var result =
            await handler.Handle(
                new UpdateCompanionScheduleCommand(
                    userId,
                    [new CompanionAvailabilityWindowItem(
                        CompanionBookingType.Hourly,
                        DayOfWeek.Sunday,
                        new TimeOnly(10, 0),
                        new TimeOnly(14, 0))]),
                default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.CompanionSchedule);
        Assert.Single(result.Value.CompanionSchedule!.Windows);
        Assert.Equal(
            CompanionBookingType.Hourly,
            result.Value.CompanionSchedule.Windows[0].BookingType);
    }

    [Fact]
    public async Task BecomeAvailable_ShouldRejectCaregiverWhoIsNotActive()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        UserId userId = await BootstrapAsync(
            dbContext,
            CaregiverType.Medical);

        var handler =
            new BecomeAvailableCommandHandler(
                dbContext);

        var result =
            await handler.Handle(
                new BecomeAvailableCommand(
                    userId,
                    CaregiverTestData.CurrentDate),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.NotActive,
            result.Error);
    }

    private static async Task<UserId> BootstrapAsync(
        CaregiversDbContext dbContext,
        CaregiverType caregiverType)
    {
        UserId userId = UserId.New();

        var bootstrapHandler =
            new BootstrapCaregiverCommandHandler(
                dbContext);

        await bootstrapHandler.Handle(
            new BootstrapCaregiverCommand(
                userId,
                caregiverType),
            default);

        return userId;
    }

    private static CaregiversDbContext CreateDbContext()
    {
        DbContextOptions<CaregiversDbContext> options =
            new DbContextOptionsBuilder<CaregiversDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new CaregiversDbContext(options);
    }
}