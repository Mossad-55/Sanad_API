using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication.Registration;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Domain.Users.Events;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.Account;

public sealed class UpdateMyProfileCommandHandlerTests
{
    [Fact]
    public async Task Handle_ShouldChangeEmailAndRequireReverification()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        RecordingEmailSender emailSender =
            new();

        RecordingSmsSender smsSender =
            new();

        UpdateMyProfileCommandHandler handler =
            CreateHandler(
                dbContext,
                new FakeOtpService(),
                emailSender,
                smsSender);

        User user =
            await SeedUserAsync(
                dbContext);

        user.VerifyEmail(
            CreateUtcDateTime());

        await dbContext.SaveChangesAsync();

        dbContext.ResetSaveChangesCalls();

        user.ClearDomainEvents();

        UpdateMyProfileCommand command =
            new(
                user.Id,
                ArabicFullName: null,
                EnglishFullName: null,
                Email: "  NEW@EXAMPLE.COM  ",
                PhoneNumber: null);

        Result<UpdateMyProfileResponse> result =
            await handler.Handle(
                command,
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            "new@example.com",
            user.Email!.Value);

        Assert.False(user.EmailVerified);
        Assert.False(result.Value.EmailVerified);

        Assert.Null(
            result.Value.PhoneVerificationRequestId);

        UserContactChangedDomainEvent domainEvent =
            Assert.Single(
                user.DomainEvents
                    .OfType<
                        UserContactChangedDomainEvent>());

        Assert.Equal(
            UserContactType.Email,
            domainEvent.ContactType);

        VerificationRequest verificationRequest =
            Assert.Single(
                dbContext.VerificationRequests);

        Assert.Equal(
            result.Value.EmailVerificationRequestId,
            verificationRequest.Id);

        Assert.Equal(
            VerificationChannel.Email,
            verificationRequest.Channel);

        Assert.Equal(
            VerificationPurpose.VerifyEmail,
            verificationRequest.Purpose);

        Assert.Equal(
            "new@example.com",
            verificationRequest.Target);

        EmailMessage emailMessage =
            Assert.Single(
                emailSender.Messages);

        Assert.Equal(
            "new@example.com",
            emailMessage.Email);

        Assert.Equal(
            "code-1",
            emailMessage.Code);

        Assert.Empty(smsSender.Messages);

        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldChangePhoneAndRequireReverification()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        RecordingEmailSender emailSender =
            new();

        RecordingSmsSender smsSender =
            new();

        UpdateMyProfileCommandHandler handler =
            CreateHandler(
                dbContext,
                new FakeOtpService(),
                emailSender,
                smsSender);

        User user =
            await SeedUserAsync(
                dbContext);

        user.VerifyPhone(
            CreateUtcDateTime());

        await dbContext.SaveChangesAsync();

        dbContext.ResetSaveChangesCalls();

        user.ClearDomainEvents();

        UpdateMyProfileCommand command =
            new(
                user.Id,
                ArabicFullName: null,
                EnglishFullName: null,
                Email: null,
                PhoneNumber: "+201009876543");

        Result<UpdateMyProfileResponse> result =
            await handler.Handle(
                command,
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            "+201009876543",
            user.PhoneNumber.Value);

        Assert.False(user.PhoneVerified);
        Assert.False(result.Value.PhoneVerified);

        Assert.Null(
            result.Value.EmailVerificationRequestId);

        UserContactChangedDomainEvent domainEvent =
            Assert.Single(
                user.DomainEvents
                    .OfType<
                        UserContactChangedDomainEvent>());

        Assert.Equal(
            UserContactType.Phone,
            domainEvent.ContactType);

        VerificationRequest verificationRequest =
            Assert.Single(
                dbContext.VerificationRequests);

        Assert.Equal(
            result.Value.PhoneVerificationRequestId,
            verificationRequest.Id);

        Assert.Equal(
            VerificationChannel.Sms,
            verificationRequest.Channel);

        Assert.Equal(
            VerificationPurpose.VerifyPhone,
            verificationRequest.Purpose);

        Assert.Equal(
            "+201009876543",
            verificationRequest.Target);

        SmsMessage smsMessage =
            Assert.Single(
                smsSender.Messages);

        Assert.Equal(
            "+201009876543",
            smsMessage.PhoneNumber);

        Assert.Equal(
            "code-1",
            smsMessage.Code);

        Assert.Empty(emailSender.Messages);

        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldChangeNamesOnly_WithoutVerificationRequests()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        RecordingEmailSender emailSender =
            new();

        RecordingSmsSender smsSender =
            new();

        UpdateMyProfileCommandHandler handler =
            CreateHandler(
                dbContext,
                new FakeOtpService(),
                emailSender,
                smsSender);

        User user =
            await SeedUserAsync(
                dbContext);

        dbContext.ResetSaveChangesCalls();

        user.ClearDomainEvents();

        UpdateMyProfileCommand command =
            new(
                user.Id,
                ArabicFullName: "محمد جديد",
                EnglishFullName: "Mohamed New",
                Email: null,
                PhoneNumber: null);

        Result<UpdateMyProfileResponse> result =
            await handler.Handle(
                command,
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            "محمد جديد",
            user.ArabicFullName.Value);

        Assert.Equal(
            "Mohamed New",
            user.EnglishFullName.Value);

        Assert.Equal(
            "mohamed@example.com",
            user.Email!.Value);

        Assert.Equal(
            "+201001234567",
            user.PhoneNumber.Value);

        Assert.Null(
            result.Value.EmailVerificationRequestId);

        Assert.Null(
            result.Value.PhoneVerificationRequestId);

        Assert.Empty(user.DomainEvents);

        Assert.Empty(
            dbContext.VerificationRequests);

        Assert.Empty(emailSender.Messages);
        Assert.Empty(smsSender.Messages);

        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldRejectEmailAlreadyInUse()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        await SeedUserAsync(
            dbContext,
            "taken@example.com",
            "+201009999999");

        User user =
            await SeedUserAsync(
                dbContext);

        dbContext.ResetSaveChangesCalls();

        UpdateMyProfileCommandHandler handler =
            CreateHandler(
                dbContext,
                new FakeOtpService(),
                new RecordingEmailSender(),
                new RecordingSmsSender());

        UpdateMyProfileCommand command =
            new(
                user.Id,
                ArabicFullName: null,
                EnglishFullName: null,
                Email: "taken@example.com",
                PhoneNumber: null);

        Result<UpdateMyProfileResponse> result =
            await handler.Handle(
                command,
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            RegistrationErrors.EmailAlreadyInUse,
            result.Error);

        Assert.Equal(
            "mohamed@example.com",
            user.Email!.Value);

        Assert.Empty(
            dbContext.VerificationRequests);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldRejectPhoneAlreadyInUse()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        await SeedUserAsync(
            dbContext,
            "another@example.com",
            "+201001234567");

        User user =
            await SeedUserAsync(
                dbContext,
                "mohamed@example.com",
                "+201008888888");

        dbContext.ResetSaveChangesCalls();

        UpdateMyProfileCommandHandler handler =
            CreateHandler(
                dbContext,
                new FakeOtpService(),
                new RecordingEmailSender(),
                new RecordingSmsSender());

        UpdateMyProfileCommand command =
            new(
                user.Id,
                ArabicFullName: null,
                EnglishFullName: null,
                Email: null,
                PhoneNumber: "+201001234567");

        Result<UpdateMyProfileResponse> result =
            await handler.Handle(
                command,
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            RegistrationErrors.PhoneAlreadyInUse,
            result.Error);

        Assert.Equal(
            "+201008888888",
            user.PhoneNumber.Value);

        Assert.Empty(
            dbContext.VerificationRequests);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldKeepAccountTypeUnchanged()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext);

        user.AddAccount(
            AccountType.Family);

        await dbContext.SaveChangesAsync();

        dbContext.ResetSaveChangesCalls();

        UpdateMyProfileCommandHandler handler =
            CreateHandler(
                dbContext,
                new FakeOtpService(),
                new RecordingEmailSender(),
                new RecordingSmsSender());

        UpdateMyProfileCommand command =
            new(
                user.Id,
                ArabicFullName: null,
                EnglishFullName: null,
                Email: "new@example.com",
                PhoneNumber: null);

        Result<UpdateMyProfileResponse> result =
            await handler.Handle(
                command,
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        UserAccount account =
            Assert.Single(user.Accounts);

        Assert.Equal(
            AccountType.Family,
            account.AccountType);
    }

    private static UpdateMyProfileCommandHandler CreateHandler(
        IIdentityDbContext dbContext,
        IOtpService otpService,
        IEmailSender emailSender,
        ISmsSender smsSender)
    {
        return new UpdateMyProfileCommandHandler(
            dbContext,
            otpService,
            emailSender,
            smsSender,
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
        IdentityTestDbContext dbContext)
    {
        return await SeedUserAsync(
            dbContext,
            "mohamed@example.com",
            "+201001234567");
    }

    private static async Task<User> SeedUserAsync(
        IdentityTestDbContext dbContext,
        string email,
        string phoneNumber)
    {
        User user =
            User.Create(
                FullName.Create(
                    "محمد أحمد"),
                FullName.Create(
                    "Mohamed Ahmed"),
                Email.Create(email),
                PhoneNumber.Create(
                    phoneNumber));

        dbContext.Users.Add(user);

        await dbContext.SaveChangesAsync();

        dbContext.ResetSaveChangesCalls();

        return user;
    }

    private static DateTime CreateUtcDateTime()
    {
        return new DateTime(
            2026,
            8,
            20,
            10,
            0,
            0,
            DateTimeKind.Utc);
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
                    5,
                    0,
                    DateTimeKind.Utc);

        public DateTime UtcNow =>
            UtcNowValue;
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

    private sealed record EmailMessage(
        string Email,
        string Code,
        VerificationPurpose Purpose);

    private sealed record SmsMessage(
        string PhoneNumber,
        string Code,
        VerificationPurpose Purpose);
}
