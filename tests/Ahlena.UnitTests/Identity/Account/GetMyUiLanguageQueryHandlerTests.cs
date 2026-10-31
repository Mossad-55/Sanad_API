using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Identity.Application.Users;
using Ahlena.Modules.Identity.Domain.Users;
using Ahlena.UnitTests.Identity.Registration;

namespace Ahlena.UnitTests.Identity.Account;

public sealed class GetMyUiLanguageQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShouldReturnDefaultArabicForNewUser()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            CreateUser();

        dbContext.Users.Add(user);

        await dbContext.SaveChangesAsync();

        GetMyUiLanguageQueryHandler handler =
            new(dbContext);

        Result<UiLanguageResponse> result =
            await handler.Handle(
                new GetMyUiLanguageQuery(
                    user.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            UiLanguage.Arabic,
            result.Value.UiLanguage);
    }

    [Fact]
    public async Task Handle_ShouldFailWhenUserNotFound()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        GetMyUiLanguageQueryHandler handler =
            new(dbContext);

        Result<UiLanguageResponse> result =
            await handler.Handle(
                new GetMyUiLanguageQuery(
                    UserId.New()),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            AccountErrors.UserNotFound,
            result.Error);
    }

    private static IdentityTestDbContext CreateDbContext()
    {
        DbContextOptions<IdentityTestDbContext>
            options =
                new DbContextOptionsBuilder<
                    IdentityTestDbContext>()
                    .UseInMemoryDatabase(
                        Guid.NewGuid()
                            .ToString())
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
}
