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
    public async Task RequestReset_ShouldReturnSuccessWithoutSideEffects_WhenUserNotFound()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

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
                    "unknown@example.com"),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Empty(
            dbContext.VerificationRequests);

        Assert.Empty(
            otpService.RequestedLengths);

        Assert.Empty(
            emailSender.Messages);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task RequestReset_ShouldReturnSuccessWithoutSideEffects_WhenWithinCooldown()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserWithPasswordAsync(
                dbContext);

        DateTime createdOnUtc =
            FixedDateTimeProvider.UtcNowValue
                .AddSeconds(-30);

        VerificationRequest pendingRequest =
            VerificationRequest.Create(
                user.Id,
                "mohamed@example.com",
                "existing-otp-hash",
                VerificationChannel.Email,
                VerificationPurpose.ResetPassword,
                createdOnUtc,
                createdOnUtc.Add(
                    OtpPolicy.Lifetime));

        dbContext.VerificationRequests.Add(
            pendingRequest);

        await dbContext.SaveChangesAsync();

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

        Assert.Empty(
            otpService.RequestedLengths);

        Assert.Empty(
            emailSender.Messages);

        Assert.Equal(
            VerificationStatus.Pending,
            pendingRequest.Status);

        VerificationRequest storedRequest =
            Assert.Single(
                dbContext.VerificationRequests);

        Assert.Equal(
            pendingRequest.Id,
            storedRequest.Id);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task RequestReset_ShouldReplacePendingRequest_WhenCooldownHasElapsed()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserWithPasswordAsync(
                dbContext);

        DateTime createdOnUtc =
            FixedDateTimeProvider.UtcNowValue
                .Subtract(
                    OtpPolicy.ResendCooldown);

        VerificationRequest pendingRequest =
            VerificationRequest.Create(
                user.Id,
                "mohamed@example.com",
                "existing-otp-hash",
                VerificationChannel.Email,
                VerificationPurpose.ResetPassword,
                createdOnUtc,
                createdOnUtc.Add(
                    OtpPolicy.Lifetime));

        dbContext.VerificationRequests.Add(
            pendingRequest);

        await dbContext.SaveChangesAsync();

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

        Assert.Equal(
            VerificationStatus.Invalidated,
            pendingRequest.Status);

        Assert.Equal(
            FixedDateTimeProvider.UtcNowValue,
            pendingRequest.InvalidatedOnUtc);

        VerificationRequest[] requests =
            await dbContext.VerificationRequests
                .ToArrayAsync();

        Assert.Equal(
            2,
            requests.Length);

        VerificationRequest replacementRequest =
            Assert.Single(
                requests,
                item =>
                    item.Status ==
                    VerificationStatus.Pending);

        Assert.Equal(
            FixedDateTimeProvider.UtcNowValue,
            replacementRequest.CreatedOnUtc);

        Assert.Equal(
            FixedDateTimeProvider.UtcNowValue
                .Add(OtpPolicy.Lifetime),
            replacementRequest.ExpiresOnUtc);

        Assert.Single(
            otpService.RequestedLengths);

        Assert.Single(
            emailSender.Messages);

        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);
    }

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
            new(
                verifyResult: true);

        FakePasswordHasher passwordHasher =
            new(
                PasswordVerificationResult.Failed);

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

        Assert.Equal(
            new[] { "NewPassword1" },
            passwordHasher.HashedPasswords);

        Assert.Equal(
            "hashed::NewPassword1",
            user.Password!.PasswordHash);

        Assert.True(
            session1.IsRevoked);

        Assert.True(
            session2.IsRevoked);

        Assert.Equal(
            "Password was reset.",
            session1.RevocationReason);

        Assert.Equal(
            "Password was reset.",
            session2.RevocationReason);

        Assert.Equal(
            VerificationStatus.Verified,
            resetRequest.Status);

        Assert.Equal(
            utcNow,
            resetRequest.VerifiedOnUtc);

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

        dbContext.ResetSaveChangesCalls();

        FakePasswordHasher passwordHasher =
            new();

        ResetPasswordCommandHandler handler =
            new(
                dbContext,
                passwordHasher,
                new FakeOtpService(
                    verifyResult: false),
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

        Assert.Equal(
            1,
            resetRequest.Attempts);

        Assert.Equal(
            VerificationStatus.Pending,
            resetRequest.Status);

        Assert.Empty(
            passwordHasher.VerificationRequests);

        Assert.Empty(
            passwordHasher.HashedPasswords);

        Assert.Equal(
            "hashed::CurrentPass1",
            user.Password!.PasswordHash);

        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Reset_ShouldRejectWhenNoPendingRequest()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserWithPasswordAsync(
                dbContext);

        dbContext.ResetSaveChangesCalls();

        FakePasswordHasher passwordHasher =
            new();

        ResetPasswordCommandHandler handler =
            new(
                dbContext,
                passwordHasher,
                new FakeOtpService(
                    verifyResult: true),
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

        Assert.Empty(
            passwordHasher.VerificationRequests);

        Assert.Empty(
            passwordHasher.HashedPasswords);

        Assert.Equal(
            "hashed::CurrentPass1",
            user.Password!.PasswordHash);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Reset_ShouldRejectWhenUserIsNoLongerActive()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserWithPasswordAsync(
                dbContext);

        DateTime utcNow =
            FixedDateTimeProvider.UtcNowValue;

        DateTime createdOnUtc =
            utcNow.AddSeconds(-30);

        VerificationRequest resetRequest =
            VerificationRequest.Create(
                user.Id,
                "mohamed@example.com",
                "otp-hash",
                VerificationChannel.Email,
                VerificationPurpose.ResetPassword,
                createdOnUtc,
                createdOnUtc.Add(
                    OtpPolicy.Lifetime));

        dbContext.VerificationRequests.Add(
            resetRequest);

        await dbContext.SaveChangesAsync();

        user.Suspend(
            "Security review.",
            utcNow.AddSeconds(-10));

        await dbContext.SaveChangesAsync();

        dbContext.ResetSaveChangesCalls();

        FakeOtpService otpService =
            new(
                verifyResult: true);

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

        Assert.Equal(
            PasswordErrors.UserNotActive,
            result.Error);

        Assert.Equal(
            VerificationStatus.Pending,
            resetRequest.Status);

        Assert.Empty(
            otpService.VerificationRequests);

        Assert.Empty(
            passwordHasher.VerificationRequests);

        Assert.Empty(
            passwordHasher.HashedPasswords);

        Assert.Equal(
            "hashed::CurrentPass1",
            user.Password!.PasswordHash);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Reset_ShouldRejectCurrentPasswordReuse()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserWithPasswordAsync(
                dbContext);

        DeviceSession session =
            await SeedSessionAsync(
                dbContext,
                user.Id,
                "session-hash");

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

        FakePasswordHasher passwordHasher =
            new(
                PasswordVerificationResult.Success);

        ResetPasswordCommandHandler handler =
            new(
                dbContext,
                passwordHasher,
                new FakeOtpService(
                    verifyResult: true),
                new FixedDateTimeProvider());

        Result result =
            await handler.Handle(
                new ResetPasswordCommand(
                    "mohamed@example.com",
                    "123456",
                    "CurrentPass1"),
                CancellationToken.None);

        Assert.Equal(
            PasswordErrors.NewPasswordMustDiffer,
            result.Error);

        Assert.Equal(
            VerificationStatus.Pending,
            resetRequest.Status);

        Assert.Null(
            resetRequest.VerifiedOnUtc);

        Assert.False(
            session.IsRevoked);

        Assert.Empty(
            passwordHasher.HashedPasswords);

        Assert.Equal(
            "hashed::CurrentPass1",
            user.Password!.PasswordHash);

        Assert.Single(
            passwordHasher.VerificationRequests);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
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
                PasswordVerificationResult
                    .SuccessRehashNeeded,
                PasswordVerificationResult.Failed);

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

        Assert.Equal(
            2,
            passwordHasher.VerificationRequests.Count);

        Assert.Equal(
            new[] { "NewPassword1" },
            passwordHasher.HashedPasswords);

        Assert.Equal(
            "hashed::NewPassword1",
            user.Password!.PasswordHash);

        Assert.True(
            session1.IsRevoked);

        Assert.True(
            session2.IsRevoked);

        Assert.Equal(
            "Password was changed.",
            session1.RevocationReason);

        Assert.Equal(
            "Password was changed.",
            session2.RevocationReason);

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

        FakePasswordHasher passwordHasher =
            new(
                PasswordVerificationResult.Failed);

        ChangePasswordCommandHandler handler =
            new(
                dbContext,
                passwordHasher,
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

        Assert.Single(
            passwordHasher.VerificationRequests);

        Assert.Empty(
            passwordHasher.HashedPasswords);

        Assert.Equal(
            "hashed::CurrentPass1",
            user.Password!.PasswordHash);
    }

    [Fact]
    public async Task Change_ShouldRejectNonActiveUser()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedPendingUserWithPasswordAsync(
                dbContext);

        FakePasswordHasher passwordHasher =
            new();

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

        Assert.Equal(
            PasswordErrors.UserNotActive,
            result.Error);

        Assert.Empty(
            passwordHasher.VerificationRequests);

        Assert.Empty(
            passwordHasher.HashedPasswords);

        Assert.Equal(
            "hashed::CurrentPass1",
            user.Password!.PasswordHash);
    }

    [Fact]
    public async Task Change_ShouldRejectCurrentPasswordReuse()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveUserWithPasswordAsync(
                dbContext);

        DeviceSession session =
            await SeedSessionAsync(
                dbContext,
                user.Id,
                "session-hash");

        dbContext.ResetSaveChangesCalls();

        FakePasswordHasher passwordHasher =
            new(
                PasswordVerificationResult.Success,
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
                    "CurrentPass1"),
                CancellationToken.None);

        Assert.Equal(
            PasswordErrors.NewPasswordMustDiffer,
            result.Error);

        Assert.Equal(
            2,
            passwordHasher.VerificationRequests.Count);

        Assert.Empty(
            passwordHasher.HashedPasswords);

        Assert.Equal(
            "hashed::CurrentPass1",
            user.Password!.PasswordHash);

        Assert.False(
            session.IsRevoked);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
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

        dbContext.Users.Add(
            user);

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

        dbContext.Users.Add(
            user);

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

        dbContext.DeviceSessions.Add(
            session);

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

        internal List<int> RequestedLengths
        {
            get;
        } = [];

        internal List<(
            string ProvidedCode,
            string OtpHash)> VerificationRequests
        {
            get;
        } = [];

        internal FakeOtpService(
            bool verifyResult = true)
        {
            _verifyResult = verifyResult;
        }

        public GeneratedOtpCode Generate(
            int length)
        {
            RequestedLengths.Add(
                length);

            _generatedCount++;

            return new GeneratedOtpCode(
                $"code-{_generatedCount}",
                $"hash-{_generatedCount}");
        }

        public bool Verify(
            string providedCode,
            string otpHash)
        {
            VerificationRequests.Add(
                (
                    providedCode,
                    otpHash));

            return _verifyResult;
        }
    }

    private sealed class FakePasswordHasher :
        IPasswordHasher
    {
        private readonly Queue<
            PasswordVerificationResult>
            _verificationResults;

        internal List<string> HashedPasswords
        {
            get;
        } = [];

        internal List<(
            string PasswordHash,
            string ProvidedPassword)>
            VerificationRequests
        {
            get;
        } = [];

        internal FakePasswordHasher(
            params PasswordVerificationResult[]
                verificationResults)
        {
            _verificationResults =
                new Queue<
                    PasswordVerificationResult>(
                        verificationResults);
        }

        public string Hash(
            string password)
        {
            HashedPasswords.Add(
                password);

            return $"hashed::{password}";
        }

        public PasswordVerificationResult Verify(
            string passwordHash,
            string providedPassword)
        {
            VerificationRequests.Add(
                (
                    passwordHash,
                    providedPassword));

            if (_verificationResults.Count == 0)
            {
                throw new InvalidOperationException(
                    "No password verification result " +
                    "was configured for this call.");
            }

            return _verificationResults.Dequeue();
        }
    }

    private sealed class RecordingEmailSender :
        IEmailSender
    {
        internal List<EmailMessage> Messages
        {
            get;
        } = [];

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