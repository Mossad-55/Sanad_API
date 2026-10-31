using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Identity.Application.Abstractions.Security;
using Ahlena.Modules.Identity.Application.Authentication;
using Ahlena.Modules.Identity.Application.Authentication.Login;
using Ahlena.Modules.Identity.Application.Authentication.Tokens;
using Ahlena.Modules.Identity.Domain.Authentication.DeviceSessions;
using Ahlena.Modules.Identity.Domain.Authentication.VerificationRequests;
using Ahlena.Modules.Identity.Domain.Users;
using Ahlena.Modules.Identity.Domain.Users.Events;
using Ahlena.UnitTests.Identity.Registration;

namespace Ahlena.UnitTests.Identity.Login;

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
    public async Task Handle_ShouldFindUserByPhoneNumber()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                UserStatus.Active,
                AccountType.Family);

        LoginCommandHandler handler =
            CreateHandler(
                dbContext,
                PasswordVerificationResult.Success,
                new FakeAuthTokenService());

        Result<LoginResponse> result =
            await handler.Handle(
                CreateCommand() with
                {
                    Identifier = "+201001234567"
                },
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AuthAccessType.Normal, result.Value.AccessType);
        Assert.Contains(
            dbContext.DeviceSessions,
            session => session.UserId == user.Id);
    }

    [Fact]
    public async Task Handle_ShouldFindUserByEmailStartingWithPlus()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                UserStatus.Active,
                email: "+user@example.com");

        LoginCommandHandler handler =
            CreateHandler(
                dbContext,
                PasswordVerificationResult.Success,
                new FakeAuthTokenService());

        Result<LoginResponse> result =
            await handler.Handle(
                CreateCommand() with
                {
                    Identifier = "+user@example.com"
                },
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(
            dbContext.DeviceSessions,
            session => session.UserId == user.Id);
    }

    [Theory]
    [InlineData(AccountType.Family)]
    [InlineData(AccountType.MedicalCaregiver)]
    [InlineData(AccountType.CompanionCaregiver)]
    [InlineData(AccountType.Elderly)]
    [InlineData(AccountType.SuperAdmin)]
    [InlineData(AccountType.ContentAdmin)]
    [InlineData(AccountType.SupportAdmin)]
    public async Task Handle_ShouldAllowPhoneLoginForPasswordBearingAccountTypes(
        AccountType accountType)
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        await SeedUserAsync(
            dbContext,
            UserStatus.Active,
            accountType);

        LoginCommandHandler handler =
            CreateHandler(
                dbContext,
                PasswordVerificationResult.Success,
                new FakeAuthTokenService());

        Result<LoginResponse> result =
            await handler.Handle(
                CreateCommand() with
                {
                    Identifier = "+201001234567"
                },
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AuthAccessType.Normal, result.Value.AccessType);
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

    [Fact]
    public async Task Handle_ShouldReturnRecoveryIds_ForPendingUserWithPendingRequests()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                UserStatus.PendingVerification);

        VerificationRequest emailRequest =
            await SeedVerificationRequestAsync(
                dbContext,
                user.Id,
                VerificationChannel.Email,
                VerificationPurpose.VerifyEmail,
                "mohamed@example.com",
                FixedDateTimeProvider.UtcNowValue
                    .AddMinutes(-4));

        VerificationRequest phoneRequest =
            await SeedVerificationRequestAsync(
                dbContext,
                user.Id,
                VerificationChannel.Sms,
                VerificationPurpose.VerifyPhone,
                "+201001234567",
                FixedDateTimeProvider.UtcNowValue
                    .AddMinutes(-3));

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
            AuthAccessType.RestrictedVerification,
            result.Value.AccessType);

        Assert.Equal(
            emailRequest.Id,
            result.Value.EmailVerificationRequestId);

        Assert.Equal(
            emailRequest.ExpiresOnUtc,
            result.Value.EmailVerificationRequestExpiresOnUtc);

        Assert.Equal(
            phoneRequest.Id,
            result.Value.PhoneVerificationRequestId);

        Assert.Equal(
            phoneRequest.ExpiresOnUtc,
            result.Value.PhoneVerificationRequestExpiresOnUtc);

        Assert.Null(result.Value.RefreshToken);
        Assert.Null(result.Value.DeviceSessionId);
    }

    [Fact]
    public async Task Handle_ShouldReturnLatestRequestId_WhenMultiplePendingRequestsExist()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                UserStatus.PendingVerification);

        await SeedVerificationRequestAsync(
            dbContext,
            user.Id,
            VerificationChannel.Email,
            VerificationPurpose.VerifyEmail,
            "mohamed@example.com",
            FixedDateTimeProvider.UtcNowValue
                .AddMinutes(-5));

        VerificationRequest latestEmailRequest =
            await SeedVerificationRequestAsync(
                dbContext,
                user.Id,
                VerificationChannel.Email,
                VerificationPurpose.VerifyEmail,
                "mohamed@example.com",
                FixedDateTimeProvider.UtcNowValue
                    .AddMinutes(-2));

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
            latestEmailRequest.Id,
            result.Value.EmailVerificationRequestId);

        Assert.Equal(
            latestEmailRequest.ExpiresOnUtc,
            result.Value.EmailVerificationRequestExpiresOnUtc);

        Assert.Null(
            result.Value.PhoneVerificationRequestId);

        Assert.Null(
            result.Value.PhoneVerificationRequestExpiresOnUtc);
    }

    [Fact]
    public async Task Handle_ShouldReturnExpiredRequestId_WithTrueExpiry()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                UserStatus.PendingVerification);

        VerificationRequest expiredRequest =
            await SeedVerificationRequestAsync(
                dbContext,
                user.Id,
                VerificationChannel.Email,
                VerificationPurpose.VerifyEmail,
                "mohamed@example.com",
                FixedDateTimeProvider.UtcNowValue
                    .AddMinutes(-10));

        expiredRequest.MarkExpired(
            FixedDateTimeProvider.UtcNowValue);

        await dbContext.SaveChangesAsync();

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
            expiredRequest.Id,
            result.Value.EmailVerificationRequestId);

        Assert.Equal(
            expiredRequest.ExpiresOnUtc,
            result.Value.EmailVerificationRequestExpiresOnUtc);

        Assert.True(
            result.Value.EmailVerificationRequestExpiresOnUtc <
            FixedDateTimeProvider.UtcNowValue);
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_ForVerifiedChannel()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                UserStatus.PendingVerification);

        user.VerifyEmail(
            FixedDateTimeProvider.UtcNowValue);

        await SeedVerificationRequestAsync(
            dbContext,
            user.Id,
            VerificationChannel.Email,
            VerificationPurpose.VerifyEmail,
            "mohamed@example.com",
            FixedDateTimeProvider.UtcNowValue
                .AddMinutes(-4));

        VerificationRequest phoneRequest =
            await SeedVerificationRequestAsync(
                dbContext,
                user.Id,
                VerificationChannel.Sms,
                VerificationPurpose.VerifyPhone,
                "+201001234567",
                FixedDateTimeProvider.UtcNowValue
                    .AddMinutes(-3));

        await dbContext.SaveChangesAsync();

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

        Assert.True(result.Value.EmailVerified);
        Assert.False(result.Value.PhoneVerified);

        Assert.Null(
            result.Value.EmailVerificationRequestId);

        Assert.Null(
            result.Value.EmailVerificationRequestExpiresOnUtc);

        Assert.Equal(
            phoneRequest.Id,
            result.Value.PhoneVerificationRequestId);
    }

    [Fact]
    public async Task Handle_ShouldNotLeakForeignOrWrongPurposeRequests()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                UserStatus.PendingVerification);

        await SeedVerificationRequestAsync(
            dbContext,
            UserId.New(),
            VerificationChannel.Email,
            VerificationPurpose.VerifyEmail,
            " stranger@example.com ".Trim(),
            FixedDateTimeProvider.UtcNowValue
                .AddMinutes(-2));

        await SeedVerificationRequestAsync(
            dbContext,
            user.Id,
            VerificationChannel.Email,
            VerificationPurpose.ResetPassword,
            "mohamed@example.com",
            FixedDateTimeProvider.UtcNowValue
                .AddMinutes(-2));

        await SeedVerificationRequestAsync(
            dbContext,
            user.Id,
            VerificationChannel.Sms,
            VerificationPurpose.ElderlyLogin,
            "+201001234567",
            FixedDateTimeProvider.UtcNowValue
                .AddMinutes(-1));

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

        Assert.Null(
            result.Value.EmailVerificationRequestId);

        Assert.Null(
            result.Value.EmailVerificationRequestExpiresOnUtc);

        Assert.Null(
            result.Value.PhoneVerificationRequestId);

        Assert.Null(
            result.Value.PhoneVerificationRequestExpiresOnUtc);
    }

    [Fact]
    public async Task Handle_ShouldReturnNullRecoveryFields_ForActiveUser()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                UserStatus.Active);

        await SeedVerificationRequestAsync(
            dbContext,
            user.Id,
            VerificationChannel.Email,
            VerificationPurpose.VerifyEmail,
            "mohamed@example.com",
            FixedDateTimeProvider.UtcNowValue
                .AddMinutes(-4));

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

        Assert.Null(
            result.Value.EmailVerificationRequestId);

        Assert.Null(
            result.Value.EmailVerificationRequestExpiresOnUtc);

        Assert.Null(
            result.Value.PhoneVerificationRequestId);

        Assert.Null(
            result.Value.PhoneVerificationRequestExpiresOnUtc);
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
        UserStatus status,
        AccountType accountType = AccountType.Family,
        string email = "mohamed@example.com")
    {
        User user =
            User.Create(
                FullName.Create(
                    "محمد أحمد"),
                FullName.Create(
                    "Mohamed Ahmed"),
                Email.Create(
                    email),
                PhoneNumber.Create(
                    "+201001234567"));

        user.AddAccount(accountType);

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

    private static async Task<VerificationRequest> SeedVerificationRequestAsync(
        IdentityTestDbContext dbContext,
        UserId userId,
        VerificationChannel channel,
        VerificationPurpose purpose,
        string target,
        DateTime createdOnUtc)
    {
        VerificationRequest request =
            VerificationRequest.Create(
                userId,
                target,
                "old-otp-hash",
                channel,
                purpose,
                createdOnUtc,
                createdOnUtc.Add(
                    OtpPolicy.Lifetime));

        dbContext.VerificationRequests.Add(
            request);

        await dbContext.SaveChangesAsync();

        return request;
    }

    private static LoginCommand CreateCommand()
    {
        return new LoginCommand(
            Identifier: "mohamed@example.com",
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
            DateTime utcNow,
            DeviceSessionId? deviceSessionId = null)
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
