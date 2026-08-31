using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Onboarding;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers.Onboarding;

public sealed class AdminCaregiverReviewsTests
{
    [Fact]
    public async Task GetDetail_ShouldReturnCaregiverProfile()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, _) =
            await SeedSubmittedCompanionAsync(dbContext);

        var result =
            await new GetCaregiverAdminDetailQueryHandler(dbContext)
                .Handle(
                    new GetCaregiverAdminDetailQuery(caregiverId),
                    default);

        Assert.True(result.IsSuccess);
        Assert.Equal(caregiverId, result.Value.Id);
        Assert.Equal(
            CaregiverStatus.PendingReview,
            result.Value.Status);
    }

    [Fact]
    public async Task GetDetail_ShouldReturnNotFoundForMissingCaregiver()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var result =
            await new GetCaregiverAdminDetailQueryHandler(dbContext)
                .Handle(
                    new GetCaregiverAdminDetailQuery(
                        CaregiverId.New()),
                    default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.CaregiverNotFound,
            result.Error);
    }

    [Fact]
    public async Task Approve_ShouldActivatePendingCaregiver()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, _) =
            await SeedSubmittedCompanionAsync(dbContext);

        var result =
            await new ApproveCaregiverCommandHandler(dbContext)
                .Handle(
                    new ApproveCaregiverCommand(
                        caregiverId,
                        CaregiverTestData.CurrentDate,
                        CaregiverTestData.CurrentUtc),
                    default);

        Assert.True(result.IsSuccess);

        var detail =
            await new GetCaregiverAdminDetailQueryHandler(dbContext)
                .Handle(
                    new GetCaregiverAdminDetailQuery(caregiverId),
                    default);

        Assert.Equal(
            CaregiverStatus.Active,
            detail.Value.Status);
    }

    [Fact]
    public async Task Approve_ShouldRejectCaregiverNotPendingReview()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var bootstrap =
            await new BootstrapCaregiverCommandHandler(dbContext)
                .Handle(
                    new BootstrapCaregiverCommand(
                        UserId.New(),
                        CaregiverType.Companion),
                    default);

        var result =
            await new ApproveCaregiverCommandHandler(dbContext)
                .Handle(
                    new ApproveCaregiverCommand(
                        bootstrap.Value.Id,
                        CaregiverTestData.CurrentDate,
                        CaregiverTestData.CurrentUtc),
                    default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.InvalidState,
            result.Error);
    }

    [Fact]
    public async Task Reject_ShouldRejectPendingApplicationWithReason()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, _) =
            await SeedSubmittedCompanionAsync(dbContext);

        var result =
            await new RejectCaregiverApplicationCommandHandler(dbContext)
                .Handle(
                    new RejectCaregiverApplicationCommand(
                        caregiverId,
                        "Missing required documentation.",
                        CaregiverTestData.CurrentUtc),
                    default);

        Assert.True(result.IsSuccess);

        var detail =
            await new GetCaregiverAdminDetailQueryHandler(dbContext)
                .Handle(
                    new GetCaregiverAdminDetailQuery(caregiverId),
                    default);

        Assert.Equal(
            CaregiverStatus.Rejected,
            detail.Value.Status);
        Assert.Equal(
            "Missing required documentation.",
            detail.Value.StatusReason);
    }

    [Fact]
    public async Task RequestCorrection_ShouldMovePendingToNeedsCorrection()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, _) =
            await SeedSubmittedCompanionAsync(dbContext);

        var result =
            await new RequestCaregiverCorrectionCommandHandler(dbContext)
                .Handle(
                    new RequestCaregiverCorrectionCommand(
                        caregiverId,
                        "Please add your service areas.",
                        CaregiverTestData.CurrentUtc),
                    default);

        Assert.True(result.IsSuccess);

        var detail =
            await new GetCaregiverAdminDetailQueryHandler(dbContext)
                .Handle(
                    new GetCaregiverAdminDetailQuery(caregiverId),
                    default);

        Assert.Equal(
            CaregiverStatus.NeedsCorrection,
            detail.Value.Status);
    }

    [Fact]
    public async Task Suspend_ShouldRejectCaregiverWhoIsNotActive()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, _) =
            await SeedSubmittedCompanionAsync(dbContext);

        var result =
            await new SuspendCaregiverCommandHandler(dbContext)
                .Handle(
                    new SuspendCaregiverCommand(
                        caregiverId,
                        "Policy violation.",
                        CaregiverTestData.CurrentUtc),
                    default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.InvalidState,
            result.Error);
    }

    private static async Task<(CaregiverId CaregiverId, UserId UserId)>
        SeedSubmittedCompanionAsync(CaregiversDbContext dbContext)
    {
        UserId userId = UserId.New();

        var bootstrap =
            await new BootstrapCaregiverCommandHandler(dbContext)
                .Handle(
                    new BootstrapCaregiverCommand(
                        userId,
                        CaregiverType.Companion),
                    default);

        Caregiver caregiver =
            await dbContext.Caregivers.SingleAsync(
                c => c.UserId == userId);

        CaregiverTestData.EnsureReadyForSubmission(
            caregiver);

        caregiver.SubmitForReview(
            CaregiverTestData.CurrentUtc,
            CaregiverTestData.CurrentDate);

        await dbContext.SaveChangesAsync();

        return (bootstrap.Value.Id, userId);
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