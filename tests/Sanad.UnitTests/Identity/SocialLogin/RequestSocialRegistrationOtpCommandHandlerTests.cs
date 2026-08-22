using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication;
using Sanad.Modules.Identity.Application.Authentication.SocialLogin;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.SocialLogin;

public sealed class RequestSocialRegistrationOtpCommandHandlerTests
{
    [Fact]
    public async Task Handle_ShouldCreateBoundSmsOtpAndRegistrationChallenge_ForValidNewUserChallenge()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();
        RecordingRegistrationChallengeStore registrationStore = new();
        RecordingSmsSender smsSender = new();
        dbContext.ResetSaveChangesCalls();

        Result<RequestSocialRegistrationOtpResponse> result = await CreateHandler(
            dbContext, CreateNewUserChallenge(), registrationStore, smsSender)
            .Handle(CreateCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("registration-challenge", result.Value.OpaqueRegistrationChallenge);
        Assert.Equal(FixedDateTimeProvider.UtcNowValue.Add(SocialLoginPolicy.ChallengeLifetime), result.Value.ExpiresOnUtc);

        VerificationRequest otpRequest = Assert.Single(dbContext.VerificationRequests);
        Assert.Null(otpRequest.UserId);
        Assert.Equal("+201001234567", otpRequest.Target);
        Assert.Equal("otp-hash", otpRequest.OtpHash);
        Assert.NotEqual("123456", otpRequest.OtpHash);
        Assert.Equal(VerificationChannel.Sms, otpRequest.Channel);
        Assert.Equal(VerificationPurpose.VerifyPhone, otpRequest.Purpose);

        SocialRegistrationChallenge registrationChallenge = Assert.Single(registrationStore.CreatedChallenges);
        Assert.Equal(otpRequest.Id, registrationChallenge.PhoneVerificationRequestId);
        Assert.Equal("mohamed@example.com", registrationChallenge.VerifiedEmail);
        Assert.Equal("محمد أحمد", registrationChallenge.ArabicFullName);
        Assert.Equal("Mohamed Ahmed", registrationChallenge.EnglishFullName);
        Assert.Equal(AccountType.Family, registrationChallenge.AccountType);
        Assert.Equal("+201001234567", registrationChallenge.PhoneNumber);

        SentSms sms = Assert.Single(smsSender.SentMessages);
        Assert.Equal("+201001234567", sms.PhoneNumber);
        Assert.Equal("123456", sms.Code);
        Assert.Equal(VerificationPurpose.VerifyPhone, sms.Purpose);
        Assert.Empty(dbContext.Users);
        Assert.Equal(1, dbContext.SaveChangesCalls);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task Handle_ShouldReturnGenericFailure_ForInvalidInitialChallenge(
        bool existingUserChallenge,
        bool missingEmail,
        bool missingChallenge)
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();
        RecordingRegistrationChallengeStore registrationStore = new();
        RecordingSmsSender smsSender = new();
        dbContext.ResetSaveChangesCalls();

        SocialAuthenticationChallenge? challenge = missingChallenge
            ? null
            : new SocialAuthenticationChallenge(
                ExternalLoginProvider.Google,
                "google-subject",
                missingEmail ? null : "mohamed@example.com",
                existingUserChallenge ? UserId.New() : null,
                existingUserChallenge ? VerificationRequestId.New() : null,
                FixedDateTimeProvider.UtcNowValue.AddMinutes(10));

        Result<RequestSocialRegistrationOtpResponse> result = await CreateHandler(
            dbContext, challenge, registrationStore, smsSender)
            .Handle(CreateCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialLoginErrors.SocialRegistrationFailed, result.Error);
        Assert.Empty(dbContext.Users);
        Assert.Empty(dbContext.VerificationRequests);
        Assert.Empty(registrationStore.CreatedChallenges);
        Assert.Empty(smsSender.SentMessages);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldReturnGenericFailureWithoutSideEffects_ForDuplicateEmail()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();
        await SeedUserAsync(dbContext, "mohamed@example.com", "+201009999999");
        dbContext.ResetSaveChangesCalls();
        RecordingRegistrationChallengeStore registrationStore = new();
        RecordingSmsSender smsSender = new();

        Result<RequestSocialRegistrationOtpResponse> result = await CreateHandler(
            dbContext, CreateNewUserChallenge(), registrationStore, smsSender)
            .Handle(CreateCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialLoginErrors.SocialRegistrationFailed, result.Error);
        Assert.Empty(dbContext.VerificationRequests);
        Assert.Empty(registrationStore.CreatedChallenges);
        Assert.Empty(smsSender.SentMessages);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldReturnGenericFailureWithoutSideEffects_ForDuplicatePhone()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();
        await SeedUserAsync(dbContext, "other@example.com", "+201001234567");
        dbContext.ResetSaveChangesCalls();
        RecordingRegistrationChallengeStore registrationStore = new();
        RecordingSmsSender smsSender = new();

        Result<RequestSocialRegistrationOtpResponse> result = await CreateHandler(
            dbContext, CreateNewUserChallenge(), registrationStore, smsSender)
            .Handle(CreateCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialLoginErrors.SocialRegistrationFailed, result.Error);
        Assert.Empty(dbContext.VerificationRequests);
        Assert.Empty(registrationStore.CreatedChallenges);
        Assert.Empty(smsSender.SentMessages);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    [Theory]
    [InlineData(AccountType.Elderly)]
    [InlineData((AccountType)999)]
    public async Task Handle_ShouldReturnGenericFailure_ForUnsupportedAccountType(AccountType accountType)
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();
        RecordingRegistrationChallengeStore registrationStore = new();
        RecordingSmsSender smsSender = new();
        dbContext.ResetSaveChangesCalls();

        Result<RequestSocialRegistrationOtpResponse> result = await CreateHandler(
            dbContext, CreateNewUserChallenge(), registrationStore, smsSender)
            .Handle(CreateCommand() with { AccountType = accountType }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialLoginErrors.SocialRegistrationFailed, result.Error);
        Assert.Empty(dbContext.VerificationRequests);
        Assert.Empty(registrationStore.CreatedChallenges);
        Assert.Empty(smsSender.SentMessages);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    private static RequestSocialRegistrationOtpCommandHandler CreateHandler(
        IdentityTestDbContext dbContext,
        SocialAuthenticationChallenge? socialChallenge,
        ISocialRegistrationChallengeStore registrationStore,
        ISmsSender smsSender)
    {
        return new RequestSocialRegistrationOtpCommandHandler(
            dbContext,
            new FakeSocialAuthenticationChallengeStore(socialChallenge),
            registrationStore,
            new FakeOtpService(),
            smsSender,
            new FixedDateTimeProvider());
    }

    private static IdentityTestDbContext CreateDbContext()
    {
        return new IdentityTestDbContext(
            new DbContextOptionsBuilder<IdentityTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
    }

    private static async Task SeedUserAsync(IdentityTestDbContext dbContext, string email, string phoneNumber)
    {
        User user = User.Create(
            FullName.Create("محمد أحمد"),
            FullName.Create("Mohamed Ahmed"),
            Email.Create(email),
            PhoneNumber.Create(phoneNumber));
        user.AddAccount(AccountType.Family);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
    }

    private static RequestSocialRegistrationOtpCommand CreateCommand()
    {
        return new RequestSocialRegistrationOtpCommand(
            "initial-challenge", "محمد أحمد", "Mohamed Ahmed",
            AccountType.Family, "+201001234567");
    }

    private static SocialAuthenticationChallenge CreateNewUserChallenge()
    {
        return new SocialAuthenticationChallenge(
            ExternalLoginProvider.Google, "google-subject", "mohamed@example.com",
            ExistingUserId: null, LinkVerificationRequestId: null,
            FixedDateTimeProvider.UtcNowValue.AddMinutes(10));
    }

    private sealed class FakeSocialAuthenticationChallengeStore : ISocialAuthenticationChallengeStore
    {
        private readonly SocialAuthenticationChallenge? _challenge;
        internal FakeSocialAuthenticationChallengeStore(SocialAuthenticationChallenge? challenge) => _challenge = challenge;
        public Task<string> CreateAsync(SocialAuthenticationChallenge challenge, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SocialAuthenticationChallenge?> ConsumeAsync(string opaqueChallenge, DateTime utcNow, CancellationToken cancellationToken) => Task.FromResult(_challenge);
    }

    private sealed class RecordingRegistrationChallengeStore : ISocialRegistrationChallengeStore
    {
        internal List<SocialRegistrationChallenge> CreatedChallenges { get; } = [];
        public Task<string> CreateAsync(SocialRegistrationChallenge challenge, CancellationToken cancellationToken)
        {
            CreatedChallenges.Add(challenge);
            return Task.FromResult("registration-challenge");
        }
        public Task<SocialRegistrationChallenge?> ConsumeAsync(string opaqueChallenge, DateTime utcNow, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeOtpService : IOtpService
    {
        public GeneratedOtpCode Generate(int length)
        {
            Assert.Equal(OtpPolicy.CodeLength, length);
            return new GeneratedOtpCode("123456", "otp-hash");
        }
        public bool Verify(string providedCode, string otpHash) => throw new NotSupportedException();
    }

    private sealed class RecordingSmsSender : ISmsSender
    {
        internal List<SentSms> SentMessages { get; } = [];
        public Task SendVerificationCodeAsync(string phoneNumber, string code, VerificationPurpose purpose, CancellationToken cancellationToken)
        {
            SentMessages.Add(new SentSms(phoneNumber, code, purpose));
            return Task.CompletedTask;
        }
    }

    private sealed record SentSms(string PhoneNumber, string Code, VerificationPurpose Purpose);

    private sealed class FixedDateTimeProvider : IDateTimeProvider
    {
        internal static readonly DateTime UtcNowValue = new(2026, 8, 22, 10, 0, 0, DateTimeKind.Utc);
        public DateTime UtcNow => UtcNowValue;
    }
}
