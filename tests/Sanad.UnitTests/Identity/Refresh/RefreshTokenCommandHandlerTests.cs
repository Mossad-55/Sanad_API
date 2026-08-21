using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication.Refresh;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.Refresh;

public sealed class RefreshTokenCommandHandlerTests
{
    [Fact]
    public async Task Handle_ShouldRotateRefreshToken()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserAsync(
                dbContext);

        DeviceSession session =
            await SeedSessionAsync(
                dbContext,
                user.Id,
                "stored-refresh-hash");

        dbContext.ResetSaveChangesCalls();

        FakeAuthTokenService tokenService =
            new(
                refreshTokenIsValid: true);

        RefreshTokenCommandHandler handler =
            CreateHandler(
                dbContext,
                tokenService);

        Result<RefreshTokenResponse> result =
            await handler.Handle(
                new RefreshTokenCommand(
                    session.Id,
                    "provided-refresh-token"),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            session.Id,
            result.Value.DeviceSessionId);

        Assert.Equal(
            "new-access-token",
            result.Value.AccessToken);

        Assert.Equal(
            "new-refresh-token",
            result.Value.RefreshToken);

        Assert.Equal(
            "new-refresh-hash",
            session.RefreshTokenHash);

        Assert.Equal(
            1,
            session.RotationCount);

        Assert.Equal(
            FixedDateTimeProvider.UtcNowValue,
            session.LastRotatedOnUtc);

        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);

        Assert.Equal(
            1,
            tokenService.VerifyCalls);

        Assert.Equal(
            1,
            tokenService.AccessTokenCalls);

        Assert.Equal(
            1,
            tokenService.RefreshTokenCalls);
    }

    [Fact]
    public async Task Handle_ShouldDetectReuseAndRevokeAllUserSessions()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserAsync(
                dbContext);

        DeviceSession currentSession =
            await SeedSessionAsync(
                dbContext,
                user.Id,
                "current-hash");

        DeviceSession otherSession =
            await SeedSessionAsync(
                dbContext,
                user.Id,
                "other-hash");

        dbContext.ResetSaveChangesCalls();

        RefreshTokenCommandHandler handler =
            CreateHandler(
                dbContext,
                new FakeAuthTokenService(
                    refreshTokenIsValid: false));

        Result<RefreshTokenResponse> result =
            await handler.Handle(
                new RefreshTokenCommand(
                    currentSession.Id,
                    "stolen-or-old-token"),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            RefreshTokenErrors.ReuseDetected,
            result.Error);

        Assert.True(currentSession.IsRevoked);
        Assert.True(
            currentSession.HasReuseDetection);

        Assert.True(otherSession.IsRevoked);

        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldRejectRevokedSession()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserAsync(
                dbContext);

        DeviceSession session =
            await SeedSessionAsync(
                dbContext,
                user.Id,
                "stored-hash");

        session.Revoke(
            "Logged out.",
            FixedDateTimeProvider.UtcNowValue
                .AddMinutes(-1));

        await dbContext.SaveChangesAsync();

        dbContext.ResetSaveChangesCalls();

        Result<RefreshTokenResponse> result =
            await CreateHandler(
                    dbContext,
                    new FakeAuthTokenService(true))
                .Handle(
                    new RefreshTokenCommand(
                        session.Id,
                        "provided-token"),
                    CancellationToken.None);

        Assert.Equal(
            RefreshTokenErrors.SessionRevoked,
            result.Error);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldRejectExpiredSession()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserAsync(
                dbContext);

        DeviceSession session =
            await SeedSessionAsync(
                dbContext,
                user.Id,
                "stored-hash",
                expiresOnUtc:
                    FixedDateTimeProvider.UtcNowValue
                        .AddMinutes(-1));

        dbContext.ResetSaveChangesCalls();

        Result<RefreshTokenResponse> result =
            await CreateHandler(
                    dbContext,
                    new FakeAuthTokenService(true))
                .Handle(
                    new RefreshTokenCommand(
                        session.Id,
                        "provided-token"),
                    CancellationToken.None);

        Assert.Equal(
            RefreshTokenErrors.SessionExpired,
            result.Error);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldRevokeSession_WhenUserIsNotActive()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedPendingUserAsync(
                dbContext);

        DeviceSession session =
            await SeedSessionAsync(
                dbContext,
                user.Id,
                "stored-hash");

        dbContext.ResetSaveChangesCalls();

        Result<RefreshTokenResponse> result =
            await CreateHandler(
                    dbContext,
                    new FakeAuthTokenService(true))
                .Handle(
                    new RefreshTokenCommand(
                        session.Id,
                        "provided-token"),
                    CancellationToken.None);

        Assert.Equal(
            RefreshTokenErrors.UserNotActive,
            result.Error);

        Assert.True(session.IsRevoked);

        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldReturnSessionNotFound()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        Result<RefreshTokenResponse> result =
            await CreateHandler(
                    dbContext,
                    new FakeAuthTokenService(true))
                .Handle(
                    new RefreshTokenCommand(
                        DeviceSessionId.New(),
                        "provided-token"),
                    CancellationToken.None);

        Assert.Equal(
            RefreshTokenErrors.SessionNotFound,
            result.Error);
    }

    [Fact]
    public async Task Handle_ShouldReturnUserNotFound()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        DeviceSession session =
            await SeedSessionAsync(
                dbContext,
                UserId.New(),
                "stored-hash");

        dbContext.ResetSaveChangesCalls();

        Result<RefreshTokenResponse> result =
            await CreateHandler(
                    dbContext,
                    new FakeAuthTokenService(true))
                .Handle(
                    new RefreshTokenCommand(
                        session.Id,
                        "provided-token"),
                    CancellationToken.None);

        Assert.Equal(
            RefreshTokenErrors.UserNotFound,
            result.Error);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    private static RefreshTokenCommandHandler CreateHandler(
        IdentityTestDbContext dbContext,
        IAuthTokenService tokenService)
    {
        return new RefreshTokenCommandHandler(
            dbContext,
            tokenService,
            new FixedDateTimeProvider());
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
            await SeedPendingUserAsync(
                dbContext);

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

    private static async Task<User> SeedPendingUserAsync(
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

        return user;
    }

    private static async Task<DeviceSession>
        SeedSessionAsync(
            IdentityTestDbContext dbContext,
            UserId userId,
            string refreshTokenHash,
            DateTime? expiresOnUtc = null)
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
                expiresOnUtc ??
                    FixedDateTimeProvider.UtcNowValue
                        .AddDays(29));

        dbContext.DeviceSessions.Add(
            session);

        await dbContext.SaveChangesAsync();

        return session;
    }

    private sealed class FakeAuthTokenService :
        IAuthTokenService
    {
        private readonly bool
            _refreshTokenIsValid;

        internal FakeAuthTokenService(
            bool refreshTokenIsValid)
        {
            _refreshTokenIsValid =
                refreshTokenIsValid;
        }

        internal int VerifyCalls { get; private set; }
        internal int AccessTokenCalls { get; private set; }
        internal int RefreshTokenCalls { get; private set; }

        public GeneratedAccessToken GenerateAccessToken(
            User user,
            DateTime utcNow)
        {
            AccessTokenCalls++;

            return new GeneratedAccessToken(
                "new-access-token",
                utcNow.AddMinutes(15));
        }

        public GeneratedAccessToken
            GenerateRestrictedVerificationToken(
                User user,
                DateTime utcNow)
        {
            throw new NotSupportedException();
        }

        public GeneratedRefreshToken GenerateRefreshToken(
            DateTime utcNow)
        {
            RefreshTokenCalls++;

            return new GeneratedRefreshToken(
                "new-refresh-token",
                "new-refresh-hash",
                utcNow.AddDays(30));
        }

        public bool VerifyRefreshToken(
            string providedToken,
            string storedHash)
        {
            VerifyCalls++;

            return _refreshTokenIsValid;
        }
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