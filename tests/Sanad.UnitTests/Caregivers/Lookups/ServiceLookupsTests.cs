using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Caregivers.Application.Lookups;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers.Lookups;

public sealed class ServiceLookupsTests
{
    [Fact]
    public async Task Create_ShouldPersistService()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var handler =
            new CreateServiceCommandHandler(
                dbContext);

        var result =
            await handler.Handle(
                new CreateServiceCommand(
                    "Doctor",
                    "Doctor",
                    "services/doctor.png",
                    CaregiverType.Medical,
                    true),
                default);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "Doctor",
            result.Value.ArabicName);
        Assert.Equal(
            CaregiverType.Medical,
            result.Value.CaregiverType);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task Create_ShouldRejectDuplicateWithinSameType()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var handler =
            new CreateServiceCommandHandler(
                dbContext);

        await handler.Handle(
            new CreateServiceCommand(
                "Doctor", "Doctor", "i.png",
                CaregiverType.Medical, true),
            default);

        var result =
            await handler.Handle(
                new CreateServiceCommand(
                    "Doctor", "Doctor", "i.png",
                    CaregiverType.Medical, true),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            LookupsErrors.NameAlreadyInUse,
            result.Error);
    }

    [Fact]
    public async Task Create_ShouldAllowSameNameAcrossTypes()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var handler =
            new CreateServiceCommandHandler(
                dbContext);

        var medical =
            await handler.Handle(
                new CreateServiceCommand(
                    "Doctor", "Doctor", "i.png",
                    CaregiverType.Medical, true),
                default);

        var companion =
            await handler.Handle(
                new CreateServiceCommand(
                    "Doctor", "Doctor", "i.png",
                    CaregiverType.Companion, true),
                default);

        Assert.True(medical.IsSuccess);
        Assert.True(companion.IsSuccess);
    }

    [Fact]
    public async Task Rename_ShouldRejectDuplicateWithinSameTypeExcludingSelf()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var createHandler =
            new CreateServiceCommandHandler(
                dbContext);

        var created =
            await createHandler.Handle(
                new CreateServiceCommand(
                    "Doctor", "Doctor", "i.png",
                    CaregiverType.Medical, true),
                default);

        await createHandler.Handle(
            new CreateServiceCommand(
                "Nurse", "Nurse", "i.png",
                CaregiverType.Medical, true),
            default);

        var renameHandler =
            new RenameServiceCommandHandler(
                dbContext);

        var result =
            await renameHandler.Handle(
                new RenameServiceCommand(
                    created.Value.Id,
                    "Nurse",
                    "Nurse"),
                default);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task SetActive_ShouldToggleIsActive()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var createHandler =
            new CreateServiceCommandHandler(
                dbContext);

        var created =
            await createHandler.Handle(
                new CreateServiceCommand(
                    "Doctor", "Doctor", "i.png",
                    CaregiverType.Medical, true),
                default);

        var setHandler =
            new SetServiceActiveCommandHandler(
                dbContext);

        var deactivated =
            await setHandler.Handle(
                new SetServiceActiveCommand(
                    created.Value.Id,
                    false),
                default);

        Assert.False(
            deactivated.Value.IsActive);

        var activated =
            await setHandler.Handle(
                new SetServiceActiveCommand(
                    created.Value.Id,
                    true),
                default);

        Assert.True(
            activated.Value.IsActive);
    }

    [Fact]
    public async Task GetActive_ShouldOnlyReturnActive()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var createHandler =
            new CreateServiceCommandHandler(
                dbContext);

        await createHandler.Handle(
            new CreateServiceCommand(
                "Doctor", "Doctor", "i.png",
                CaregiverType.Medical, true),
            default);

        await createHandler.Handle(
            new CreateServiceCommand(
                "Nurse", "Nurse", "i.png",
                CaregiverType.Medical, false),
            default);

        var handler =
            new GetActiveServicesQueryHandler(
                dbContext);

        var result =
            await handler.Handle(
                new GetActiveServicesQuery(),
                default);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(
            "Doctor",
            result.Value[0].ArabicName);
    }

    private static CaregiversDbContext CreateDbContext()
    {
        DbContextOptions<CaregiversDbContext> options =
            new DbContextOptionsBuilder<
                CaregiversDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString())
                .Options;

        return new CaregiversDbContext(
            options);
    }
}