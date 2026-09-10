using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.Account;

public sealed class UpdateMyNotificationPreferencesCommandHandlerTests
{
    [Fact]
    public async Task Handle_ShouldPersistChangesAndReturnNewValues()
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

        UpdateMyNotificationPreferencesCommandHandler handler =
            new(
                handlerContext,
                new FixedDateTimeProvider());

        UpdateMyNotificationPreferencesCommand command =
            new(
                user.Id,
                CheckInAlerts: true,
                MedicationReminders: false,
                BookingUpdates: true,
                CommunityNotifications: false);

        Result<NotificationPreferencesResponse> result =
            await handler.Handle(
                command,
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.True(
            result.Value.CheckInAlerts);

        Assert.False(
            result.Value.MedicationReminders);

        Assert.True(
            result.Value.BookingUpdates);

        Assert.False(
            result.Value.CommunityNotifications);

        Assert.Equal(
            1,
            handlerContext.SaveChangesCalls);

        await using IdentityTestDbContext verificationContext =
            CreateDbContext(databaseName);

        User persistedUser =
            await verificationContext.Users
                .SingleAsync();

        Assert.True(
            persistedUser.NotificationPreferences.CheckInAlerts);

        Assert.False(
            persistedUser.NotificationPreferences.MedicationReminders);

        Assert.True(
            persistedUser.NotificationPreferences.BookingUpdates);

        Assert.False(
            persistedUser.NotificationPreferences.CommunityNotifications);

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

        UpdateMyNotificationPreferencesCommandHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider());

        UpdateMyNotificationPreferencesCommand command =
            new(
                user.Id,
                CheckInAlerts: false,
                MedicationReminders: false,
                BookingUpdates: false,
                CommunityNotifications: false);

        Result<NotificationPreferencesResponse> result =
            await handler.Handle(
                command,
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            AccountErrors.InvalidOperation,
            result.Error);

        Assert.True(
            user.NotificationPreferences.CheckInAlerts);

        Assert.True(
            user.NotificationPreferences.MedicationReminders);

        Assert.True(
            user.NotificationPreferences.BookingUpdates);

        Assert.True(
            user.NotificationPreferences.CommunityNotifications);

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
