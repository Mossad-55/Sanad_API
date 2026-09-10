using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.Account;

public sealed class UpdateMyUiLanguageCommandHandlerTests
{
    [Fact]
    public async Task Handle_ShouldPersistChangeAndReturnNewValue()
    {
        string databaseName =
            Guid.NewGuid()
                .ToString();

        IdentityTestDbContext seedContext =
            CreateDbContext(databaseName);

        User user =
            CreateUser();

        seedContext.Users.Add(user);

        await seedContext.SaveChangesAsync();

        await using IdentityTestDbContext handlerContext =
            CreateDbContext(databaseName);

        UpdateMyUiLanguageCommandHandler handler =
            new(
                handlerContext,
                new FixedDateTimeProvider());

        UpdateMyUiLanguageCommand command =
            new(
                user.Id,
                UiLanguage.English);

        Result<UiLanguageResponse> result =
            await handler.Handle(
                command,
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            UiLanguage.English,
            result.Value.UiLanguage);

        Assert.Equal(
            1,
            handlerContext.SaveChangesCalls);

        await using IdentityTestDbContext verificationContext =
            CreateDbContext(databaseName);

        User persistedUser =
            await verificationContext.Users
                .SingleAsync();

        Assert.Equal(
            UiLanguage.English,
            persistedUser.UiLanguage);

        Assert.Equal(
            FixedDateTimeProvider.UtcNowValue,
            persistedUser.UpdatedOnUtc);
    }

    [Fact]
    public async Task Handle_ShouldFailWhenUserIsBlocked()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            CreateUser();

        user.Block(
            "Security block.",
            CreateUtcDateTime());

        dbContext.Users.Add(user);

        await dbContext.SaveChangesAsync();

        dbContext.ResetSaveChangesCalls();

        UpdateMyUiLanguageCommandHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider());

        UpdateMyUiLanguageCommand command =
            new(
                user.Id,
                UiLanguage.English);

        Result<UiLanguageResponse> result =
            await handler.Handle(
                command,
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            AccountErrors.InvalidOperation,
            result.Error);

        Assert.Equal(
            UiLanguage.Arabic,
            user.UiLanguage);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    private static IdentityTestDbContext CreateDbContext()
    {
        return CreateDbContext(
            Guid.NewGuid()
                .ToString());
    }

    private static IdentityTestDbContext CreateDbContext(
        string databaseName)
    {
        DbContextOptions<IdentityTestDbContext>
            options =
                new DbContextOptionsBuilder<
                    IdentityTestDbContext>()
                    .UseInMemoryDatabase(
                        databaseName)
                    .Options;

        return new IdentityTestDbContext(
            options);
    }

    private static User CreateUser()
    {
        return User.Create(
            FullName.Create("محمد أحمد"),
            FullName.Create("Mohamed Ahmed"),
            Email.Create(
                "mohamed@example.com"),
            PhoneNumber.Create(
                "+201001234567"));
    }

    private static DateTime CreateUtcDateTime()
    {
        return new DateTime(
            2026,
            8,
            20,
            10,
            0,
            0,
            DateTimeKind.Utc);
    }

    private sealed class FixedDateTimeProvider :
        IDateTimeProvider
    {
        internal static readonly DateTime
            UtcNowValue =
                new(
                    2026,
                    8,
                    20,
                    10,
                    5,
                    0,
                    DateTimeKind.Utc);

        public DateTime UtcNow =>
            UtcNowValue;
    }
}
