using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Application.Abstractions.Messaging;
using Ahlena.Modules.Identity.Application.Abstractions.Security;
using Ahlena.Modules.Identity.Application.Authentication;
using Ahlena.Modules.Identity.Application.Authentication.Login;
using Ahlena.Modules.Identity.Application.Authentication.Registration;
using Ahlena.Modules.Identity.Application.Authentication.Tokens;
using Ahlena.Modules.Identity.Domain.Authentication.DeviceSessions;
using Ahlena.Modules.Identity.Domain.Authentication.VerificationRequests;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.UnitTests.Identity.Registration;

public sealed class RegisterUserCommandHandlerTests
{
    [Theory]
    [InlineData(AccountType.Family)]
    [InlineData(AccountType.MedicalCaregiver)]
    [InlineData(AccountType.CompanionCaregiver)]
    [InlineData(AccountType.CareHomeOwner)]
    public async Task Handle_ShouldRegisterUserAndCreateDualVerificationRequests(
        AccountType accountType)
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        FakePasswordHasher passwordHasher =
            new();

        FakeOtpService otpService =
            new();

        RecordingEmailSender emailSender =
            new();

        RecordingSmsSender smsSender =
            new();

        RegisterUserCommandHandler handler =
            CreateHandler(
                dbContext,
                passwordHasher,
                otpService,
                emailSender,
                smsSender);

        RegisterUserCommand command =
            CreateCommand(accountType) with
            {
                Email =
                    "  MOHAMED@EXAMPLE.COM  ",
                PhoneNumber =
                    "  +201001234567  "
            };

        Result<RegisterUserResponse> result =
            await handler.Handle(
                command,
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        User user =
            Assert.Single(
                dbContext.Users);

        Assert.Equal(
            result.Value.UserId,
            user.Id);

        Assert.Equal(
            "mohamed@example.com",
            user.Email!.Value);

        Assert.Equal(
            "+201001234567",
            user.PhoneNumber.Value);

        Assert.True(user.HasPassword);

        Assert.Equal(
            $"hashed::{command.Password}",
            user.Password!.PasswordHash);

        UserAccount account =
            Assert.Single(
                user.Accounts);

        Assert.Equal(
            accountType,
            account.AccountType);

        VerificationRequest[] requests =
            await dbContext
                .VerificationRequests
                .OrderBy(request =>
                    request.Channel)
                .ToArrayAsync();

        Assert.Equal(2, requests.Length);

        VerificationRequest emailRequest =
            Assert.Single(
                requests,
                request =>
                    request.Channel ==
                    VerificationChannel.Email);

        VerificationRequest phoneRequest =
            Assert.Single(
                requests,
                request =>
                    request.Channel ==
                    VerificationChannel.Sms);

        Assert.Equal(
            VerificationPurpose.VerifyEmail,
            emailRequest.Purpose);

        Assert.Equal(
            VerificationPurpose.VerifyPhone,
            phoneRequest.Purpose);

        Assert.Equal(
            result.Value.EmailVerificationRequestId,
            emailRequest.Id);

        Assert.Equal(
            result.Value.PhoneVerificationRequestId,
            phoneRequest.Id);

        Assert.Equal(
            "hash-1",
            emailRequest.OtpHash);

        Assert.Equal(
            "hash-2",
            phoneRequest.OtpHash);

        Assert.Equal(
            FixedDateTimeProvider.UtcNowValue,
            emailRequest.CreatedOnUtc);

        Assert.Equal(
            FixedDateTimeProvider.UtcNowValue
                .Add(OtpPolicy.Lifetime),
            emailRequest.ExpiresOnUtc);

        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);

        EmailMessage emailMessage =
            Assert.Single(
                emailSender.Messages);

        Assert.Equal(
            "mohamed@example.com",
            emailMessage.Email);

        Assert.Equal(
            "code-1",
            emailMessage.Code);

        Assert.Equal(
            VerificationPurpose.VerifyEmail,
            emailMessage.Purpose);

        SmsMessage smsMessage =
            Assert.Single(
                smsSender.Messages);

        Assert.Equal(
            "+201001234567",
            smsMessage.PhoneNumber);

        Assert.Equal(
            "code-2",
            smsMessage.Code);

        Assert.Equal(
            VerificationPurpose.VerifyPhone,
            smsMessage.Purpose);

        Assert.Equal(
            [OtpPolicy.CodeLength, OtpPolicy.CodeLength],
            otpService.RequestedLengths);
    }

    [Fact]
    public async Task Handle_ShouldRejectDuplicateEmail()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        await SeedUserAsync(
            dbContext,
            "mohamed@example.com",
            "+201009999999");

        dbContext.ResetSaveChangesCalls();

        RecordingEmailSender emailSender =
            new();

        RecordingSmsSender smsSender =
            new();

        RegisterUserCommandHandler handler =
            CreateHandler(
                dbContext,
                new FakePasswordHasher(),
                new FakeOtpService(),
                emailSender,
                smsSender);

        Result<RegisterUserResponse> result =
            await handler.Handle(
                CreateCommand(),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            RegistrationErrors.EmailAlreadyInUse,
            result.Error);

        Assert.Single(dbContext.Users);

        Assert.Empty(
            dbContext.VerificationRequests);

        Assert.Empty(emailSender.Messages);
        Assert.Empty(smsSender.Messages);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldRejectDuplicatePhone()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        await SeedUserAsync(
            dbContext,
            "another@example.com",
            "+201001234567");

        dbContext.ResetSaveChangesCalls();

        RegisterUserCommandHandler handler =
            CreateHandler(
                dbContext,
                new FakePasswordHasher(),
                new FakeOtpService(),
                new RecordingEmailSender(),
                new RecordingSmsSender());

        Result<RegisterUserResponse> result =
            await handler.Handle(
                CreateCommand(),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            RegistrationErrors.PhoneAlreadyInUse,
            result.Error);

        Assert.Single(dbContext.Users);

        Assert.Empty(
            dbContext.VerificationRequests);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldRejectElderlyAccountType()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        RegisterUserCommandHandler handler =
            CreateHandler(
                dbContext,
                new FakePasswordHasher(),
                new FakeOtpService(),
                new RecordingEmailSender(),
                new RecordingSmsSender());

        RegisterUserCommand command =
            CreateCommand(
                AccountType.Elderly);

        Result<RegisterUserResponse> result =
            await handler.Handle(
                command,
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            RegistrationErrors.UnsupportedAccountType,
            result.Error);

        Assert.Empty(dbContext.Users);

        Assert.Empty(
            dbContext.VerificationRequests);
    }

    [Fact]
    public async Task Handle_ShouldPreserveUserAndRequests_WhenProviderSendFails_AndLoginRecoversIds()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        RegisterUserCommandHandler handler =
            CreateHandler(
                dbContext,
                new FakePasswordHasher(),
                new FakeOtpService(),
                new ThrowingEmailSender(),
                new RecordingSmsSender());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(
                CreateCommand(),
                CancellationToken.None));

        User user =
            Assert.Single(
                dbContext.Users);

        Assert.Equal(
            UserStatus.PendingVerification,
            user.Status);

        VerificationRequest[] requests =
            await dbContext
                .VerificationRequests
                .OrderBy(request =>
                    request.Purpose)
                .ToArrayAsync();

        Assert.Equal(2, requests.Length);

        Assert.All(
            requests,
            request =>
                Assert.Equal(
                    VerificationStatus.Pending,
                    request.Status));

        VerificationRequest emailRequest =
            Assert.Single(
                requests,
                request =>
                    request.Purpose ==
                    VerificationPurpose.VerifyEmail);

        VerificationRequest phoneRequest =
            Assert.Single(
                requests,
                request =>
                    request.Purpose ==
                    VerificationPurpose.VerifyPhone);

        LoginCommandHandler loginHandler =
            new(
                dbContext,
                new FakePasswordHasher(),
                new FakeAuthTokenService(),
                new FixedDateTimeProvider());

        Result<LoginResponse> loginResult =
            await loginHandler.Handle(
                new LoginCommand(
                    "mohamed@example.com",
                    "StrongPass123",
                    "Pixel 8",
                    DevicePlatform.Android,
                    "1.0.0"),
                CancellationToken.None);

        Assert.True(loginResult.IsSuccess);

        Assert.Equal(
            AuthAccessType.RestrictedVerification,
            loginResult.Value.AccessType);

        Assert.Equal(
            emailRequest.Id,
            loginResult.Value.EmailVerificationRequestId);

        Assert.Equal(
            phoneRequest.Id,
            loginResult.Value.PhoneVerificationRequestId);

        Assert.Null(loginResult.Value.RefreshToken);
        Assert.Null(loginResult.Value.DeviceSessionId);
    }

    private static RegisterUserCommandHandler CreateHandler(
        IIdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        IOtpService otpService,
        IEmailSender emailSender,
        ISmsSender smsSender)
    {
        return new RegisterUserCommandHandler(
            dbContext,
            passwordHasher,
            otpService,
            emailSender,
            smsSender,
            new FixedDateTimeProvider());
    }

    private static RegisterUserCommand CreateCommand(
        AccountType accountType =
            AccountType.Family)
    {
        return new RegisterUserCommand(
            ArabicFullName: "محمد أحمد",
            EnglishFullName: "Mohamed Ahmed",
            Email: "mohamed@example.com",
            PhoneNumber: "+201001234567",
            Password: "StrongPass123",
            AccountType: accountType);
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

    private static async Task SeedUserAsync(
        IdentityTestDbContext dbContext,
        string email,
        string phoneNumber)
    {
        User user =
            User.Create(
                FullName.Create(
                    "مستخدم موجود"),
                FullName.Create(
                    "Existing User"),
                Email.Create(email),
                PhoneNumber.Create(
                    phoneNumber));

        dbContext.Users.Add(user);

        await dbContext.SaveChangesAsync();

        dbContext.ResetSaveChangesCalls();
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

    private sealed class FakePasswordHasher :
        IPasswordHasher
    {
        public string Hash(
            string password)
        {
            return $"hashed::{password}";
        }

        public PasswordVerificationResult Verify(
            string passwordHash,
            string providedPassword)
        {
            return passwordHash ==
                   $"hashed::{providedPassword}"
                ? PasswordVerificationResult.Success
                : PasswordVerificationResult.Failed;
        }
    }

    private sealed class FakeOtpService :
        IOtpService
    {
        private int _generatedCount;

        internal List<int> RequestedLengths
        {
            get;
        } = [];

        public GeneratedOtpCode Generate(
            int length)
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
            return otpHash ==
                   providedCode.Replace(
                       "code",
                       "hash");
        }
    }

    private sealed class RecordingEmailSender :
        IEmailSender
    {
        internal List<EmailMessage> Messages
        {
            get;
        } = [];

        public Task SendEmailAsync(string email, string subject, string body, string? correlationId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SendSupportRequestAsync(
            string senderName,
            string? senderEmail,
            string senderPhoneNumber,
            string accountTypes,
            string subject,
            string message,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

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

        public Task SendFamilyInvitationAsync(
            string email,
            string familyName,
            string inviteLink,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSmsSender :
        ISmsSender
    {
        internal List<SmsMessage> Messages
        {
            get;
        } = [];

        public Task SendVerificationCodeAsync(
            string phoneNumber,
            string code,
            VerificationPurpose purpose,
            CancellationToken cancellationToken)
        {
            Messages.Add(
                new SmsMessage(
                    phoneNumber,
                    code,
                    purpose));

            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingEmailSender :
        IEmailSender
    {
        public Task SendEmailAsync(string email, string subject, string body, string? correlationId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SendSupportRequestAsync(
            string senderName,
            string? senderEmail,
            string senderPhoneNumber,
            string accountTypes,
            string subject,
            string message,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task SendVerificationCodeAsync(
            string email,
            string code,
            VerificationPurpose purpose,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(
                "Email provider OTP delivery failed.");
        }

        public Task SendFamilyInvitationAsync(
            string email,
            string familyName,
            string inviteLink,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuthTokenService :
        IAuthTokenService
    {
        public GeneratedAccessToken GenerateAccessToken(
            User user,
            DateTime utcNow,
            DeviceSessionId? deviceSessionId = null)
        {
            return new GeneratedAccessToken(
                "access-token",
                utcNow.AddMinutes(15));
        }

        public GeneratedAccessToken
            GenerateRestrictedVerificationToken(
                User user,
                DateTime utcNow)
        {
            return new GeneratedAccessToken(
                "restricted-token",
                utcNow.AddMinutes(15));
        }

        public GeneratedRefreshToken GenerateRefreshToken(
            DateTime utcNow)
        {
            return new GeneratedRefreshToken(
                "refresh-token",
                "refresh-token-hash",
                utcNow.AddDays(30));
        }

        public bool VerifyRefreshToken(
            string providedToken,
            string storedHash)
        {
            return false;
        }
    }

    private sealed record EmailMessage(
        string Email,
        string Code,
        VerificationPurpose Purpose);

    private sealed record SmsMessage(
        string PhoneNumber,
        string Code,
        VerificationPurpose Purpose);
}
