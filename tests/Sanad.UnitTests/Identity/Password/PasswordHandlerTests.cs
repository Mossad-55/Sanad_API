using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication;
using Sanad.Modules.Identity.Application.Authentication.Password;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.Password;

public sealed class PasswordHandlerTests
{
    [Fact]
    public async Task RequestReset_ShouldCreateOtpAndSendEmail()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserWithPasswordAsync(
                dbContext);

        dbContext.ResetSaveChangesCalls();

        FakeOtpService otpService =
            new();

        RecordingEmailSender emailSender =
            new();

        RequestPasswordResetCommandHandler handler =
            new(
                dbContext,
                otpService,
                emailSender,
                new FixedDateTimeProvider());

        Result result =
            await handler.Handle(
                new RequestPasswordResetCommand(
                    "mohamed@example.com"),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Single(
            otpService.RequestedLengths);

        Assert.Equal(
            OtpPolicy.CodeLength,
            otpService.RequestedLengths[0]);

        RecordingEmailSender.EmailMessage message =
            Assert.Single(
                emailSender.Messages);

        Assert.Equal(
            "mohamed@example.com",
            message.Email);

        Assert.Equal(
            VerificationPurpose.ResetPassword,
            message.Purpose);

        VerificationRequest? request =
            await dbContext.VerificationRequests
                .SingleOrDefaultAsync(
                    item =>
                        item.UserId ==
                            user.Id &&
                        item.Purpose ==
                            VerificationPurpose
                                .ResetPassword);

        Assert.NotNull(request);

        Assert.Equal(
            VerificationStatus.Pending,
            request.Status);

        Assert.Equal(
            FixedDateTimeProvider.UtcNowValue,
            request.CreatedOnUtc);

        Assert.Equal(
            FixedDateTimeProvider.UtcNowValue
                .Add(OtpPolicy.Lifetime),
            request.ExpiresOnUtc);

        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task RequestReset_ShouldReturnSuccess_WhenUserNotFound()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        RequestPasswordResetCommandHandler handler =
            new(
                dbContext,
                new FakeOtpService(),
                new RecordingEmailSender(),
                new FixedDateTimeProvider());

        Result result =
            await handler.Handle(
                new RequestPasswordResetCommand(
                    "unknown@example.com"),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Empty(
            dbContext.VerificationRequests);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    // ═══════════════════════════════════════════════════════════════
    // ResetPasswordCommandHandler Tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Reset_ShouldUpdatePasswordAndRevokeAllSessions()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserWithPasswordAsync(
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

        DateTime utcNow =
            FixedDateTimeProvider.UtcNowValue;

        VerificationRequest resetRequest =
            VerificationRequest.Create(
                user.Id,
                "mohamed@example.com",
                "otp-hash",
                VerificationChannel.Email,
                VerificationPurpose.ResetPassword,
                utcNow.AddMinutes(-2),
                utcNow.AddMinutes(3));

        dbContext.VerificationRequests.Add(
            resetRequest);

        await dbContext.SaveChangesAsync();

        dbContext.ResetSaveChangesCalls();

        FakeOtpService otpService =
            new(verifyResult: true);

        FakePasswordHasher passwordHasher =
            new();

        ResetPasswordCommandHandler handler =
            new(
                dbContext,
                passwordHasher,
                otpService,
                new FixedDateTimeProvider());

        Result result =
            await handler.Handle(
                new ResetPasswordCommand(
                    "mohamed@example.com",
                    "123456",
                    "NewPassword1"),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.True(session1.IsRevoked);

        Assert.True(session2.IsRevoked);

        Assert.Equal(
            "Password was reset.",
            session1.RevocationReason);

        Assert.Equal(
            VerificationStatus.Verified,
            resetRequest.Status);

        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Reset_ShouldRejectInvalidOtp()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserWithPasswordAsync(
                dbContext);

        DateTime utcNow =
            FixedDateTimeProvider.UtcNowValue;

        VerificationRequest resetRequest =
            VerificationRequest.Create(
                user.Id,
                "mohamed@example.com",
                "otp-hash",
                VerificationChannel.Email,
                VerificationPurpose.ResetPassword,
                utcNow.AddMinutes(-2),
                utcNow.AddMinutes(3));

        dbContext.VerificationRequests.Add(
            resetRequest);

        await dbContext.SaveChangesAsync();

        ResetPasswordCommandHandler handler =
            new(
                dbContext,
                new FakePasswordHasher(),
                new FakeOtpService(verifyResult: false),
                new FixedDateTimeProvider());

        Result result =
            await handler.Handle(
                new ResetPasswordCommand(
                    "mohamed@example.com",
                    "999999",
                    "NewPassword1"),
                CancellationToken.None);

        Assert.Equal(
            PasswordErrors.OtpVerificationFailed,
            result.Error);
    }

    [Fact]
    public async Task Reset_ShouldRejectWhenNoPendingRequest()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        await SeedActiveUserWithPasswordAsync(
            dbContext);

        ResetPasswordCommandHandler handler =
            new(
                dbContext,
                new FakePasswordHasher(),
                new FakeOtpService(verifyResult: true),
                new FixedDateTimeProvider());

        Result result =
            await handler.Handle(
                new ResetPasswordCommand(
                    "mohamed@example.com",
                    "123456",
                    "NewPassword1"),
                CancellationToken.None);

        Assert.Equal(
            PasswordErrors.PendingRequestNotFound,
            result.Error);
    }

    [Fact]
    public async Task Change_ShouldUpdatePasswordAndRevokeAllSessions()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserWithPasswordAsync(
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

        dbContext.ResetSaveChangesCalls();

        FakePasswordHasher passwordHasher =
            new(
                verifyResult:
                    PasswordVerificationResult.Success);

        ChangePasswordCommandHandler handler =
            new(
                dbContext,
                passwordHasher,
                new FixedDateTimeProvider());

        Result result =
            await handler.Handle(
                new ChangePasswordCommand(
                    user.Id,
                    "CurrentPass1",
                    "NewPassword1"),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.True(session1.IsRevoked);

        Assert.True(session2.IsRevoked);

        Assert.Equal(
            "Password was changed.",
            session1.RevocationReason);

        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Change_ShouldRejectWrongCurrentPassword()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserWithPasswordAsync(
                dbContext);

        ChangePasswordCommandHandler handler =
            new(
                dbContext,
                new FakePasswordHasher(
                    verifyResult:
                        PasswordVerificationResult.Failed),
                new FixedDateTimeProvider());

        Result result =
            await handler.Handle(
                new ChangePasswordCommand(
                    user.Id,
                    "WrongPassword",
                    "NewPassword1"),
                CancellationToken.None);

        Assert.Equal(
            PasswordErrors.InvalidCurrentPassword,
            result.Error);
    }

    [Fact]
    public async Task Change_ShouldRejectNonActiveUser()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedPendingUserWithPasswordAsync(
                dbContext);

        ChangePasswordCommandHandler handler =
            new(
                dbContext,
                new FakePasswordHasher(),
                new FixedDateTimeProvider());

        Result result =
            await handler.Handle(
                new ChangePasswordCommand(
                    user.Id,
                    "CurrentPass1",
                    "NewPassword1"),
                CancellationToken.None);

        Assert.Equal(
            PasswordErrors.UserNotActive,
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

    private static async Task<User> SeedActiveUserWithPasswordAsync(
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
            "hashed::CurrentPass1",
            FixedDateTimeProvider.UtcNowValue);

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

    private static async Task<User> SeedPendingUserWithPasswordAsync(
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
            "hashed::CurrentPass1",
            FixedDateTimeProvider.UtcNowValue);

        dbContext.Users.Add(user);

        await dbContext.SaveChangesAsync();

        return user;
    }

    private static async Task<DeviceSession> SeedSessionAsync(
        IdentityTestDbContext dbContext,
        UserId userId,
        string tokenHash)
    {
        DeviceSession session =
            DeviceSession.Create(
                userId,
                "iPhone 16",
                DevicePlatform.iOS,
                "1.0.0",
                tokenHash,
                FixedDateTimeProvider.UtcNowValue
                    .AddDays(-1),
                FixedDateTimeProvider.UtcNowValue
                    .AddDays(29));

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

    private sealed class FakeOtpService :
        IOtpService
    {
        private readonly bool _verifyResult;

        private int _generatedCount;

        internal List<int> RequestedLengths { get; } = [];

        internal FakeOtpService(bool verifyResult = true)
        {
            _verifyResult = verifyResult;
        }

        public GeneratedOtpCode Generate(int length)
        {
            RequestedLengths.Add(length);

            _generatedCount++;

            return new GeneratedOtpCode(
                $"code-{_generatedCount}",
                $"hash-{_generatedCount}");
        }

        public bool Verify(
            string providedCode,
            string otpHash)
        {
            return _verifyResult;
        }
    }

    private sealed class FakePasswordHasher :
        IPasswordHasher
    {
        private readonly PasswordVerificationResult
            _verifyResult;

        internal FakePasswordHasher(
            PasswordVerificationResult verifyResult =
                PasswordVerificationResult.Success)
        {
            _verifyResult = verifyResult;
        }

        public string Hash(string password)
        {
            return $"hashed::{password}";
        }

        public PasswordVerificationResult Verify(
            string passwordHash,
            string providedPassword)
        {
            return _verifyResult;
        }
    }

    private sealed class RecordingEmailSender :
        IEmailSender
    {
        internal List<EmailMessage> Messages { get; } = [];

        public Task SendVerificationCodeAsync(
            string email,
            string code,
            VerificationPurpose purpose,
            CancellationToken cancellationToken)
        {
            Messages.Add(
                new EmailMessage(
                    email,
                    code,
                    purpose));

            return Task.CompletedTask;
        }

        internal sealed record EmailMessage(
            string Email,
            string Code,
            VerificationPurpose Purpose);
    }
}