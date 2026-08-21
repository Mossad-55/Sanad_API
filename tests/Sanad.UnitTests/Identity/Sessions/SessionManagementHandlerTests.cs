using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Authentication.Sessions;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.Sessions;

public sealed class SessionManagementHandlerTests
{
    [Fact]
    public async Task LogoutCurrent_ShouldRevokeOwnSession()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserAsync(
                dbContext);

        DeviceSession session =
            await SeedSessionAsync(
                dbContext,
                user.Id);

        dbContext.ResetSaveChangesCalls();

        LogoutCurrentSessionCommandHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider());

        Result result =
            await handler.Handle(
                new LogoutCurrentSessionCommand(
                    session.Id,
                    user.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(session.IsRevoked);
        Assert.Equal(
            "User logged out.",
            session.RevocationReason);
        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task LogoutCurrent_ShouldBeIdempotent()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserAsync(
                dbContext);

        DeviceSession session =
            await SeedSessionAsync(
                dbContext,
                user.Id);

        session.Revoke(
            "Previously revoked.",
            FixedDateTimeProvider.UtcNowValue
                .AddMinutes(-5));

        await dbContext.SaveChangesAsync();

        dbContext.ResetSaveChangesCalls();

        LogoutCurrentSessionCommandHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider());

        Result result =
            await handler.Handle(
                new LogoutCurrentSessionCommand(
                    session.Id,
                    user.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task LogoutCurrent_ShouldRejectWhenSessionNotFound()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserAsync(
                dbContext);

        LogoutCurrentSessionCommandHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider());

        Result result =
            await handler.Handle(
                new LogoutCurrentSessionCommand(
                    DeviceSessionId.New(),
                    user.Id),
                CancellationToken.None);

        Assert.Equal(
            SessionManagementErrors.SessionNotFound,
            result.Error);
    }

    [Fact]
    public async Task LogoutCurrent_ShouldRejectWhenNotOwned()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User owner =
            await SeedActiveUserAsync(
                dbContext);

        DeviceSession session =
            await SeedSessionAsync(
                dbContext,
                owner.Id);

        LogoutCurrentSessionCommandHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider());

        Result result =
            await handler.Handle(
                new LogoutCurrentSessionCommand(
                    session.Id,
                    UserId.New()),
                CancellationToken.None);

        Assert.Equal(
            SessionManagementErrors.SessionNotOwned,
            result.Error);

        Assert.False(session.IsRevoked);
    }

    [Fact]
    public async Task LogoutAll_ShouldRevokeEveryNonRevokedSession()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserAsync(
                dbContext);

        DeviceSession session1 =
            await SeedSessionAsync(
                dbContext,
                user.Id,
                "hash-1");

        DeviceSession session2 =
            await SeedSessionAsync(
                dbContext,
                user.Id,
                "hash-2");

        DeviceSession alreadyRevoked =
            await SeedSessionAsync(
                dbContext,
                user.Id,
                "hash-3");

        alreadyRevoked.Revoke(
            "Old logout.",
            FixedDateTimeProvider.UtcNowValue
                .AddDays(-10));

        await dbContext.SaveChangesAsync();

        DateTime revokedBefore =
            alreadyRevoked.RevokedOnUtc!.Value;

        dbContext.ResetSaveChangesCalls();

        LogoutAllSessionsCommandHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider());

        Result result =
            await handler.Handle(
                new LogoutAllSessionsCommand(
                    user.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(session1.IsRevoked);
        Assert.True(session2.IsRevoked);
        Assert.True(alreadyRevoked.IsRevoked);
        Assert.Equal(
            revokedBefore,
            alreadyRevoked.RevokedOnUtc);
        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task LogoutAll_ShouldNotAffectOtherUsersSessions()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User userA =
            await SeedActiveUserAsync(
                dbContext);

        User otherUser =
            User.Create(
                FullName.Create("آخر"),
                FullName.Create("Other"),
                Email.Create("other@example.com"),
                PhoneNumber.Create("+201009999999"));

        otherUser.AddAccount(AccountType.Family);

        otherUser.SetInitialPasswordHash(
            "hash",
            FixedDateTimeProvider.UtcNowValue);

        dbContext.Users.Add(otherUser);

        await dbContext.SaveChangesAsync();

        otherUser.VerifyEmail(
            FixedDateTimeProvider.UtcNowValue);

        otherUser.VerifyPhone(
            FixedDateTimeProvider.UtcNowValue);

        otherUser.Activate(
            FixedDateTimeProvider.UtcNowValue);

        await dbContext.SaveChangesAsync();

        DeviceSession userASession =
            await SeedSessionAsync(
                dbContext,
                userA.Id,
                "user-a-hash");

        DeviceSession otherUserSession =
            await SeedSessionAsync(
                dbContext,
                otherUser.Id,
                "other-hash");

        dbContext.ResetSaveChangesCalls();

        LogoutAllSessionsCommandHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider());

        await handler.Handle(
            new LogoutAllSessionsCommand(
                userA.Id),
            CancellationToken.None);

        Assert.True(userASession.IsRevoked);
        Assert.False(otherUserSession.IsRevoked);
    }

    [Fact]
    public async Task LogoutAll_ShouldSucceedWithNoSessions()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserAsync(
                dbContext);

        dbContext.ResetSaveChangesCalls();

        LogoutAllSessionsCommandHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider());

        Result result =
            await handler.Handle(
                new LogoutAllSessionsCommand(
                    user.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task GetActive_ShouldReturnOnlyNonRevokedAndNonExpired()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserAsync(
                dbContext);

        DeviceSession activeSession =
            await SeedSessionAsync(
                dbContext,
                user.Id,
                "active-hash");

        DeviceSession revokedSession =
            await SeedSessionAsync(
                dbContext,
                user.Id,
                "revoked-hash");

        revokedSession.Revoke(
            "Logged out.",
            FixedDateTimeProvider.UtcNowValue
                .AddDays(-5));

        DeviceSession expiredSession =
            await SeedExpiredSessionAsync(
                dbContext,
                user.Id);

        GetActiveSessionsQueryHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider());

        Result<ActiveSessionsResponse> result =
            await handler.Handle(
                new GetActiveSessionsQuery(
                    user.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        ActiveSessionItem[] items =
            result.Value.Sessions.ToArray();

        Assert.Single(items);

        Assert.Equal(
            activeSession.Id,
            items[0].DeviceSessionId);

        Assert.Equal(
            "iPhone 16",
            items[0].DeviceName);

        Assert.Equal(
            DevicePlatform.iOS,
            items[0].Platform);
    }

    [Fact]
    public async Task GetActive_ShouldNotIncludeRevokedOrExpired()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserAsync(
                dbContext);

        await SeedSessionAsync(
            dbContext,
            user.Id,
            "revoked-hash");

        DeviceSession revokedSession =
            dbContext.DeviceSessions
                .First(item =>
                    item.RefreshTokenHash ==
                    "revoked-hash");

        revokedSession.Revoke(
            "Logged out.",
            FixedDateTimeProvider.UtcNowValue
                .AddDays(-1));

        await SeedExpiredSessionAsync(
            dbContext,
            user.Id);

        dbContext.ResetSaveChangesCalls();

        GetActiveSessionsQueryHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider());

        Result<ActiveSessionsResponse> result =
            await handler.Handle(
                new GetActiveSessionsQuery(
                    user.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Sessions);
    }

    [Fact]
    public async Task GetActive_ShouldReturnEmptyListForUnknownUser()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        GetActiveSessionsQueryHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider());

        Result<ActiveSessionsResponse> result =
            await handler.Handle(
                new GetActiveSessionsQuery(
                    UserId.New()),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Sessions);
    }

    [Fact]
    public async Task GetActive_ShouldOrderOldestFirst()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserAsync(
                dbContext);

        DeviceSession first =
            await SeedSessionAsync(
                dbContext,
                user.Id,
                "first-hash");

        DeviceSession second =
            await SeedSessionAsync(
                dbContext,
                user.Id,
                "second-hash");

        GetActiveSessionsQueryHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider());

        Result<ActiveSessionsResponse> result =
            await handler.Handle(
                new GetActiveSessionsQuery(
                    user.Id),
                CancellationToken.None);

        ActiveSessionItem[] items =
            result.Value.Sessions.ToArray();

        Assert.Equal(2, items.Length);
        Assert.Equal(first.Id, items[0].DeviceSessionId);
        Assert.Equal(second.Id, items[1].DeviceSessionId);
    }

    [Fact]
    public async Task RevokeSession_ShouldRevokeOwnedSession()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserAsync(
                dbContext);

        DeviceSession session =
            await SeedSessionAsync(
                dbContext,
                user.Id);

        dbContext.ResetSaveChangesCalls();

        RevokeSessionCommandHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider());

        Result result =
            await handler.Handle(
                new RevokeSessionCommand(
                    session.Id,
                    user.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(session.IsRevoked);
        Assert.Equal(
            "Revoked by the user.",
            session.RevocationReason);
        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task RevokeSession_ShouldBeIdempotent()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserAsync(
                dbContext);

        DeviceSession session =
            await SeedSessionAsync(
                dbContext,
                user.Id);

        session.Revoke(
            "Already revoked.",
            FixedDateTimeProvider.UtcNowValue
                .AddMinutes(-5));

        await dbContext.SaveChangesAsync();

        dbContext.ResetSaveChangesCalls();

        RevokeSessionCommandHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider());

        Result result =
            await handler.Handle(
                new RevokeSessionCommand(
                    session.Id,
                    user.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task RevokeSession_ShouldRejectWhenNotOwned()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User owner =
            await SeedActiveUserAsync(
                dbContext);

        DeviceSession session =
            await SeedSessionAsync(
                dbContext,
                owner.Id);

        RevokeSessionCommandHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider());

        Result result =
            await handler.Handle(
                new RevokeSessionCommand(
                    session.Id,
                    UserId.New()),
                CancellationToken.None);

        Assert.Equal(
            SessionManagementErrors.SessionNotOwned,
            result.Error);

        Assert.False(session.IsRevoked);
    }

    [Fact]
    public async Task RevokeSession_ShouldRejectWhenNotFound()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserAsync(
                dbContext);

        RevokeSessionCommandHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider());

        Result result =
            await handler.Handle(
                new RevokeSessionCommand(
                    DeviceSessionId.New(),
                    user.Id),
                CancellationToken.None);

        Assert.Equal(
            SessionManagementErrors.SessionNotFound,
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

    private static async Task<User> SeedActiveUserAsync(
        IdentityTestDbContext dbContext)
    {
        User user =
            User.Create(
                FullName.Create(
                    "محمد أحمد"),
                FullName.Create(
                    "Mohamed Ahmed"),
                Email.Create(
                    "mohamed@example.com"),
                PhoneNumber.Create(
                    "+201001234567"));

        user.AddAccount(
            AccountType.Family);

        user.SetInitialPasswordHash(
            "password-hash",
            FixedDateTimeProvider.UtcNowValue
                .AddMinutes(-3));

        dbContext.Users.Add(user);

        await dbContext.SaveChangesAsync();

        user.VerifyEmail(
            FixedDateTimeProvider.UtcNowValue
                .AddMinutes(-2));

        user.VerifyPhone(
            FixedDateTimeProvider.UtcNowValue
                .AddMinutes(-2));

        user.Activate(
            FixedDateTimeProvider.UtcNowValue
                .AddMinutes(-1));

        await dbContext.SaveChangesAsync();

        return user;
    }

    private static async Task<DeviceSession>
        SeedSessionAsync(
            IdentityTestDbContext dbContext,
            UserId userId,
            string refreshTokenHash = "default-hash")
    {
        DateTime createdOnUtc =
            FixedDateTimeProvider.UtcNowValue
                .AddDays(-1);

        DeviceSession session =
            DeviceSession.Create(
                userId,
                "iPhone 16",
                DevicePlatform.iOS,
                "1.0.0",
                refreshTokenHash,
                createdOnUtc,
                FixedDateTimeProvider.UtcNowValue
                    .AddDays(29));

        dbContext.DeviceSessions.Add(session);

        await dbContext.SaveChangesAsync();

        return session;
    }

    private static async Task<DeviceSession>
        SeedExpiredSessionAsync(
            IdentityTestDbContext dbContext,
            UserId userId)
    {
        DeviceSession session =
            DeviceSession.Create(
                userId,
                "Old Phone",
                DevicePlatform.Android,
                "0.9.0",
                "expired-hash",
                FixedDateTimeProvider.UtcNowValue
                    .AddDays(-40),
                FixedDateTimeProvider.UtcNowValue
                    .AddDays(-10));

        dbContext.DeviceSessions.Add(session);

        await dbContext.SaveChangesAsync();

        return session;
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
                    0,
                    0,
                    DateTimeKind.Utc);

        public DateTime UtcNow =>
            UtcNowValue;
    }
}