using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication.SocialLogin;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.SocialLogin;

public sealed class CompleteSocialRegistrationCommandHandlerTests
{
    [Fact]
    public async Task Handle_ShouldCreateActiveUserLinkAndSession_ForValidChallengeAndOtp()
    {
        await using IdentityTestDbContext db = CreateDb();
        VerificationRequest otp = await SeedOtpAsync(db);
        db.ResetSaveChangesCalls();

        Result<StartSocialLoginResponse> result = await CreateHandler(db, CreateChallenge(otp.Id), true)
            .Handle(CreateCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AuthAccessType.Normal, result.Value.AccessType);
        Assert.Equal("access-token", result.Value.AccessToken);
        Assert.Equal("refresh-token", result.Value.RefreshToken);
        User user = Assert.Single(db.Users);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.True(user.EmailVerified);
        Assert.True(user.PhoneVerified);
        Assert.Equal(AccountType.Family, Assert.Single(user.Accounts).AccountType);
        Assert.Equal("google-subject", Assert.Single(user.ExternalLogins).ProviderSubject);
        Assert.Equal(VerificationStatus.Verified, otp.Status);
        Assert.Single(db.DeviceSessions);
        Assert.Equal(1, db.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldPersistFailedAttemptWithoutUserOrSession_ForInvalidOtp()
    {
        await using IdentityTestDbContext db = CreateDb();
        VerificationRequest otp = await SeedOtpAsync(db);
        db.ResetSaveChangesCalls();

        Result<StartSocialLoginResponse> result = await CreateHandler(db, CreateChallenge(otp.Id), false)
            .Handle(CreateCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialLoginErrors.SocialRegistrationFailed, result.Error);
        Assert.Equal(1, otp.Attempts);
        Assert.Equal(VerificationStatus.Pending, otp.Status);
        Assert.Empty(db.Users);
        Assert.Empty(db.DeviceSessions);
        Assert.Equal(1, db.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldExpireOtpWithoutUserOrSession()
    {
        await using IdentityTestDbContext db = CreateDb();
        VerificationRequest otp = await SeedOtpAsync(db, FixedClock.Now.AddMinutes(-5));
        db.ResetSaveChangesCalls();

        Result<StartSocialLoginResponse> result = await CreateHandler(db, CreateChallenge(otp.Id), true)
            .Handle(CreateCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(VerificationStatus.Expired, otp.Status);
        Assert.Empty(db.Users);
        Assert.Empty(db.DeviceSessions);
        Assert.Equal(1, db.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldReturnGenericFailure_ForMismatchedOtpTarget()
    {
        await using IdentityTestDbContext db = CreateDb();
        VerificationRequest otp = VerificationRequest.Create(null, "+201009999999", "otp-hash",
            VerificationChannel.Sms, VerificationPurpose.VerifyPhone, FixedClock.Now, FixedClock.Now.AddMinutes(5));
        db.VerificationRequests.Add(otp);
        await db.SaveChangesAsync();
        db.ResetSaveChangesCalls();

        Result<StartSocialLoginResponse> result = await CreateHandler(db, CreateChallenge(otp.Id), true)
            .Handle(CreateCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialLoginErrors.SocialRegistrationFailed, result.Error);
        Assert.Equal(VerificationStatus.Pending, otp.Status);
        Assert.Empty(db.Users);
        Assert.Equal(0, db.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldReturnGenericFailureWithoutConsumingOtp_ForDuplicatePhone()
    {
        await using IdentityTestDbContext db = CreateDb();
        User existing = User.Create(FullName.Create("أحمد محمد"), FullName.Create("Ahmed Mohamed"),
            Email.Create("existing@example.com"), PhoneNumber.Create("+201001234567"));
        existing.AddAccount(AccountType.Family);
        db.Users.Add(existing);
        VerificationRequest otp = await SeedOtpAsync(db);
        await db.SaveChangesAsync();
        db.ResetSaveChangesCalls();

        Result<StartSocialLoginResponse> result = await CreateHandler(db, CreateChallenge(otp.Id), true)
            .Handle(CreateCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialLoginErrors.SocialRegistrationFailed, result.Error);
        Assert.Equal(VerificationStatus.Pending, otp.Status);
        Assert.Single(db.Users);
        Assert.Empty(db.DeviceSessions);
        Assert.Equal(0, db.SaveChangesCalls);
    }

    private static CompleteSocialRegistrationCommandHandler CreateHandler(IdentityTestDbContext db, SocialRegistrationChallenge? challenge, bool validOtp)
        => new(db, new ChallengeStore(challenge), new OtpService(validOtp), new TokenService(), new FixedClock());

    private static IdentityTestDbContext CreateDb() => new(new DbContextOptionsBuilder<IdentityTestDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<VerificationRequest> SeedOtpAsync(IdentityTestDbContext db, DateTime? created = null)
    {
        DateTime now = created ?? FixedClock.Now;
        VerificationRequest otp = VerificationRequest.Create(null, "+201001234567", "otp-hash",
            VerificationChannel.Sms, VerificationPurpose.VerifyPhone, now, now.AddMinutes(5));
        db.VerificationRequests.Add(otp);
        await db.SaveChangesAsync();
        return otp;
    }

    private static SocialRegistrationChallenge CreateChallenge(VerificationRequestId requestId) => new(
        ExternalLoginProvider.Google, "google-subject", "mohamed@example.com", "محمد أحمد", "Mohamed Ahmed",
        AccountType.Family, "+201001234567", requestId, FixedClock.Now.AddMinutes(10));

    private static CompleteSocialRegistrationCommand CreateCommand() => new("registration-challenge", "123456", "iPhone", DevicePlatform.iOS, "1.0.0");

    private sealed class ChallengeStore(
        SocialRegistrationChallenge? challenge) :
        ISocialRegistrationChallengeStore
    {
        public Task<string> CreateAsync(
            SocialRegistrationChallenge challenge,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<SocialRegistrationChallenge?>
            GetActiveAsync(
                string opaqueChallenge,
                DateTime utcNow,
                CancellationToken cancellationToken)
        {
            return Task.FromResult(
                challenge);
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

    private sealed class OtpService(bool valid) : IOtpService
    {
        public GeneratedOtpCode Generate(int length) => throw new NotSupportedException();
        public bool Verify(string providedCode, string otpHash) => valid;
    }

    private sealed class TokenService : IAuthTokenService
    {
        public GeneratedAccessToken GenerateAccessToken(User user, DateTime utcNow) => new("access-token", utcNow.AddMinutes(15));
        public GeneratedAccessToken GenerateRestrictedVerificationToken(User user, DateTime utcNow) => throw new NotSupportedException();
        public GeneratedRefreshToken GenerateRefreshToken(DateTime utcNow) => new("refresh-token", "refresh-token-hash", utcNow.AddDays(30));
        public bool VerifyRefreshToken(string providedToken, string storedHash) => throw new NotSupportedException();
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        internal static readonly DateTime Now = new(2026, 8, 22, 10, 0, 0, DateTimeKind.Utc);
        public DateTime UtcNow => Now;
    }
}
