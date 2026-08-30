using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Onboarding;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers.Onboarding;

public sealed class CaregiverSubmissionTests
{
    [Fact]
    public async Task Submit_ShouldMoveReadyOnboardingCaregiverToPendingReview()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiver, userId) =
            await SeedReadyCompanionAsync(dbContext);

        var result =
            await new SubmitCaregiverCommandHandler(dbContext)
                .Handle(
                    new SubmitCaregiverCommand(
                        userId,
                        CaregiverTestData.CurrentDate,
                        CaregiverTestData.CurrentUtc),
                    default);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            CaregiverStatus.PendingReview,
            result.Value.Status);
    }

    [Fact]
    public async Task Submit_ShouldRejectCaregiverWithMissingReadiness()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        UserId userId =
            await BootstrapAsync(
                dbContext,
                CaregiverType.Companion);

        var result =
            await new SubmitCaregiverCommandHandler(dbContext)
                .Handle(
                    new SubmitCaregiverCommand(
                        userId,
                        CaregiverTestData.CurrentDate,
                        CaregiverTestData.CurrentUtc),
                    default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.InvalidState,
            result.Error);
    }

    [Fact]
    public async Task Submit_ShouldRejectSecondSubmitWhilePendingReview()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiver, userId) =
            await SeedReadyCompanionAsync(dbContext);

        var handler =
            new SubmitCaregiverCommandHandler(dbContext);

        await handler.Handle(
            new SubmitCaregiverCommand(
                userId,
                CaregiverTestData.CurrentDate,
                CaregiverTestData.CurrentUtc),
            default);

        var second =
            await handler.Handle(
                new SubmitCaregiverCommand(
                    userId,
                    CaregiverTestData.CurrentDate,
                    CaregiverTestData.CurrentUtc.AddMinutes(1)),
                default);

        Assert.True(second.IsFailure);
        Assert.Equal(
            OnboardingErrors.InvalidState,
            second.Error);
    }

    [Fact]
    public async Task Submit_ShouldResubmitCaregiverNeedingCorrection()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiver, userId) =
            await SeedReadyCompanionAsync(dbContext);

        caregiver.SubmitForReview(
            CaregiverTestData.CurrentUtc,
            CaregiverTestData.CurrentDate);

        caregiver.RequestCorrection(
            "Please add your work areas.",
            CaregiverTestData.CurrentUtc.AddMinutes(1));

        // Caregiver completes the correction (already ready in seed).
        var result =
            await new SubmitCaregiverCommandHandler(dbContext)
                .Handle(
                    new SubmitCaregiverCommand(
                        userId,
                        CaregiverTestData.CurrentDate,
                        CaregiverTestData.CurrentUtc.AddMinutes(2)),
                    default);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            CaregiverStatus.PendingReview,
            result.Value.Status);
    }

    private static async Task<(Caregiver Caregiver, UserId UserId)>
        SeedReadyCompanionAsync(CaregiversDbContext dbContext)
    {
        UserId userId =
            await BootstrapAsync(
                dbContext,
                CaregiverType.Companion);

        Caregiver caregiver =
            await dbContext.Caregivers.SingleAsync(
                c => c.UserId == userId);

        CaregiverTestData.EnsureReadyForSubmission(
            caregiver);

        await dbContext.SaveChangesAsync();

        return (caregiver, userId);
    }

    private static async Task<UserId> BootstrapAsync(
        CaregiversDbContext dbContext,
        CaregiverType caregiverType)
    {
        UserId userId = UserId.New();

        await new BootstrapCaregiverCommandHandler(dbContext)
            .Handle(
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