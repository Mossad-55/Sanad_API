using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Lookups;
using Sanad.Modules.Caregivers.Application.Onboarding;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers.Onboarding;

public sealed class CaregiverOnboardingTests
{
    [Fact]
    public async Task Bootstrap_ShouldPersistOnboardingCaregiver()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var handler =
            new BootstrapCaregiverCommandHandler(
                dbContext);

        UserId userId = UserId.New();

        var result =
            await handler.Handle(
                new BootstrapCaregiverCommand(
                    userId,
                    CaregiverType.Medical),
                default);

        Assert.True(result.IsSuccess);
        Assert.Equal(userId, result.Value.UserId);
        Assert.Equal(CaregiverType.Medical, result.Value.Type);
        Assert.Equal(CaregiverStatus.Onboarding, result.Value.Status);
        Assert.Equal(
            CaregiverAvailability.Unavailable,
            result.Value.Availability);
        Assert.Empty(result.Value.Certificates);
    }

    [Fact]
    public async Task Bootstrap_ShouldRejectSecondProfileForSameUser()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var handler =
            new BootstrapCaregiverCommandHandler(
                dbContext);

        UserId userId = UserId.New();

        await handler.Handle(
            new BootstrapCaregiverCommand(
                userId,
                CaregiverType.Medical),
            default);

        var duplicate =
            await handler.Handle(
                new BootstrapCaregiverCommand(
                    userId,
                    CaregiverType.Medical),
                default);

        Assert.True(duplicate.IsFailure);
        Assert.Equal(
            OnboardingErrors.AlreadyExists,
            duplicate.Error);
    }

    [Fact]
    public async Task GetProfile_ShouldReturnNotFoundBeforeBootstrap()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var handler =
            new GetCaregiverProfileQueryHandler(
                dbContext);

        var result =
            await handler.Handle(
                new GetCaregiverProfileQuery(
                    UserId.New()),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.NotFound,
            result.Error);
    }

    [Fact]
    public async Task UpdateMedicalProfile_ShouldPersistAndReturnProfile()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        ProfessionalTitle title =
            ProfessionalTitle.Create(
                "Consultant",
                "Consultant",
                true);

        Specialization specialization =
            Specialization.Create(
                "Cardiology",
                "Cardiology",
                true,
                CaregiverType.Medical);

        AcademicDegree degree =
            AcademicDegree.Create(
                "Master",
                "Master",
                true);

        dbContext.ProfessionalTitles.Add(title);
        dbContext.Specializations.Add(specialization);
        dbContext.AcademicDegrees.Add(degree);
        await dbContext.SaveChangesAsync();

        UserId userId = await BootstrapAsync(
            dbContext,
            CaregiverType.Medical);

        var handler =
            new UpdateMedicalProfileCommandHandler(
                dbContext);

        var result =
            await handler.Handle(
                new UpdateMedicalProfileCommand(
                    userId,
                    title.Id,
                    7,
                    specialization.Id,
                    degree.Id,
                    "Cairo Hospital",
                    "Experienced nurse.",
                    DateTime.UtcNow),
                default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.MedicalProfile);
        Assert.Null(result.Value.CompanionProfile);
        Assert.Equal(7, result.Value.MedicalProfile!.YearsOfExperience);
        Assert.Equal(
            title.Id,
            result.Value.MedicalProfile.ProfessionalTitleId);
        Assert.Equal(
            specialization.Id,
            result.Value.MedicalProfile.SpecializationId);
        Assert.Equal(
            degree.Id,
            result.Value.MedicalProfile.AcademicDegreeId);
        Assert.Equal(
            "Cairo Hospital",
            result.Value.MedicalProfile.CurrentWorkplace);
    }

    [Fact]
    public async Task UpdateMedicalProfile_ShouldRejectInactiveProfessionalTitle()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        ProfessionalTitle inactiveTitle =
            ProfessionalTitle.Create(
                "Retired Title",
                "Retired Title",
                false);

        Specialization specialization =
            Specialization.Create(
                "Cardiology",
                "Cardiology",
                true,
                CaregiverType.Medical);

        AcademicDegree degree =
            AcademicDegree.Create(
                "Master",
                "Master",
                true);

        dbContext.ProfessionalTitles.Add(inactiveTitle);
        dbContext.Specializations.Add(specialization);
        dbContext.AcademicDegrees.Add(degree);
        await dbContext.SaveChangesAsync();

        UserId userId = await BootstrapAsync(
            dbContext,
            CaregiverType.Medical);

        var handler =
            new UpdateMedicalProfileCommandHandler(
                dbContext);

        var result =
            await handler.Handle(
                new UpdateMedicalProfileCommand(
                    userId,
                    inactiveTitle.Id,
                    7,
                    specialization.Id,
                    degree.Id,
                    null,
                    null,
                    DateTime.UtcNow),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.InactiveLookup,
            result.Error);
    }

    [Fact]
    public async Task UpdateMedicalProfile_ShouldRejectWrongCaregiverType()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        UserId userId = await BootstrapAsync(
            dbContext,
            CaregiverType.Companion);

        var handler =
            new UpdateMedicalProfileCommandHandler(
                dbContext);

        var result =
            await handler.Handle(
                new UpdateMedicalProfileCommand(
                    userId,
                    ProfessionalTitleId.New(),
                    5,
                    SpecializationId.New(),
                    AcademicDegreeId.New(),
                    null,
                    null,
                    DateTime.UtcNow),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.WrongCaregiverType,
            result.Error);
    }

    [Fact]
    public async Task UpdateMedicalProfile_ShouldReturnLookupsNotFoundForMissingReference()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        UserId userId = await BootstrapAsync(
            dbContext,
            CaregiverType.Medical);

        var handler =
            new UpdateMedicalProfileCommandHandler(
                dbContext);

        var result =
            await handler.Handle(
                new UpdateMedicalProfileCommand(
                    userId,
                    ProfessionalTitleId.New(),
                    5,
                    SpecializationId.New(),
                    AcademicDegreeId.New(),
                    null,
                    null,
                    DateTime.UtcNow),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            LookupsErrors.NotFound,
            result.Error);
    }

    [Fact]
    public async Task UpdateAddress_ShouldTrimAndPersist()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        UserId userId = await BootstrapAsync(
            dbContext,
            CaregiverType.Companion);

        var handler =
            new UpdateCaregiverAddressCommandHandler(
                dbContext);

        var result =
            await handler.Handle(
                new UpdateCaregiverAddressCommand(
                    userId,
                    "  12 Tahrir Street  "),
                default);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "12 Tahrir Street",
            result.Value.DetailedAddress);
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