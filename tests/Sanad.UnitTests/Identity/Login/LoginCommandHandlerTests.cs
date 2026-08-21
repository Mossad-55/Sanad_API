using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication.Login;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Domain.Users.Events;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.Login;

public sealed class LoginCommandHandlerTests
{
    [Fact]
    public async Task Handle_ShouldReturnNormalTokensAndCreateSession_ForActiveUser()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                UserStatus.Active);

        dbContext.ResetSaveChangesCalls();

        FakeAuthTokenService tokenService =
            new();

        LoginCommandHandler handler =
            CreateHandler(
                dbContext,
                PasswordVerificationResult.Success,
                tokenService);

        LoginCommand command =
            CreateCommand();

        Result<LoginResponse> result =
            await handler.Handle(
                command,
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            AuthAccessType.Normal,
            result.Value.AccessType);

        Assert.Equal(
            "access-token",
            result.Value.AccessToken);

        Assert.Equal(
            "refresh-token",
            result.Value.RefreshToken);

        Assert.NotNull(
            result.Value.DeviceSessionId);

        DeviceSession session =
            Assert.Single(
                dbContext.DeviceSessions);

        Assert.Equal(
            user.Id,
            session.UserId);

        Assert.Equal(
            command.DeviceName,
            session.DeviceName);

        Assert.Equal(
            command.DevicePlatform,
            session.Platform);

        Assert.Equal(
            command.AppVersion,
            session.AppVersion);

        Assert.Equal(
            "refresh-token-hash",
            session.RefreshTokenHash);

        Assert.Equal(
            result.Value.DeviceSessionId,
            session.Id);

        Assert.Equal(
            FixedDateTimeProvider.UtcNowValue,
            session.CreatedOnUtc);

        Assert.Equal(
            FixedDateTimeProvider.UtcNowValue
                .AddDays(30),
            session.ExpiresOnUtc);

        Assert.Equal(
            FixedDateTimeProvider.UtcNowValue,
            user.LastLoginOnUtc);

        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);

        Assert.Equal(
            1,
            tokenService.AccessTokenCalls);

        Assert.Equal(
            1,
            tokenService.RefreshTokenCalls);

        Assert.Equal(
            0,
            tokenService.RestrictedTokenCalls);
    }

    [Fact]
    public async Task Handle_ShouldReturnRestrictedTokenWithoutSession_ForPendingUser()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                UserStatus.PendingVerification);

        dbContext.ResetSaveChangesCalls();

        FakeAuthTokenService tokenService =
            new();

        LoginCommandHandler handler =
            CreateHandler(
                dbContext,
                PasswordVerificationResult.Success,
                tokenService);

        Result<LoginResponse> result =
            await handler.Handle(
                CreateCommand(),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            AuthAccessType.RestrictedVerification,
            result.Value.AccessType);

        Assert.Equal(
            "restricted-token",
            result.Value.AccessToken);

        Assert.Null(result.Value.RefreshToken);

        Assert.Null(
            result.Value.RefreshTokenExpiresOnUtc);

        Assert.Null(result.Value.DeviceSessionId);

        Assert.Empty(dbContext.DeviceSessions);

        Assert.Equal(
            FixedDateTimeProvider.UtcNowValue,
            user.LastLoginOnUtc);

        Assert.Equal(
            1,
            tokenService.RestrictedTokenCalls);

        Assert.Equal(
            0,
            tokenService.AccessTokenCalls);

        Assert.Equal(
            0,
            tokenService.RefreshTokenCalls);
    }

    [Fact]
    public async Task Handle_ShouldReturnInvalidCredentials_ForUnknownEmail()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        LoginCommandHandler handler =
            CreateHandler(
                dbContext,
                PasswordVerificationResult.Success,
                new FakeAuthTokenService());

        Result<LoginResponse> result =
            await handler.Handle(
                CreateCommand(),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            LoginErrors.InvalidCredentials,
            result.Error);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldReturnInvalidCredentials_ForWrongPassword()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        await SeedUserAsync(
            dbContext,
            UserStatus.Active);

        dbContext.ResetSaveChangesCalls();

        FakeAuthTokenService tokenService =
            new();

        LoginCommandHandler handler =
            CreateHandler(
                dbContext,
                PasswordVerificationResult.Failed,
                tokenService);

        Result<LoginResponse> result =
            await handler.Handle(
                CreateCommand(),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            LoginErrors.InvalidCredentials,
            result.Error);

        Assert.Empty(dbContext.DeviceSessions);

        Assert.Equal(
            0,
            tokenService.TotalCalls);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldRejectSuspendedUser()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        await SeedUserAsync(
            dbContext,
            UserStatus.Suspended);

        dbContext.ResetSaveChangesCalls();

        LoginCommandHandler handler =
            CreateHandler(
                dbContext,
                PasswordVerificationResult.Success,
                new FakeAuthTokenService());

        Result<LoginResponse> result =
            await handler.Handle(
                CreateCommand(),
                CancellationToken.None);

        Assert.Equal(
            LoginErrors.UserSuspended,
            result.Error);

        Assert.Empty(dbContext.DeviceSessions);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldRejectBlockedUser()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        await SeedUserAsync(
            dbContext,
            UserStatus.Blocked);

        dbContext.ResetSaveChangesCalls();

        LoginCommandHandler handler =
            CreateHandler(
                dbContext,
                PasswordVerificationResult.Success,
                new FakeAuthTokenService());

        Result<LoginResponse> result =
            await handler.Handle(
                CreateCommand(),
                CancellationToken.None);

        Assert.Equal(
            LoginErrors.UserBlocked,
            result.Error);

        Assert.Empty(dbContext.DeviceSessions);
    }

    [Fact]
    public async Task Handle_ShouldRejectMaximumActiveSessions()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                UserStatus.Active);

        for (int index = 1;
             index <=
             DeviceSessionPolicy.MaximumActiveSessions;
             index++)
        {
            dbContext.DeviceSessions.Add(
                CreateDeviceSession(
                    user.Id,
                    $"active-hash-{index}",
                    isExpired: false,
                    isRevoked: false));
        }

        await dbContext.SaveChangesAsync();

        dbContext.ResetSaveChangesCalls();

        FakeAuthTokenService tokenService =
            new();

        LoginCommandHandler handler =
            CreateHandler(
                dbContext,
                PasswordVerificationResult.Success,
                tokenService);

        Result<LoginResponse> result =
            await handler.Handle(
                CreateCommand(),
                CancellationToken.None);

        Assert.Equal(
            LoginErrors.SessionLimitReached,
            result.Error);

        Assert.Equal(
            DeviceSessionPolicy.MaximumActiveSessions,
            dbContext.DeviceSessions.Count());

        Assert.Equal(
            0,
            tokenService.TotalCalls);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldIgnoreExpiredAndRevokedSessions_WhenCountingLimit()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                UserStatus.Active);

        for (int index = 1;
             index <= 4;
             index++)
        {
            dbContext.DeviceSessions.Add(
                CreateDeviceSession(
                    user.Id,
                    $"active-hash-{index}",
                    isExpired: false,
                    isRevoked: false));
        }

        dbContext.DeviceSessions.Add(
            CreateDeviceSession(
                user.Id,
                "expired-hash",
                isExpired: true,
                isRevoked: false));

        dbContext.DeviceSessions.Add(
            CreateDeviceSession(
                user.Id,
                "revoked-hash",
                isExpired: false,
                isRevoked: true));

        await dbContext.SaveChangesAsync();

        dbContext.ResetSaveChangesCalls();

        LoginCommandHandler handler =
            CreateHandler(
                dbContext,
                PasswordVerificationResult.Success,
                new FakeAuthTokenService());

        Result<LoginResponse> result =
            await handler.Handle(
                CreateCommand(),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            AuthAccessType.Normal,
            result.Value.AccessType);

        Assert.Equal(
            7,
            dbContext.DeviceSessions.Count());
    }

    [Fact]
    public async Task Handle_ShouldRehashPasswordWithoutPasswordChangeEvent()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                UserStatus.Active);

        user.ClearDomainEvents();

        dbContext.ResetSaveChangesCalls();

        LoginCommandHandler handler =
            CreateHandler(
                dbContext,
                PasswordVerificationResult
                    .SuccessRehashNeeded,
                new FakeAuthTokenService());

        Result<LoginResponse> result =
            await handler.Handle(
                CreateCommand(),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            "rehash::StrongPass123",
            user.Password!.PasswordHash);

        Assert.Empty(
            user.DomainEvents
                .OfType<
                    UserPasswordChangedDomainEvent>());
    }

    private static LoginCommandHandler CreateHandler(
        IdentityTestDbContext dbContext,
        PasswordVerificationResult verificationResult,
        IAuthTokenService tokenService)
    {
        return new LoginCommandHandler(
            dbContext,
            new ConfigurablePasswordHasher(
                verificationResult),
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

    private static async Task<User> SeedUserAsync(
        IdentityTestDbContext dbContext,
        UserStatus status)
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
            "stored-password-hash",
            FixedDateTimeProvider.UtcNowValue);

        if (status !=
            UserStatus.PendingVerification)
        {
            user.VerifyEmail(
                FixedDateTimeProvider.UtcNowValue);

            user.VerifyPhone(
                FixedDateTimeProvider.UtcNowValue);

            user.Activate(
                FixedDateTimeProvider.UtcNowValue
                    .AddMinutes(1));
        }

        if (status ==
            UserStatus.Suspended)
        {
            user.Suspend(
                "Suspended for test.",
                FixedDateTimeProvider.UtcNowValue
                    .AddMinutes(2));
        }

        if (status ==
            UserStatus.Blocked)
        {
            user.Block(
                "Blocked for test.",
                FixedDateTimeProvider.UtcNowValue
                    .AddMinutes(2));
        }

        dbContext.Users.Add(user);

        await dbContext.SaveChangesAsync();

        return user;
    }

    private static DeviceSession CreateDeviceSession(
        UserId userId,
        string refreshTokenHash,
        bool isExpired,
        bool isRevoked)
    {
        DateTime createdOnUtc =
            FixedDateTimeProvider.UtcNowValue
                .AddDays(-1);

        DateTime expiresOnUtc =
            isExpired
                ? FixedDateTimeProvider.UtcNowValue
                    .AddMinutes(-1)
                : FixedDateTimeProvider.UtcNowValue
                    .AddDays(29);

        DeviceSession session =
            DeviceSession.Create(
                userId,
                "Existing Device",
                DevicePlatform.iOS,
                "1.0.0",
                refreshTokenHash,
                createdOnUtc,
                expiresOnUtc);

        if (isRevoked)
        {
            session.Revoke(
                "Revoked for test.",
                FixedDateTimeProvider.UtcNowValue
                    .AddMinutes(-1));
        }

        return session;
    }

    private static LoginCommand CreateCommand()
    {
        return new LoginCommand(
            Email: "mohamed@example.com",
            Password: "StrongPass123",
            DeviceName: "iPhone 16",
            DevicePlatform: DevicePlatform.iOS,
            AppVersion: "1.0.0");
    }

    private sealed class ConfigurablePasswordHasher :
        IPasswordHasher
    {
        private readonly PasswordVerificationResult
            _verificationResult;

        internal ConfigurablePasswordHasher(
            PasswordVerificationResult verificationResult)
        {
            _verificationResult =
                verificationResult;
        }

        public string Hash(
            string password)
        {
            return $"rehash::{password}";
        }

        public PasswordVerificationResult Verify(
            string passwordHash,
            string providedPassword)
        {
            return _verificationResult;
        }
    }

    private sealed class FakeAuthTokenService :
        IAuthTokenService
    {
        internal int AccessTokenCalls { get; private set; }

        internal int RestrictedTokenCalls
        {
            get;
            private set;
        }

        internal int RefreshTokenCalls { get; private set; }

        internal int TotalCalls =>
            AccessTokenCalls +
            RestrictedTokenCalls +
            RefreshTokenCalls;

        public GeneratedAccessToken GenerateAccessToken(
            User user,
            DateTime utcNow)
        {
            AccessTokenCalls++;

            return new GeneratedAccessToken(
                "access-token",
                utcNow.AddMinutes(15));
        }

        public GeneratedAccessToken
            GenerateRestrictedVerificationToken(
                User user,
                DateTime utcNow)
        {
            RestrictedTokenCalls++;

            return new GeneratedAccessToken(
                "restricted-token",
                utcNow.AddMinutes(15));
        }

        public GeneratedRefreshToken GenerateRefreshToken(
            DateTime utcNow)
        {
            RefreshTokenCalls++;

            return new GeneratedRefreshToken(
                "refresh-token",
                "refresh-token-hash",
                utcNow.AddDays(30));
        }

        public bool VerifyRefreshToken(
            string providedToken,
            string storedHash)
        {
            return providedToken ==
                        "refresh-token" &&
                   storedHash ==
                        "refresh-token-hash";
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