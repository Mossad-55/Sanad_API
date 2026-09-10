using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.Account;

public sealed class GetMyProfileQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShouldReturnCallerProfile()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            CreateUser();

        user.AddAccount(
            AccountType.Family);

        user.VerifyEmail(
            CreateUtcDateTime());

        dbContext.Users.Add(user);

        await dbContext.SaveChangesAsync();

        GetMyProfileQueryHandler handler =
            new(dbContext);

        Result<MyProfileResponse> result =
            await handler.Handle(
                new GetMyProfileQuery(
                    user.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            "محمد أحمد",
            result.Value.ArabicFullName);

        Assert.Equal(
            "Mohamed Ahmed",
            result.Value.EnglishFullName);

        Assert.Equal(
            "mohamed@example.com",
            result.Value.Email);

        Assert.Equal(
            "+201001234567",
            result.Value.PhoneNumber);

        Assert.Equal(
            AccountType.Family,
            result.Value.AccountType);

        Assert.True(result.Value.EmailVerified);
        Assert.False(result.Value.PhoneVerified);

        Assert.Null(
            result.Value.AvatarUrl);
    }

    [Fact]
    public async Task Handle_ShouldReturnStoredAvatarPath()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            CreateUser();

        user.AddAccount(
            AccountType.Family);

        user.ChangeAvatar(
            "avatars/profile/test.png");

        dbContext.Users.Add(user);

        await dbContext.SaveChangesAsync();

        GetMyProfileQueryHandler handler =
            new(dbContext);

        Result<MyProfileResponse> result =
            await handler.Handle(
                new GetMyProfileQuery(
                    user.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            "avatars/profile/test.png",
            result.Value.AvatarUrl);
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
}
