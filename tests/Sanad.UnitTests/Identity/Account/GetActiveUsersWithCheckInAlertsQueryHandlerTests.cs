using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.Account;

public sealed class GetActiveUsersWithCheckInAlertsQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShouldReturnOnlyActiveUsersWithCheckInAlertsEnabled()
    {
        await using var db = new IdentityTestDbContext(
            new DbContextOptionsBuilder<IdentityTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        var enabled = CreateActiveUser();
        var disabled = CreateActiveUser();
        disabled.ChangeNotificationPreferences(
            NotificationPreferences.Create(false, true, true, true, true, true, true, true), DateTime.UtcNow);
        var suspended = CreateActiveUser();
        suspended.Suspend("temporarily unavailable", DateTime.UtcNow);
        db.Users.AddRange(enabled, disabled, suspended);
        await db.SaveChangesAsync();

        var result = await new GetActiveUsersWithCheckInAlertsQueryHandler(db).Handle(
            new GetActiveUsersWithCheckInAlertsQuery([enabled.Id, disabled.Id, suspended.Id, enabled.Id]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([enabled.Id], result.Value);
    }

    [Fact]
    public async Task HelpRequestAlertsQuery_ShouldReturnOnlyActiveUsersWithPreferenceEnabled()
    {
        await using var db = new IdentityTestDbContext(
            new DbContextOptionsBuilder<IdentityTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        var enabled = CreateActiveUser();
        var disabled = CreateActiveUser();
        disabled.ChangeNotificationPreferences(
            NotificationPreferences.Create(true, true, true, true, true, true, true, true, helpRequestAlerts: false), DateTime.UtcNow);
        var suspended = CreateActiveUser();
        suspended.Suspend("temporarily unavailable", DateTime.UtcNow);
        db.Users.AddRange(enabled, disabled, suspended);
        await db.SaveChangesAsync();

        var result = await new GetActiveUsersWithHelpRequestAlertsQueryHandler(db).Handle(
            new GetActiveUsersWithHelpRequestAlertsQuery([enabled.Id, disabled.Id, suspended.Id, enabled.Id]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([enabled.Id], result.Value);
    }

    private static User CreateActiveUser() => User.CreateElderly(
        FullName.Create("عمر"),
        FullName.Create("Family member"),
        PhoneNumber.Create($"+2010{Random.Shared.Next(10000000, 99999999)}"),
        Gender.Male,
        new DateOnly(1980, 1, 1),
        DateTime.UtcNow);
}
