using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.PayoutAccounts;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers;

public sealed class PayoutAccountHandlerTests
{
    [Fact]
    public async Task Update_ShouldCreatePendingAccountWithMaskedIban()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        Caregiver caregiver = await AddCaregiverAsync(dbContext);
        await AddBankAsync(dbContext, "NBE", true);

        var handler = new UpdateCaregiverPayoutAccountCommandHandler(dbContext);

        var result =
            await handler.Handle(
                new UpdateCaregiverPayoutAccountCommand(
                    caregiver.Id.Value,
                    caregiver.UserId,
                    "Mohamed Ahmed",
                    "nbe",
                    "GB29NWBK60161331926819"),
                default);

        Assert.True(result.IsSuccess);
        Assert.Equal("Mohamed Ahmed", result.Value.AccountHolderName);
        Assert.Equal("NBE", result.Value.BankCode);
        Assert.Equal("****6819", result.Value.MaskedIban);
        Assert.Equal("Pending", result.Value.Status);

        Assert.Equal(
            1,
            await dbContext.PayoutAccounts.CountAsync());
    }

    [Fact]
    public async Task Update_ShouldResubmitExistingAccountToPending()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        Caregiver caregiver = await AddCaregiverAsync(dbContext);
        await AddBankAsync(dbContext, "NBE", true);
        await AddBankAsync(dbContext, "CIB", true);

        var handler = new UpdateCaregiverPayoutAccountCommandHandler(dbContext);

        await handler.Handle(
            new UpdateCaregiverPayoutAccountCommand(
                caregiver.Id.Value,
                caregiver.UserId,
                "Mohamed Ahmed",
                "NBE",
                "GB29NWBK60161331926819"),
            default);

        var result =
            await handler.Handle(
                new UpdateCaregiverPayoutAccountCommand(
                    caregiver.Id.Value,
                    caregiver.UserId,
                    "Mohamed Ahmed",
                    "CIB",
                    "DE89370400440532013000"),
                default);

        Assert.True(result.IsSuccess);
        Assert.Equal("CIB", result.Value.BankCode);
        Assert.Equal("****3000", result.Value.MaskedIban);
        Assert.Equal("Pending", result.Value.Status);

        Assert.Equal(
            1,
            await dbContext.PayoutAccounts.CountAsync());
    }

    [Fact]
    public async Task Update_ShouldRejectUnknownBankCode()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        Caregiver caregiver = await AddCaregiverAsync(dbContext);
        await AddBankAsync(dbContext, "NBE", true);

        var handler = new UpdateCaregiverPayoutAccountCommandHandler(dbContext);

        var result =
            await handler.Handle(
                new UpdateCaregiverPayoutAccountCommand(
                    caregiver.Id.Value,
                    caregiver.UserId,
                    "Mohamed Ahmed",
                    "XXX",
                    "GB29NWBK60161331926819"),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(PayoutAccountErrors.UnknownBank, result.Error);
    }

    [Fact]
    public async Task Update_ShouldRejectInactiveBank()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        Caregiver caregiver = await AddCaregiverAsync(dbContext);
        await AddBankAsync(dbContext, "NBE", false);

        var handler = new UpdateCaregiverPayoutAccountCommandHandler(dbContext);

        var result =
            await handler.Handle(
                new UpdateCaregiverPayoutAccountCommand(
                    caregiver.Id.Value,
                    caregiver.UserId,
                    "Mohamed Ahmed",
                    "NBE",
                    "GB29NWBK60161331926819"),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(PayoutAccountErrors.InactiveBank, result.Error);
    }

    [Fact]
    public async Task Update_ShouldRejectInvalidIban()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        Caregiver caregiver = await AddCaregiverAsync(dbContext);
        await AddBankAsync(dbContext, "NBE", true);

        var handler = new UpdateCaregiverPayoutAccountCommandHandler(dbContext);

        var result =
            await handler.Handle(
                new UpdateCaregiverPayoutAccountCommand(
                    caregiver.Id.Value,
                    caregiver.UserId,
                    "Mohamed Ahmed",
                    "NBE",
                    "GB29NWBK60161331926818"),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(PayoutAccountErrors.InvalidIban, result.Error);
    }

    [Fact]
    public async Task Update_ShouldRejectWrongActor()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        Caregiver caregiver = await AddCaregiverAsync(dbContext);
        await AddBankAsync(dbContext, "NBE", true);

        var handler = new UpdateCaregiverPayoutAccountCommandHandler(dbContext);

        var result =
            await handler.Handle(
                new UpdateCaregiverPayoutAccountCommand(
                    caregiver.Id.Value,
                    UserId.New(),
                    "Mohamed Ahmed",
                    "NBE",
                    "GB29NWBK60161331926819"),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(PayoutAccountErrors.CaregiverNotFound, result.Error);
    }

    [Fact]
    public async Task Get_ShouldReturnMaskedAccount()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        Caregiver caregiver = await AddCaregiverAsync(dbContext);
        await AddBankAsync(dbContext, "NBE", true);

        await new UpdateCaregiverPayoutAccountCommandHandler(dbContext).Handle(
            new UpdateCaregiverPayoutAccountCommand(
                caregiver.Id.Value,
                caregiver.UserId,
                "Mohamed Ahmed",
                "NBE",
                "GB29NWBK60161331926819"),
            default);

        var result =
            await new GetCaregiverPayoutAccountQueryHandler(dbContext).Handle(
                new GetCaregiverPayoutAccountQuery(
                    caregiver.Id.Value,
                    caregiver.UserId),
                default);

        Assert.True(result.IsSuccess);
        Assert.Equal(caregiver.Id.Value, result.Value.CaregiverId);
        Assert.Equal("****6819", result.Value.MaskedIban);
    }

    [Fact]
    public async Task Get_ShouldReturnNotFound_WhenNoAccountSaved()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        Caregiver caregiver = await AddCaregiverAsync(dbContext);

        var result =
            await new GetCaregiverPayoutAccountQueryHandler(dbContext).Handle(
                new GetCaregiverPayoutAccountQuery(
                    caregiver.Id.Value,
                    caregiver.UserId),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(PayoutAccountErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Get_ShouldRejectWrongActor()
    {
        using CaregiversDbContext dbContext = CreateDbContext();

        Caregiver caregiver = await AddCaregiverAsync(dbContext);

        var result =
            await new GetCaregiverPayoutAccountQueryHandler(dbContext).Handle(
                new GetCaregiverPayoutAccountQuery(
                    caregiver.Id.Value,
                    UserId.New()),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(PayoutAccountErrors.CaregiverNotFound, result.Error);
    }

    private static async Task<Caregiver> AddCaregiverAsync(
        CaregiversDbContext dbContext)
    {
        Caregiver caregiver =
            Caregiver.Create(UserId.New(), CaregiverType.Medical);

        dbContext.Caregivers.Add(caregiver);
        await dbContext.SaveChangesAsync();

        return caregiver;
    }

    private static async Task AddBankAsync(
        CaregiversDbContext dbContext,
        string code,
        bool isActive)
    {
        Bank bank = Bank.Create(
            code,
            $"بنك {code}",
            $"Bank {code}");

        if (!isActive)
        {
            bank.Deactivate();
        }

        dbContext.Banks.Add(bank);
        await dbContext.SaveChangesAsync();
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
