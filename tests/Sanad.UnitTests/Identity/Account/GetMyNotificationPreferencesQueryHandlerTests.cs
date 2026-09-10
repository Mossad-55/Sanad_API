using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.Account;

public sealed class GetMyNotificationPreferencesQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShouldReturnAllOnDefaultsForNewUser()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            CreateUser();

        dbContext.Users.Add(user);

        await dbContext.SaveChangesAsync();

        GetMyNotificationPreferencesQueryHandler handler =
            new(dbContext);

        Result<NotificationPreferencesResponse> result =
            await handler.Handle(
                new GetMyNotificationPreferencesQuery(
                    user.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.True(
            result.Value.CheckInAlerts);

        Assert.True(
            result.Value.MedicationReminders);

        Assert.True(
            result.Value.BookingUpdates);

        Assert.True(
            result.Value.CommunityNotifications);
    }

    [Fact]
    public async Task Handle_ShouldFailWhenUserNotFound()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        GetMyNotificationPreferencesQueryHandler handler =
            new(dbContext);

        Result<NotificationPreferencesResponse> result =
            await handler.Handle(
                new GetMyNotificationPreferencesQuery(
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
