using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Lookups;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers.Lookups;

public sealed class BankLookupsTests
{
    [Fact]
    public async Task Create_ShouldPersistBankAsActiveWithUpperCode()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        var handler = new CreateBankCommandHandler(dbContext);

        var result =
            await handler.Handle(
                new CreateBankCommand(
                    "nbe",
                    "البنك الأهلي المصري",
                    "National Bank of Egypt"),
                default);

        Assert.True(result.IsSuccess);
        Assert.Equal("NBE", result.Value.Code);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task Create_ShouldRejectDuplicateCode()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        var handler = new CreateBankCommandHandler(dbContext);

        await handler.Handle(
            new CreateBankCommand(
                "NBE",
                "البنك الأهلي المصري",
                "National Bank of Egypt"),
            default);

        var result =
            await handler.Handle(
                new CreateBankCommand(
                    "nbe",
                    "بنك آخر",
                    "Other Bank"),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(LookupsErrors.BankCodeInUse, result.Error);
    }

    [Fact]
    public async Task Create_ShouldRejectDuplicateName()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        var handler = new CreateBankCommandHandler(dbContext);

        await handler.Handle(
            new CreateBankCommand(
                "NBE",
                "البنك الأهلي المصري",
                "National Bank of Egypt"),
            default);

        var result =
            await handler.Handle(
                new CreateBankCommand(
                    "CIB",
                    "البنك الأهلي المصري",
                    "National Bank of Egypt"),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(LookupsErrors.NameAlreadyInUse, result.Error);
    }

    [Fact]
    public async Task Rename_ShouldKeepCodeAndUpdateNames()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        var createHandler = new CreateBankCommandHandler(dbContext);

        var created =
            await createHandler.Handle(
                new CreateBankCommand(
                    "NBE",
                    "البنك الأهلي المصري",
                    "National Bank of Egypt"),
                default);

        var result =
            await new RenameBankCommandHandler(dbContext).Handle(
                new RenameBankCommand(
                    created.Value.Id,
                    "البنك الأهلي",
                    "National Bank"),
                default);

        Assert.True(result.IsSuccess);
        Assert.Equal("NBE", result.Value.Code);
        Assert.Equal("البنك الأهلي", result.Value.ArabicName);
        Assert.Equal("National Bank", result.Value.EnglishName);
    }

    [Fact]
    public async Task SetActive_ShouldToggleVisibility()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        var createHandler = new CreateBankCommandHandler(dbContext);

        var created =
            await createHandler.Handle(
                new CreateBankCommand(
                    "NBE",
                    "البنك الأهلي المصري",
                    "National Bank of Egypt"),
                default);

        var deactivated =
            await new SetBankActiveCommandHandler(dbContext).Handle(
                new SetBankActiveCommand(created.Value.Id, false),
                default);

        Assert.True(deactivated.IsSuccess);
        Assert.False(deactivated.Value.IsActive);

        var reactivated =
            await new SetBankActiveCommandHandler(dbContext).Handle(
                new SetBankActiveCommand(created.Value.Id, true),
                default);

        Assert.True(reactivated.IsSuccess);
        Assert.True(reactivated.Value.IsActive);
    }

    [Fact]
    public async Task GetActive_ShouldOnlyReturnActiveOrderedByCode()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        var createHandler = new CreateBankCommandHandler(dbContext);

        await createHandler.Handle(
            new CreateBankCommand(
                "CIB",
                "البنك التجاري الدولي",
                "Commercial International Bank"),
            default);

        var hidden =
            await createHandler.Handle(
                new CreateBankCommand(
                    "NBE",
                    "البنك الأهلي المصري",
                    "National Bank of Egypt"),
                default);

        await new SetBankActiveCommandHandler(dbContext).Handle(
            new SetBankActiveCommand(hidden.Value.Id, false),
            default);

        var result =
            await new GetActiveBanksQueryHandler(dbContext).Handle(
                new GetActiveBanksQuery(),
                default);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value);
        Assert.Equal("CIB", item.Code);
        Assert.Equal("البنك التجاري الدولي", item.ArabicName);
        Assert.Equal("Commercial International Bank", item.EnglishName);
    }

    [Fact]
    public async Task GetAll_ShouldReturnActiveAndInactive()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        var createHandler = new CreateBankCommandHandler(dbContext);

        await createHandler.Handle(
            new CreateBankCommand(
                "CIB",
                "البنك التجاري الدولي",
                "Commercial International Bank"),
            default);

        var hidden =
            await createHandler.Handle(
                new CreateBankCommand(
                    "NBE",
                    "البنك الأهلي المصري",
                    "National Bank of Egypt"),
                default);

        await new SetBankActiveCommandHandler(dbContext).Handle(
            new SetBankActiveCommand(hidden.Value.Id, false),
            default);

        var result =
            await new GetAllBanksQueryHandler(dbContext).Handle(
                new GetAllBanksQuery(),
                default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Contains(
            result.Value,
            item => item.Code == "NBE" && !item.IsActive);
    }

    [Fact]
    public async Task Rename_ShouldReturnNotFound_WhenBankIsUnknown()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        var result =
            await new RenameBankCommandHandler(dbContext).Handle(
                new RenameBankCommand(
                    BankId.New(),
                    "بنك",
                    "Bank"),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(LookupsErrors.NotFound, result.Error);
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
