using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication;
using Sanad.Modules.Identity.Application.Authentication.ElderlyLogin;
using Sanad.Modules.Identity.Application.Authentication.Login;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.ElderlyLogin;

public sealed class ElderlyLoginCommandHandlerTests
{
    [Fact]
    public async Task Request_ShouldPersistHashedOtpAndSendSms_ForEligiblePendingElderlyUser()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                UserStatus.PendingVerification,
                AccountType.Elderly);

        dbContext.ResetSaveChangesCalls();

        RecordingSmsSender smsSender =
            new();

        RequestElderlyLoginOtpCommandHandler handler =
            CreateRequestHandler(
                dbContext,
                smsSender);

        Result result =
            await handler.Handle(
                new RequestElderlyLoginOtpCommand(
                    user.PhoneNumber.Value),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        VerificationRequest request =
            Assert.Single(dbContext.VerificationRequests);

        Assert.Equal(user.Id, request.UserId);
        Assert.Equal(user.PhoneNumber.Value, request.Target);
        Assert.Equal("otp-hash", request.OtpHash);
        Assert.NotEqual("123456", request.OtpHash);
        Assert.Equal(VerificationChannel.Sms, request.Channel);
        Assert.Equal(VerificationPurpose.ElderlyLogin, request.Purpose);
        Assert.Equal(VerificationStatus.Pending, request.Status);
        Assert.Equal(FixedDateTimeProvider.UtcNowValue, request.CreatedOnUtc);
        Assert.Equal(
            FixedDateTimeProvider.UtcNowValue.Add(OtpPolicy.Lifetime),
            request.ExpiresOnUtc);
        Assert.Equal(1, dbContext.SaveChangesCalls);

        SentSms sentSms =
            Assert.Single(smsSender.SentMessages);

        Assert.Equal(user.PhoneNumber.Value, sentSms.PhoneNumber);
        Assert.Equal("123456", sentSms.Code);
        Assert.Equal(VerificationPurpose.ElderlyLogin, sentSms.Purpose);
    }

    [Theory]
    [InlineData(UserStatus.PendingVerification, AccountType.Family)]
    [InlineData(UserStatus.Suspended, AccountType.Elderly)]
    [InlineData(UserStatus.Blocked, AccountType.Elderly)]
    public async Task Request_ShouldReturnGenericSuccessWithoutPersistenceOrSms_ForIneligibleUser(
        UserStatus status,
        AccountType accountType)
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                status,
                accountType);

        dbContext.ResetSaveChangesCalls();

        RecordingSmsSender smsSender =
            new();

        RequestElderlyLoginOtpCommandHandler handler =
            CreateRequestHandler(
                dbContext,
                smsSender);

        Result result =
            await handler.Handle(
                new RequestElderlyLoginOtpCommand(
                    user.PhoneNumber.Value),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(dbContext.VerificationRequests);
        Assert.Empty(smsSender.SentMessages);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Request_ShouldReturnGenericSuccessWithoutPersistenceOrSms_ForUnknownPhone()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        dbContext.ResetSaveChangesCalls();

        RecordingSmsSender smsSender =
            new();

        RequestElderlyLoginOtpCommandHandler handler =
            CreateRequestHandler(
                dbContext,
                smsSender);

        Result result =
            await handler.Handle(
                new RequestElderlyLoginOtpCommand(
                    "+201009999999"),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(dbContext.VerificationRequests);
        Assert.Empty(smsSender.SentMessages);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Request_ShouldNotReplaceOrSendSms_BeforeCooldownBoundary()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                UserStatus.PendingVerification,
                AccountType.Elderly);

        VerificationRequest existingRequest =
            await SeedPendingRequestAsync(
                dbContext,
                user,
                FixedDateTimeProvider.UtcNowValue.AddSeconds(-59));

        dbContext.ResetSaveChangesCalls();

        RecordingSmsSender smsSender =
            new();

        Result result =
            await CreateRequestHandler(
                    dbContext,
                    smsSender)
                .Handle(
                    new RequestElderlyLoginOtpCommand(
                        user.PhoneNumber.Value),
                    CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(dbContext.VerificationRequests);
        Assert.Equal(VerificationStatus.Pending, existingRequest.Status);
        Assert.Empty(smsSender.SentMessages);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Request_ShouldInvalidateAndReplacePendingRequest_AtExactCooldownBoundary()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                UserStatus.PendingVerification,
                AccountType.Elderly);

        VerificationRequest existingRequest =
            await SeedPendingRequestAsync(
                dbContext,
                user,
                FixedDateTimeProvider.UtcNowValue.Subtract(
                    OtpPolicy.ResendCooldown));

        dbContext.ResetSaveChangesCalls();

        RecordingSmsSender smsSender =
            new();

        Result result =
            await CreateRequestHandler(
                    dbContext,
                    smsSender)
                .Handle(
                    new RequestElderlyLoginOtpCommand(
                        user.PhoneNumber.Value),
                    CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(VerificationStatus.Invalidated, existingRequest.Status);
        Assert.Equal(2, dbContext.VerificationRequests.Count());
        Assert.Single(smsSender.SentMessages);
        Assert.Equal(1, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Verify_ShouldActivatePendingElderlyUserAndCreateNormalSession()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                UserStatus.PendingVerification,
                AccountType.Elderly);

        VerificationRequest verificationRequest =
            await SeedPendingRequestAsync(
                dbContext,
                user,
                FixedDateTimeProvider.UtcNowValue);

        dbContext.ResetSaveChangesCalls();

        FakeAuthTokenService tokenService =
            new();

        Result<LoginResponse> result =
            await CreateVerifyHandler(
                    dbContext,
                    otpIsValid: true,
                    tokenService)
                .Handle(
                    CreateVerifyCommand(user.PhoneNumber.Value),
                    CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AuthAccessType.Normal, result.Value.AccessType);
        Assert.Equal("access-token", result.Value.AccessToken);
        Assert.Equal("refresh-token", result.Value.RefreshToken);
        Assert.Equal(user.Id, result.Value.UserId);
        Assert.True(result.Value.PhoneVerified);
        Assert.False(result.Value.EmailVerified);

        Assert.True(user.PhoneVerified);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(VerificationStatus.Verified, verificationRequest.Status);
        Assert.Equal(FixedDateTimeProvider.UtcNowValue, user.LastLoginOnUtc);

        DeviceSession session =
            Assert.Single(dbContext.DeviceSessions);

        Assert.Equal(user.Id, session.UserId);
        Assert.Equal("Ahmed's iPhone", session.DeviceName);
        Assert.Equal(DevicePlatform.iOS, session.Platform);
        Assert.Equal("1.0.0", session.AppVersion);
        Assert.Equal("refresh-token-hash", session.RefreshTokenHash);
        Assert.Equal(1, dbContext.SaveChangesCalls);
        Assert.Equal(1, tokenService.AccessTokenCalls);
        Assert.Equal(1, tokenService.RefreshTokenCalls);
    }

    [Fact]
    public async Task Verify_ShouldPersistFailedAttempt_WhenOtpIsInvalid()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                UserStatus.PendingVerification,
                AccountType.Elderly);

        VerificationRequest verificationRequest =
            await SeedPendingRequestAsync(
                dbContext,
                user,
                FixedDateTimeProvider.UtcNowValue);

        dbContext.ResetSaveChangesCalls();

        Result<LoginResponse> result =
            await CreateVerifyHandler(
                    dbContext,
                    otpIsValid: false,
                    new FakeAuthTokenService())
                .Handle(
                    CreateVerifyCommand(user.PhoneNumber.Value),
                    CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ElderlyLoginErrors.OtpVerificationFailed, result.Error);
        Assert.Equal(1, verificationRequest.Attempts);
        Assert.Equal(VerificationStatus.Pending, verificationRequest.Status);
        Assert.False(user.PhoneVerified);
        Assert.Equal(UserStatus.PendingVerification, user.Status);
        Assert.Empty(dbContext.DeviceSessions);
        Assert.Equal(1, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Verify_ShouldExpireRequest_WhenOtpIsExpired()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                UserStatus.PendingVerification,
                AccountType.Elderly);

        VerificationRequest verificationRequest =
            await SeedPendingRequestAsync(
                dbContext,
                user,
                FixedDateTimeProvider.UtcNowValue.Subtract(
                    OtpPolicy.Lifetime));

        dbContext.ResetSaveChangesCalls();

        Result<LoginResponse> result =
            await CreateVerifyHandler(
                    dbContext,
                    otpIsValid: true,
                    new FakeAuthTokenService())
                .Handle(
                    CreateVerifyCommand(user.PhoneNumber.Value),
                    CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ElderlyLoginErrors.OtpVerificationFailed, result.Error);
        Assert.Equal(VerificationStatus.Expired, verificationRequest.Status);
        Assert.False(user.PhoneVerified);
        Assert.Equal(UserStatus.PendingVerification, user.Status);
        Assert.Empty(dbContext.DeviceSessions);
        Assert.Equal(1, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Verify_ShouldKeepValidOtpPending_WhenSessionLimitReached()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                UserStatus.PendingVerification,
                AccountType.Elderly);

        VerificationRequest verificationRequest =
            await SeedPendingRequestAsync(
                dbContext,
                user,
                FixedDateTimeProvider.UtcNowValue);

        for (int index = 0; index < DeviceSessionPolicy.MaximumActiveSessions; index++)
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

        Result<LoginResponse> result =
            await CreateVerifyHandler(
                    dbContext,
                    otpIsValid: true,
                    new FakeAuthTokenService())
                .Handle(
                    CreateVerifyCommand(user.PhoneNumber.Value),
                    CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ElderlyLoginErrors.SessionLimitReached, result.Error);
        Assert.Equal(VerificationStatus.Pending, verificationRequest.Status);
        Assert.False(user.PhoneVerified);
        Assert.Equal(UserStatus.PendingVerification, user.Status);
        Assert.Equal(DeviceSessionPolicy.MaximumActiveSessions, dbContext.DeviceSessions.Count());
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Verify_ShouldReturnGenericFailureWithoutMutation_ForNonElderlyUser()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                UserStatus.PendingVerification,
                AccountType.Family);

        dbContext.ResetSaveChangesCalls();

        Result<LoginResponse> result =
            await CreateVerifyHandler(
                    dbContext,
                    otpIsValid: true,
                    new FakeAuthTokenService())
                .Handle(
                    CreateVerifyCommand(user.PhoneNumber.Value),
                    CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ElderlyLoginErrors.OtpVerificationFailed, result.Error);
        Assert.Empty(dbContext.VerificationRequests);
        Assert.Empty(dbContext.DeviceSessions);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    private static RequestElderlyLoginOtpCommandHandler CreateRequestHandler(
        IdentityTestDbContext dbContext,
        ISmsSender smsSender)
    {
        return new RequestElderlyLoginOtpCommandHandler(
            dbContext,
            new FakeOtpService(),
            smsSender,
            new FixedDateTimeProvider());
    }

    private static VerifyElderlyLoginOtpCommandHandler CreateVerifyHandler(
        IdentityTestDbContext dbContext,
        bool otpIsValid,
        IAuthTokenService tokenService)
    {
        return new VerifyElderlyLoginOtpCommandHandler(
            dbContext,
            new FakeOtpService(otpIsValid),
            tokenService,
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

    private static async Task<User> SeedUserAsync(
        IdentityTestDbContext dbContext,
        UserStatus status,
        AccountType accountType)
    {
        User user =
            User.Create(
                FullName.Create("محمد أحمد"),
                FullName.Create("Mohamed Ahmed"),
                email: null,
                PhoneNumber.Create("+201001234567"));

        user.AddAccount(accountType);

        if (status is UserStatus.Active or UserStatus.Suspended)
        {
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

    private static async Task<VerificationRequest> SeedPendingRequestAsync(
        IdentityTestDbContext dbContext,
        User user,
        DateTime createdOnUtc)
    {
        VerificationRequest request =
            VerificationRequest.Create(
                user.Id,
                user.PhoneNumber.Value,
                "otp-hash",
                VerificationChannel.Sms,
                VerificationPurpose.ElderlyLogin,
                createdOnUtc,
                createdOnUtc.Add(OtpPolicy.Lifetime));

        dbContext.VerificationRequests.Add(request);
        await dbContext.SaveChangesAsync();

        return request;
    }

    private static VerifyElderlyLoginOtpCommand CreateVerifyCommand(
        string phoneNumber)
    {
        return new VerifyElderlyLoginOtpCommand(
            phoneNumber,
            "123456",
            "Ahmed's iPhone",
            DevicePlatform.iOS,
            "1.0.0");
    }

    private sealed class FakeOtpService : IOtpService
    {
        private readonly bool _verifyResult;

        internal FakeOtpService(bool verifyResult = true)
        {
            _verifyResult = verifyResult;
        }

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
            Assert.Equal("123456", providedCode);
            Assert.Equal("otp-hash", otpHash);

            return _verifyResult;
        }
    }

    private sealed class RecordingSmsSender : ISmsSender
    {
        internal List<SentSms> SentMessages { get; } = [];

        public Task SendVerificationCodeAsync(
            string phoneNumber,
            string code,
            VerificationPurpose purpose,
            CancellationToken cancellationToken)
        {
            SentMessages.Add(
                new SentSms(
                    phoneNumber,
                    code,
                    purpose));

            return Task.CompletedTask;
        }
    }

    private sealed record SentSms(
        string PhoneNumber,
        string Code,
        VerificationPurpose Purpose);

    private sealed class FakeAuthTokenService : IAuthTokenService
    {
        internal int AccessTokenCalls { get; private set; }
        internal int RefreshTokenCalls { get; private set; }

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
            throw new NotSupportedException();
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
