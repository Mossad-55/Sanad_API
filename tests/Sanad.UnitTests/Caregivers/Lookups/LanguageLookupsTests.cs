using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Lookups;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using static Sanad.Modules.Caregivers.Application.Lookups.GetActiveLanguagesQueryHandler;

namespace Sanad.UnitTests.Caregivers.Lookups;

public sealed class LanguageLookupsTests
{
    [Fact]
    public async Task Create_ShouldPersistLanguageAsActiveWithLowerCode()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        var handler = new CreateLanguageCommandHandler(dbContext);

        var result =
            await handler.Handle(
                new CreateLanguageCommand(
                    "AR",
                    "العربية",
                    "Arabic"),
                default);

        Assert.True(result.IsSuccess);
        Assert.Equal("ar", result.Value.Code);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task GetAll_ShouldReturnActiveAndInactive()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        var createHandler = new CreateLanguageCommandHandler(dbContext);

        await createHandler.Handle(
            new CreateLanguageCommand("ar", "العربية", "Arabic"),
            default);

        var english =
            await createHandler.Handle(
                new CreateLanguageCommand("en", "الإنجليزية", "English"),
                default);

        await new SetLanguageActiveCommandHandler(dbContext).Handle(
            new SetLanguageActiveCommand(english.Value.Id, false),
            default);

        var result =
            await new GetAllLanguagesQueryHandler(dbContext).Handle(
                new GetAllLanguagesQuery(),
                default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Contains(
            result.Value,
            item => item.Code == "en" && !item.IsActive);
    }

    [Fact]
    public async Task Create_ShouldRejectDuplicateCode()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        var handler = new CreateLanguageCommandHandler(dbContext);

        await handler.Handle(
            new CreateLanguageCommand("ar", "العربية", "Arabic"),
            default);

        var result =
            await handler.Handle(
                new CreateLanguageCommand("AR", "عربي آخر", "Other Arabic"),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            LookupsErrors.LanguageCodeInUse,
            result.Error);
    }

    [Fact]
    public async Task Create_ShouldRejectDuplicateName()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        var handler = new CreateLanguageCommandHandler(dbContext);

        await handler.Handle(
            new CreateLanguageCommand("ar", "العربية", "Arabic"),
            default);

        var result =
            await handler.Handle(
                new CreateLanguageCommand("fr", "العربية", "Arabic"),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            LookupsErrors.NameAlreadyInUse,
            result.Error);
    }

    [Fact]
    public async Task Rename_ShouldRejectDuplicateNameExcludingSelf()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        var createHandler = new CreateLanguageCommandHandler(dbContext);

        var arabic =
            await createHandler.Handle(
                new CreateLanguageCommand("ar", "العربية", "Arabic"),
                default);

        await createHandler.Handle(
            new CreateLanguageCommand("fr", "الفرنسية", "French"),
            default);

        var renameHandler = new RenameLanguageCommandHandler(dbContext);

        var result =
            await renameHandler.Handle(
                new RenameLanguageCommand(
                    arabic.Value.Id,
                    "الفرنسية",
                    "French"),
                default);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task SetActive_ShouldReturnNotFoundForMissingLanguage()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        var handler = new SetLanguageActiveCommandHandler(dbContext);

        var result =
            await handler.Handle(
                new SetLanguageActiveCommand(
                    LanguageId.New(),
                    false),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(LookupsErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task GetActive_ShouldOnlyReturnActive()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        var createHandler = new CreateLanguageCommandHandler(dbContext);

        await createHandler.Handle(
            new CreateLanguageCommand("ar", "العربية", "Arabic"),
            default);

        var english =
            await createHandler.Handle(
                new CreateLanguageCommand("en", "الإنجليزية", "English"),
                default);

        var setHandler = new SetLanguageActiveCommandHandler(dbContext);

        await setHandler.Handle(
            new SetLanguageActiveCommand(
                english.Value.Id,
                false),
            default);

        var queryHandler = new GetActiveLanguagesQueryHandler(dbContext);

        var result =
            await queryHandler.Handle(
                new GetActiveLanguagesQuery(),
                default);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("ar", result.Value[0].Code);
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