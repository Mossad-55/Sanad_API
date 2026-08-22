using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
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

public sealed class StartSocialLoginCommandHandlerTests
{
    [Fact]
    public async Task Handle_ShouldReturnAuthenticationFailed_WhenProviderCredentialIsInvalid()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        dbContext.ResetSaveChangesCalls();

        RecordingChallengeStore challengeStore =
            new();

        RecordingEmailSender emailSender =
            new();

        Result<StartSocialLoginResponse> result =
            await CreateHandler(
                    dbContext,
                    externalIdentity: null,
                    challengeStore,
                    emailSender)
                .Handle(
                    CreateCommand(),
                    CancellationToken.None);

        Assert.False(result.IsSuccess);

        Assert.Equal(
            SocialLoginErrors.AuthenticationFailed,
            result.Error);

        Assert.Empty(dbContext.Users);
        Assert.Empty(dbContext.VerificationRequests);
        Assert.Empty(dbContext.DeviceSessions);
        Assert.Empty(challengeStore.CreatedChallenges);
        Assert.Empty(emailSender.SentMessages);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldReturnNormalTokensAndCreateSession_ForActiveLinkedUser()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedLinkedUserAsync(
                dbContext,
                UserStatus.Active,
                AccountType.Family);

        dbContext.ResetSaveChangesCalls();

        FakeAuthTokenService tokenService =
            new();

        Result<StartSocialLoginResponse> result =
            await CreateHandler(
                    dbContext,
                    CreateVerifiedIdentity(),
                    new RecordingChallengeStore(),
                    new RecordingEmailSender(),
                    tokenService)
                .Handle(
                    CreateCommand(),
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

        Assert.NotNull(result.Value.DeviceSessionId);
        Assert.Null(result.Value.OpaqueChallenge);

        DeviceSession session =
            Assert.Single(dbContext.DeviceSessions);

        Assert.Equal(user.Id, session.UserId);
        Assert.Equal("Ahmed's iPhone", session.DeviceName);
        Assert.Equal(DevicePlatform.iOS, session.Platform);
        Assert.Equal("1.0.0", session.AppVersion);
        Assert.Equal("refresh-token-hash", session.RefreshTokenHash);
        Assert.Equal(FixedDateTimeProvider.UtcNowValue, user.LastLoginOnUtc);
        Assert.Equal(1, dbContext.SaveChangesCalls);
        Assert.Equal(1, tokenService.AccessTokenCalls);
        Assert.Equal(1, tokenService.RefreshTokenCalls);
        Assert.Equal(0, tokenService.RestrictedTokenCalls);
    }

    [Fact]
    public async Task Handle_ShouldReturnRestrictedTokenWithoutSession_ForPendingLinkedUser()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedLinkedUserAsync(
                dbContext,
                UserStatus.PendingVerification,
                AccountType.MedicalCaregiver);

        dbContext.ResetSaveChangesCalls();

        FakeAuthTokenService tokenService =
            new();

        Result<StartSocialLoginResponse> result =
            await CreateHandler(
                    dbContext,
                    CreateVerifiedIdentity(),
                    new RecordingChallengeStore(),
                    new RecordingEmailSender(),
                    tokenService)
                .Handle(
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
        Assert.Null(result.Value.DeviceSessionId);
        Assert.Null(result.Value.OpaqueChallenge);
        Assert.Empty(dbContext.DeviceSessions);
        Assert.Equal(FixedDateTimeProvider.UtcNowValue, user.LastLoginOnUtc);
        Assert.Equal(1, dbContext.SaveChangesCalls);
        Assert.Equal(0, tokenService.AccessTokenCalls);
        Assert.Equal(0, tokenService.RefreshTokenCalls);
        Assert.Equal(1, tokenService.RestrictedTokenCalls);
    }

    [Theory]
    [InlineData(UserStatus.Suspended)]
    [InlineData(UserStatus.Blocked)]
    public async Task Handle_ShouldReturnAuthenticationFailedWithoutMutation_ForIneligibleLinkedStatus(
        UserStatus status)
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedLinkedUserAsync(
                dbContext,
                status,
                AccountType.Family);

        DateTime? originalLastLoginOnUtc =
            user.LastLoginOnUtc;

        dbContext.ResetSaveChangesCalls();

        RecordingChallengeStore challengeStore =
            new();

        RecordingEmailSender emailSender =
            new();

        Result<StartSocialLoginResponse> result =
            await CreateHandler(
                    dbContext,
                    CreateVerifiedIdentity(),
                    challengeStore,
                    emailSender)
                .Handle(
                    CreateCommand(),
                    CancellationToken.None);

        Assert.False(result.IsSuccess);

        Assert.Equal(
            SocialLoginErrors.AuthenticationFailed,
            result.Error);

        Assert.Equal(status, user.Status);
        Assert.Equal(originalLastLoginOnUtc, user.LastLoginOnUtc);
        Assert.Empty(dbContext.DeviceSessions);
        Assert.Empty(dbContext.VerificationRequests);
        Assert.Empty(challengeStore.CreatedChallenges);
        Assert.Empty(emailSender.SentMessages);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldReturnAuthenticationFailedWithoutMutation_ForLinkedElderlyUser()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedLinkedUserAsync(
                dbContext,
                UserStatus.PendingVerification,
                AccountType.Elderly);

        dbContext.ResetSaveChangesCalls();

        Result<StartSocialLoginResponse> result =
            await CreateHandler(
                    dbContext,
                    CreateVerifiedIdentity(),
                    new RecordingChallengeStore(),
                    new RecordingEmailSender())
                .Handle(
                    CreateCommand(),
                    CancellationToken.None);

        Assert.False(result.IsSuccess);

        Assert.Equal(
            SocialLoginErrors.AuthenticationFailed,
            result.Error);

        Assert.Equal(UserStatus.PendingVerification, user.Status);
        Assert.Null(user.LastLoginOnUtc);
        Assert.Empty(dbContext.DeviceSessions);
        Assert.Empty(dbContext.VerificationRequests);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldReturnSessionLimitWithoutMutation_ForActiveLinkedUser()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedLinkedUserAsync(
                dbContext,
                UserStatus.Active,
                AccountType.Family);

        for (int index = 0;
             index < DeviceSessionPolicy.MaximumActiveSessions;
             index++)
        {
            dbContext.DeviceSessions.Add(
                DeviceSession.Create(
                    user.Id,
                    $"Existing device {index}",
                    DevicePlatform.Android,
                    "1.0.0",
                    $"existing-hash-{index}",
                    FixedDateTimeProvider.UtcNowValue,
                    FixedDateTimeProvider.UtcNowValue.AddDays(30)));
        }

        await dbContext.SaveChangesAsync();
        dbContext.ResetSaveChangesCalls();

        FakeAuthTokenService tokenService =
            new();

        Result<StartSocialLoginResponse> result =
            await CreateHandler(
                    dbContext,
                    CreateVerifiedIdentity(),
                    new RecordingChallengeStore(),
                    new RecordingEmailSender(),
                    tokenService)
                .Handle(
                    CreateCommand(),
                    CancellationToken.None);

        Assert.False(result.IsSuccess);

        Assert.Equal(
            SocialLoginErrors.SessionLimitReached,
            result.Error);

        Assert.Equal(
            DeviceSessionPolicy.MaximumActiveSessions,
            dbContext.DeviceSessions.Count());

        Assert.Null(user.LastLoginOnUtc);
        Assert.Equal(0, dbContext.SaveChangesCalls);
        Assert.Equal(0, tokenService.AccessTokenCalls);
        Assert.Equal(0, tokenService.RefreshTokenCalls);
    }

    [Fact]
    public async Task Handle_ShouldCreateBoundEmailOtpAndChallengeWithoutAutomaticLink_ForMatchingEmailUser()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User matchingUser =
            await SeedMatchingEmailUserAsync(
                dbContext);

        dbContext.ResetSaveChangesCalls();

        RecordingChallengeStore challengeStore =
            new();

        RecordingEmailSender emailSender =
            new();

        Result<StartSocialLoginResponse> result =
            await CreateHandler(
                    dbContext,
                    CreateVerifiedIdentity(),
                    challengeStore,
                    emailSender)
                .Handle(
                    CreateCommand(),
                    CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Null(result.Value.AccessType);
        Assert.Null(result.Value.AccessToken);
        Assert.Null(result.Value.RefreshToken);
        Assert.Null(result.Value.DeviceSessionId);
        Assert.Equal("opaque-challenge", result.Value.OpaqueChallenge);

        SocialAuthenticationChallenge challenge =
            Assert.Single(challengeStore.CreatedChallenges);

        Assert.Equal(ExternalLoginProvider.Google, challenge.Provider);
        Assert.Equal("google-subject", challenge.ProviderSubject);
        Assert.Equal("mohamed@example.com", challenge.VerifiedEmail);
        Assert.Equal(matchingUser.Id, challenge.ExistingUserId);
        Assert.Equal(
            FixedDateTimeProvider.UtcNowValue.Add(
                SocialLoginPolicy.ChallengeLifetime),
            challenge.ExpiresOnUtc);

        VerificationRequest otpRequest =
            Assert.Single(dbContext.VerificationRequests);

        Assert.Equal(otpRequest.Id, challenge.LinkVerificationRequestId);
        Assert.Equal(matchingUser.Id, otpRequest.UserId);
        Assert.Equal(VerificationChannel.Email, otpRequest.Channel);
        Assert.Equal(
            VerificationPurpose.ConfirmExternalLoginLink,
            otpRequest.Purpose);
        Assert.Equal("otp-hash", otpRequest.OtpHash);
        Assert.NotEqual("123456", otpRequest.OtpHash);
        Assert.Empty(matchingUser.ExternalLogins);
        Assert.Empty(dbContext.DeviceSessions);
        Assert.Equal(1, dbContext.SaveChangesCalls);

        SentEmail sentEmail =
            Assert.Single(emailSender.SentMessages);

        Assert.Equal("mohamed@example.com", sentEmail.Email);
        Assert.Equal("123456", sentEmail.Code);
        Assert.Equal(
            VerificationPurpose.ConfirmExternalLoginLink,
            sentEmail.Purpose);
    }

    [Fact]
    public async Task Handle_ShouldCreateNewChallengeWithoutUserOtpTokenOrSession_ForBrandNewIdentity()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        dbContext.ResetSaveChangesCalls();

        RecordingChallengeStore challengeStore =
            new();

        RecordingEmailSender emailSender =
            new();

        Result<StartSocialLoginResponse> result =
            await CreateHandler(
                    dbContext,
                    CreateVerifiedIdentity(),
                    challengeStore,
                    emailSender)
                .Handle(
                    CreateCommand(),
                    CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Null(result.Value.AccessType);
        Assert.Null(result.Value.AccessToken);
        Assert.Null(result.Value.RefreshToken);
        Assert.Null(result.Value.DeviceSessionId);
        Assert.Equal("opaque-challenge", result.Value.OpaqueChallenge);

        SocialAuthenticationChallenge challenge =
            Assert.Single(challengeStore.CreatedChallenges);

        Assert.Equal(ExternalLoginProvider.Google, challenge.Provider);
        Assert.Equal("google-subject", challenge.ProviderSubject);
        Assert.Equal("mohamed@example.com", challenge.VerifiedEmail);
        Assert.Null(challenge.ExistingUserId);
        Assert.Null(challenge.LinkVerificationRequestId);
        Assert.Equal(
            FixedDateTimeProvider.UtcNowValue.Add(
                SocialLoginPolicy.ChallengeLifetime),
            challenge.ExpiresOnUtc);

        Assert.Empty(dbContext.Users);
        Assert.Empty(dbContext.VerificationRequests);
        Assert.Empty(dbContext.DeviceSessions);
        Assert.Empty(emailSender.SentMessages);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldCreateNewChallengeWithNullEmail_WhenProviderHasNoVerifiedEmail()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        dbContext.ResetSaveChangesCalls();

        RecordingChallengeStore challengeStore =
            new();

        Result<StartSocialLoginResponse> result =
            await CreateHandler(
                    dbContext,
                    CreateVerifiedIdentity(verifiedEmail: null),
                    challengeStore,
                    new RecordingEmailSender())
                .Handle(
                    CreateCommand(),
                    CancellationToken.None);

        Assert.True(result.IsSuccess);

        SocialAuthenticationChallenge challenge =
            Assert.Single(challengeStore.CreatedChallenges);

        Assert.Null(challenge.VerifiedEmail);
        Assert.Null(challenge.ExistingUserId);
        Assert.Null(challenge.LinkVerificationRequestId);
        Assert.Empty(dbContext.Users);
        Assert.Empty(dbContext.VerificationRequests);
        Assert.Empty(dbContext.DeviceSessions);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    private static StartSocialLoginCommandHandler CreateHandler(
        IdentityTestDbContext dbContext,
        VerifiedExternalIdentity? externalIdentity,
        ISocialAuthenticationChallengeStore challengeStore,
        IEmailSender emailSender,
        IAuthTokenService? tokenService = null)
    {
        return new StartSocialLoginCommandHandler(
            dbContext,
            new FakeExternalIdentityVerifier(externalIdentity),
            challengeStore,
            new FakeOtpService(),
            emailSender,
            tokenService ?? new FakeAuthTokenService(),
            new FixedDateTimeProvider());
    }

    private static IdentityTestDbContext CreateDbContext()
    {
        DbContextOptions<IdentityTestDbContext> options =
            new DbContextOptionsBuilder<IdentityTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new IdentityTestDbContext(options);
    }

    private static async Task<User> SeedLinkedUserAsync(
        IdentityTestDbContext dbContext,
        UserStatus status,
        AccountType accountType)
    {
        User user =
            User.Create(
                FullName.Create("محمد أحمد"),
                FullName.Create("Mohamed Ahmed"),
                Email.Create("mohamed@example.com"),
                PhoneNumber.Create("+201001234567"));

        user.AddAccount(accountType);

        user.LinkExternalLogin(
            ExternalLoginProvider.Google,
            "google-subject",
            FixedDateTimeProvider.UtcNowValue);

        if (status is UserStatus.Active or UserStatus.Suspended)
        {
            if (accountType != AccountType.Elderly)
            {
                user.VerifyEmail(FixedDateTimeProvider.UtcNowValue);
            }

            user.VerifyPhone(FixedDateTimeProvider.UtcNowValue);
            user.Activate(FixedDateTimeProvider.UtcNowValue);
        }

        if (status == UserStatus.Suspended)
        {
            user.Suspend(
                "Security review.",
                FixedDateTimeProvider.UtcNowValue);
        }

        if (status == UserStatus.Blocked)
        {
            user.Block(
                "Security review.",
                FixedDateTimeProvider.UtcNowValue);
        }

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        return user;
    }

    private static async Task<User> SeedMatchingEmailUserAsync(
        IdentityTestDbContext dbContext)
    {
        User user =
            User.Create(
                FullName.Create("محمد أحمد"),
                FullName.Create("Mohamed Ahmed"),
                Email.Create("mohamed@example.com"),
                PhoneNumber.Create("+201001234567"));

        user.AddAccount(AccountType.Family);

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        return user;
    }

    private static StartSocialLoginCommand CreateCommand()
    {
        return new StartSocialLoginCommand(
            ExternalLoginProvider.Google,
            "provider-credential",
            "Ahmed's iPhone",
            DevicePlatform.iOS,
            "1.0.0");
    }

    private static VerifiedExternalIdentity CreateVerifiedIdentity(
        string? verifiedEmail = "mohamed@example.com")
    {
        return new VerifiedExternalIdentity(
            ExternalLoginProvider.Google,
            "google-subject",
            verifiedEmail);
    }

    private sealed class FakeExternalIdentityVerifier :
        IExternalIdentityVerifier
    {
        private readonly VerifiedExternalIdentity? _externalIdentity;

        internal FakeExternalIdentityVerifier(
            VerifiedExternalIdentity? externalIdentity)
        {
            _externalIdentity = externalIdentity;
        }

        public Task<VerifiedExternalIdentity?> VerifyAsync(
            ExternalLoginProvider provider,
            string providerCredential,
            CancellationToken cancellationToken)
        {
            Assert.Equal(ExternalLoginProvider.Google, provider);
            Assert.Equal("provider-credential", providerCredential);

            return Task.FromResult(_externalIdentity);
        }
    }

    private sealed class RecordingChallengeStore :
        ISocialAuthenticationChallengeStore
    {
        internal List<SocialAuthenticationChallenge> CreatedChallenges { get; } = [];

        public Task<string> CreateAsync(
            SocialAuthenticationChallenge challenge,
            CancellationToken cancellationToken)
        {
            CreatedChallenges.Add(challenge);

            return Task.FromResult("opaque-challenge");
        }

        public Task<SocialAuthenticationChallenge?> ConsumeAsync(
            string opaqueChallenge,
            DateTime utcNow,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeOtpService : IOtpService
    {
        public GeneratedOtpCode Generate(int length)
        {
            Assert.Equal(OtpPolicy.CodeLength, length);

            return new GeneratedOtpCode(
                "123456",
                "otp-hash");
        }

        public bool Verify(
            string providedCode,
            string otpHash)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        internal List<SentEmail> SentMessages { get; } = [];

        public Task SendVerificationCodeAsync(
            string email,
            string code,
            VerificationPurpose purpose,
            CancellationToken cancellationToken)
        {
            SentMessages.Add(
                new SentEmail(
                    email,
                    code,
                    purpose));

            return Task.CompletedTask;
        }
    }

    private sealed record SentEmail(
        string Email,
        string Code,
        VerificationPurpose Purpose);

    private sealed class FakeAuthTokenService : IAuthTokenService
    {
        internal int AccessTokenCalls { get; private set; }
        internal int RefreshTokenCalls { get; private set; }
        internal int RestrictedTokenCalls { get; private set; }

        public GeneratedAccessToken GenerateAccessToken(
            User user,
            DateTime utcNow)
        {
            AccessTokenCalls++;

            return new GeneratedAccessToken(
                "access-token",
                utcNow.AddMinutes(15));
        }

        public GeneratedAccessToken GenerateRestrictedVerificationToken(
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
            throw new NotSupportedException();
        }
    }

    private sealed class FixedDateTimeProvider : IDateTimeProvider
    {
        internal static readonly DateTime UtcNowValue =
            new(
                2026,
                8,
                22,
                10,
                0,
                0,
                DateTimeKind.Utc);

        public DateTime UtcNow => UtcNowValue;
    }
}
