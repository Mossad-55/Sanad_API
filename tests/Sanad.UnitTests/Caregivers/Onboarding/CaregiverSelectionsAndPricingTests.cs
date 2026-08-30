using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Lookups;
using Sanad.Modules.Caregivers.Application.Onboarding;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers.Onboarding;

public sealed class CaregiverSelectionsAndPricingTests
{
    [Fact]
    public async Task UpdateSelections_ShouldPersistBulkSet()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        Service service1 =
            Service.Create("Nursing", "Nursing", "i.png",
                CaregiverType.Medical, true);
        Service service2 =
            Service.Create("Physio", "Physio", "i.png",
                CaregiverType.Medical, true);
        Language language1 =
            Language.Create("ar", "Arabic", "Arabic");
        Language language2 =
            Language.Create("en", "English", "English");
        Governorate governorate =
            Governorate.Create("Giza", "Giza");
        City city =
            City.Create(governorate.Id, "Hawamdiya", "Hawamdiya");
        Area area =
            Area.Create(city.Id, "Center", "Center");

        dbContext.Services.AddRange(service1, service2);
        dbContext.Languages.AddRange(language1, language2);
        dbContext.Governorates.Add(governorate);
        dbContext.Cities.Add(city);
        dbContext.Areas.Add(area);
        await dbContext.SaveChangesAsync();

        UserId userId = await BootstrapAsync(
            dbContext,
            CaregiverType.Medical);

        var handler =
            new UpdateCaregiverSelectionsCommandHandler(
                dbContext);

        var result =
            await handler.Handle(
                new UpdateCaregiverSelectionsCommand(
                    userId,
                    [service1.Id.Value, service2.Id.Value],
                    [language1.Id.Value, language2.Id.Value],
                    [area.Id.Value]),
                default);

        Assert.True(result.IsSuccess);
        Assert.Contains(service1.Id, result.Value.ServiceIds);
        Assert.Contains(service2.Id, result.Value.ServiceIds);
        Assert.Contains(language1.Id, result.Value.LanguageIds);
        Assert.Contains(area.Id, result.Value.AreaIds);
    }

    [Fact]
    public async Task UpdateSelections_ShouldRemoveSelectionsNoLongerDesired()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        Service service1 =
            Service.Create("Nursing", "Nursing", "i.png",
                CaregiverType.Medical, true);
        Service service2 =
            Service.Create("Physio", "Physio", "i.png",
                CaregiverType.Medical, true);

        dbContext.Services.AddRange(service1, service2);
        await dbContext.SaveChangesAsync();

        UserId userId = await BootstrapAsync(
            dbContext,
            CaregiverType.Medical);

        var handler =
            new UpdateCaregiverSelectionsCommandHandler(
                dbContext);

        await handler.Handle(
            new UpdateCaregiverSelectionsCommand(
                userId,
                [service1.Id.Value, service2.Id.Value],
                [],
                []),
            default);

        var trimmed =
            await handler.Handle(
                new UpdateCaregiverSelectionsCommand(
                    userId,
                    [service1.Id.Value],
                    [],
                    []),
                default);

        Assert.True(trimmed.IsSuccess);
        Assert.Contains(service1.Id, trimmed.Value.ServiceIds);
        Assert.DoesNotContain(
            service2.Id,
            trimmed.Value.ServiceIds);
    }

    [Fact]
    public async Task UpdateSelections_ShouldRejectInactiveService()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        Service inactiveService =
            Service.Create("Old", "Old", "i.png",
                CaregiverType.Medical, false);

        dbContext.Services.Add(inactiveService);
        await dbContext.SaveChangesAsync();

        UserId userId = await BootstrapAsync(
            dbContext,
            CaregiverType.Medical);

        var handler =
            new UpdateCaregiverSelectionsCommandHandler(
                dbContext);

        var result =
            await handler.Handle(
                new UpdateCaregiverSelectionsCommand(
                    userId,
                    [inactiveService.Id.Value],
                    [],
                    []),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.InactiveLookup,
            result.Error);
    }

    [Fact]
    public async Task UpdateSelections_ShouldRejectServiceOfWrongCaregiverType()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        Service companionService =
            Service.Create("Companionship", "Companionship", "i.png",
                CaregiverType.Companion, true);

        dbContext.Services.Add(companionService);
        await dbContext.SaveChangesAsync();

        UserId userId = await BootstrapAsync(
            dbContext,
            CaregiverType.Medical);

        var handler =
            new UpdateCaregiverSelectionsCommandHandler(
                dbContext);

        var result =
            await handler.Handle(
                new UpdateCaregiverSelectionsCommand(
                    userId,
                    [companionService.Id.Value],
                    [],
                    []),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.InactiveLookup,
            result.Error);
    }

    [Fact]
    public async Task UpdateSelections_ShouldRejectMissingReference()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        UserId userId = await BootstrapAsync(
            dbContext,
            CaregiverType.Medical);

        var handler =
            new UpdateCaregiverSelectionsCommandHandler(
                dbContext);

        var result =
            await handler.Handle(
                new UpdateCaregiverSelectionsCommand(
                    userId,
                    [Guid.NewGuid()],
                    [],
                    []),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            LookupsErrors.NotFound,
            result.Error);
    }

    [Fact]
    public async Task UpdateSelections_ShouldRejectAreaWithInactiveParentChain()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        Governorate governorate =
            Governorate.Create("Giza", "Giza");
        City inactiveCity =
            City.Create(governorate.Id, "City", "City");
        inactiveCity.Deactivate();
        Area area =
            Area.Create(inactiveCity.Id, "Area", "Area");

        dbContext.Governorates.Add(governorate);
        dbContext.Cities.Add(inactiveCity);
        dbContext.Areas.Add(area);
        await dbContext.SaveChangesAsync();

        UserId userId = await BootstrapAsync(
            dbContext,
            CaregiverType.Medical);

        var handler =
            new UpdateCaregiverSelectionsCommandHandler(
                dbContext);

        var result =
            await handler.Handle(
                new UpdateCaregiverSelectionsCommand(
                    userId,
                    [],
                    [],
                    [area.Id.Value]),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.InactiveLookup,
            result.Error);
    }

    [Fact]
    public async Task UpdateMedicalPricing_ShouldPersistAndReturnProfile()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        UserId userId = await BootstrapAsync(
            dbContext,
            CaregiverType.Medical);

        var handler =
            new UpdateMedicalPricingCommandHandler(
                dbContext);

        var result =
            await handler.Handle(
                new UpdateMedicalPricingCommand(
                    userId,
                    150.00m,
                    500.00m,
                    700.50m,
                    1200.00m),
                default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.MedicalPricing);
        Assert.Null(result.Value.CompanionPricing);
        Assert.Equal(
            150.00m,
            result.Value.MedicalPricing!.HomeVisitPrice);
        Assert.Equal(
            700.50m,
            result.Value.MedicalPricing.TwelveHourShiftPrice);
    }

    [Fact]
    public async Task UpdateMedicalPricing_ShouldRejectForCompanionCaregiver()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        UserId userId = await BootstrapAsync(
            dbContext,
            CaregiverType.Companion);

        var handler =
            new UpdateMedicalPricingCommandHandler(
                dbContext);

        var result =
            await handler.Handle(
                new UpdateMedicalPricingCommand(
                    userId,
                    150m,
                    500m,
                    700m,
                    1200m),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.WrongCaregiverType,
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