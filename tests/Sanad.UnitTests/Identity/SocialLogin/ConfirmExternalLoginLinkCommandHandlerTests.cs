using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication;
using Sanad.Modules.Identity.Application.Authentication.SocialLogin;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.SocialLogin;

public sealed class ConfirmExternalLoginLinkCommandHandlerTests
{
    [Fact]
    public async Task Handle_ShouldLinkActiveUserAndReturnNormalSession()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();
        User user = await SeedUserAsync(dbContext, phoneVerified: true, active: true);
        VerificationRequest otpRequest = await SeedRequestAsync(dbContext, user);
        dbContext.ResetSaveChangesCalls();

        Result<StartSocialLoginResponse> result = await CreateHandler(
            dbContext, CreateChallenge(user.Id, otpRequest.Id), otpIsValid: true)
            .Handle(CreateCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AuthAccessType.Normal, result.Value.AccessType);
        Assert.Equal("access-token", result.Value.AccessToken);
        Assert.Equal("refresh-token", result.Value.RefreshToken);
        Assert.NotNull(result.Value.DeviceSessionId);
        Assert.True(user.EmailVerified);
        Assert.Equal(VerificationStatus.Verified, otpRequest.Status);
        Assert.Single(user.ExternalLogins);
        Assert.Single(dbContext.DeviceSessions);
        Assert.Equal(1, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldActivatePendingPhoneVerifiedUserAndReturnNormalSession()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();
        User user = await SeedUserAsync(dbContext, phoneVerified: true, active: false);
        VerificationRequest otpRequest = await SeedRequestAsync(dbContext, user);
        dbContext.ResetSaveChangesCalls();

        Result<StartSocialLoginResponse> result = await CreateHandler(
            dbContext, CreateChallenge(user.Id, otpRequest.Id), otpIsValid: true)
            .Handle(CreateCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AuthAccessType.Normal, result.Value.AccessType);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.True(user.EmailVerified);
        Assert.True(user.PhoneVerified);
        Assert.Equal(VerificationStatus.Verified, otpRequest.Status);
        Assert.Single(user.ExternalLogins);
        Assert.Single(dbContext.DeviceSessions);
    }

    [Fact]
    public async Task Handle_ShouldLinkPendingPhoneUnverifiedUserAndReturnRestrictedToken()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();
        User user = await SeedUserAsync(dbContext, phoneVerified: false, active: false);
        VerificationRequest otpRequest = await SeedRequestAsync(dbContext, user);
        dbContext.ResetSaveChangesCalls();

        Result<StartSocialLoginResponse> result = await CreateHandler(
            dbContext, CreateChallenge(user.Id, otpRequest.Id), otpIsValid: true)
            .Handle(CreateCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AuthAccessType.RestrictedVerification, result.Value.AccessType);
        Assert.Equal("restricted-token", result.Value.AccessToken);
        Assert.Null(result.Value.RefreshToken);
        Assert.Null(result.Value.DeviceSessionId);
        Assert.Equal(UserStatus.PendingVerification, user.Status);
        Assert.True(user.EmailVerified);
        Assert.False(user.PhoneVerified);
        Assert.Equal(VerificationStatus.Verified, otpRequest.Status);
        Assert.Single(user.ExternalLogins);
        Assert.Empty(dbContext.DeviceSessions);
    }

    [Fact]
    public async Task Handle_ShouldPersistFailedOtpAttemptWithoutLinkOrSession()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();
        User user = await SeedUserAsync(dbContext, phoneVerified: true, active: false);
        VerificationRequest otpRequest = await SeedRequestAsync(dbContext, user);
        dbContext.ResetSaveChangesCalls();

        Result<StartSocialLoginResponse> result = await CreateHandler(
            dbContext, CreateChallenge(user.Id, otpRequest.Id), otpIsValid: false)
            .Handle(CreateCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialLoginErrors.ExternalLinkConfirmationFailed, result.Error);
        Assert.Equal(1, otpRequest.Attempts);
        Assert.Equal(VerificationStatus.Pending, otpRequest.Status);
        Assert.Empty(user.ExternalLogins);
        Assert.Empty(dbContext.DeviceSessions);
        Assert.Equal(UserStatus.PendingVerification, user.Status);
        Assert.Equal(1, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldExpireOtpWithoutLinkOrSession()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();
        User user = await SeedUserAsync(dbContext, phoneVerified: true, active: false);
        VerificationRequest otpRequest = await SeedRequestAsync(
            dbContext, user, FixedDateTimeProvider.UtcNowValue.AddMinutes(-5));
        dbContext.ResetSaveChangesCalls();

        Result<StartSocialLoginResponse> result = await CreateHandler(
            dbContext, CreateChallenge(user.Id, otpRequest.Id), otpIsValid: true)
            .Handle(CreateCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialLoginErrors.ExternalLinkConfirmationFailed, result.Error);
        Assert.Equal(VerificationStatus.Expired, otpRequest.Status);
        Assert.Empty(user.ExternalLogins);
        Assert.Empty(dbContext.DeviceSessions);
        Assert.Equal(1, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldKeepOtpPendingWithoutLinkWhenSessionLimitReached()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();
        User user = await SeedUserAsync(dbContext, phoneVerified: true, active: false);
        VerificationRequest otpRequest = await SeedRequestAsync(dbContext, user);

        for (int index = 0; index < DeviceSessionPolicy.MaximumActiveSessions; index++)
        {
            dbContext.DeviceSessions.Add(DeviceSession.Create(
                user.Id, $"Device {index}", DevicePlatform.Android, "1.0.0",
                $"hash-{index}", FixedDateTimeProvider.UtcNowValue,
                FixedDateTimeProvider.UtcNowValue.AddDays(30)));
        }

        await dbContext.SaveChangesAsync();
        dbContext.ResetSaveChangesCalls();

        Result<StartSocialLoginResponse> result = await CreateHandler(
            dbContext, CreateChallenge(user.Id, otpRequest.Id), otpIsValid: true)
            .Handle(CreateCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialLoginErrors.SessionLimitReached, result.Error);
        Assert.Equal(VerificationStatus.Pending, otpRequest.Status);
        Assert.Empty(user.ExternalLogins);
        Assert.Equal(UserStatus.PendingVerification, user.Status);
        Assert.Equal(DeviceSessionPolicy.MaximumActiveSessions, dbContext.DeviceSessions.Count());
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task Handle_ShouldReturnGenericFailure_ForInvalidChallengeShape(
        bool missingUserId,
        bool missingRequestId,
        bool missingEmail)
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();
        dbContext.ResetSaveChangesCalls();

        SocialAuthenticationChallenge challenge = new(
            ExternalLoginProvider.Google,
            "google-subject",
            missingEmail ? null : "mohamed@example.com",
            missingUserId ? null : UserId.New(),
            missingRequestId ? null : VerificationRequestId.New(),
            FixedDateTimeProvider.UtcNowValue.AddMinutes(10));

        Result<StartSocialLoginResponse> result = await CreateHandler(
            dbContext, challenge, otpIsValid: true)
            .Handle(CreateCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialLoginErrors.ExternalLinkConfirmationFailed, result.Error);
        Assert.Empty(dbContext.Users);
        Assert.Empty(dbContext.VerificationRequests);
        Assert.Empty(dbContext.DeviceSessions);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    private static ConfirmExternalLoginLinkCommandHandler CreateHandler(
        IdentityTestDbContext dbContext,
        SocialAuthenticationChallenge? challenge,
        bool otpIsValid)
    {
        return new ConfirmExternalLoginLinkCommandHandler(
            dbContext,
            new FakeChallengeStore(challenge),
            new FakeOtpService(otpIsValid),
            new FakeAuthTokenService(),
            new FixedDateTimeProvider());
    }

    private static IdentityTestDbContext CreateDbContext()
    {
        return new IdentityTestDbContext(
            new DbContextOptionsBuilder<IdentityTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
    }

    private static async Task<User> SeedUserAsync(
        IdentityTestDbContext dbContext,
        bool phoneVerified,
        bool active)
    {
        User user = User.Create(
            FullName.Create("محمد أحمد"),
            FullName.Create("Mohamed Ahmed"),
            Email.Create("mohamed@example.com"),
            PhoneNumber.Create("+201001234567"));

        user.AddAccount(AccountType.Family);

        if (phoneVerified)
        {
            user.VerifyPhone(FixedDateTimeProvider.UtcNowValue);
        }

        if (active)
        {
            user.SetInitialPasswordHash("password-hash", FixedDateTimeProvider.UtcNowValue);
            user.VerifyEmail(FixedDateTimeProvider.UtcNowValue);
            user.Activate(FixedDateTimeProvider.UtcNowValue);
        }

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user;
    }

    private static async Task<VerificationRequest> SeedRequestAsync(
        IdentityTestDbContext dbContext,
        User user,
        DateTime? createdOnUtc = null)
    {
        DateTime created = createdOnUtc ?? FixedDateTimeProvider.UtcNowValue;
        VerificationRequest request = VerificationRequest.Create(
            user.Id, "mohamed@example.com", "otp-hash",
            VerificationChannel.Email,
            VerificationPurpose.ConfirmExternalLoginLink,
            created, created.AddMinutes(5));
        dbContext.VerificationRequests.Add(request);
        await dbContext.SaveChangesAsync();
        return request;
    }

    private static SocialAuthenticationChallenge CreateChallenge(
        UserId userId,
        VerificationRequestId requestId)
    {
        return new SocialAuthenticationChallenge(
            ExternalLoginProvider.Google,
            "google-subject",
            "mohamed@example.com",
            userId,
            requestId,
            FixedDateTimeProvider.UtcNowValue.AddMinutes(10));
    }

    private static ConfirmExternalLoginLinkCommand CreateCommand()
    {
        return new ConfirmExternalLoginLinkCommand(
            "opaque-challenge", "123456", "Ahmed's iPhone",
            DevicePlatform.iOS, "1.0.0");
    }

    private sealed class FakeChallengeStore :
        ISocialAuthenticationChallengeStore
    {
        private readonly SocialAuthenticationChallenge?
            _challenge;

        internal FakeChallengeStore(
            SocialAuthenticationChallenge? challenge)
        {
            _challenge =
                challenge;
        }

        public Task<string> CreateAsync(
            SocialAuthenticationChallenge challenge,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<SocialAuthenticationChallenge?>
            GetActiveAsync(
                string opaqueChallenge,
                DateTime utcNow,
                CancellationToken cancellationToken)
        {
            return Task.FromResult(
                _challenge);
        }

        public Task<bool> StageConsumeAsync(
            string opaqueChallenge,
            DateTime utcNow,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                true);
        }
    }

    private sealed class FakeOtpService : IOtpService
    {
        private readonly bool _valid;
        internal FakeOtpService(bool valid) => _valid = valid;
        public GeneratedOtpCode Generate(int length) => throw new NotSupportedException();
        public bool Verify(string providedCode, string otpHash)
        {
            Assert.Equal("123456", providedCode);
            Assert.Equal("otp-hash", otpHash);
            return _valid;
        }
    }

    private sealed class FakeAuthTokenService : IAuthTokenService
    {
        public GeneratedAccessToken GenerateAccessToken(User user, DateTime utcNow) => new("access-token", utcNow.AddMinutes(15));
        public GeneratedAccessToken GenerateRestrictedVerificationToken(User user, DateTime utcNow) => new("restricted-token", utcNow.AddMinutes(15));
        public GeneratedRefreshToken GenerateRefreshToken(DateTime utcNow) => new("refresh-token", "refresh-token-hash", utcNow.AddDays(30));
        public bool VerifyRefreshToken(string providedToken, string storedHash) => throw new NotSupportedException();
    }

    private sealed class FixedDateTimeProvider : IDateTimeProvider
    {
        internal static readonly DateTime UtcNowValue = new(2026, 8, 22, 10, 0, 0, DateTimeKind.Utc);
        public DateTime UtcNow => UtcNowValue;
    }
}
