using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Identity.Application.Users;
using Ahlena.Modules.Identity.Domain.Users;
using Ahlena.UnitTests.Identity.Registration;

namespace Ahlena.UnitTests.Identity.Account;

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

        Assert.True(
            result.Value.FamilyActivityAlerts);

        Assert.True(
            result.Value.NewOrders);

        Assert.True(
            result.Value.MessagesFromFamilies);

        Assert.True(
            result.Value.SystemNotifications);
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
