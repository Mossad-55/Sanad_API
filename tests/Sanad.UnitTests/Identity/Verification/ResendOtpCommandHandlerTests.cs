using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication;
using Sanad.Modules.Identity.Application.Authentication.Verification;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.Verification;

public sealed class ResendOtpCommandHandlerTests
{
    [Theory]
    [InlineData(
        VerificationChannel.Email,
        VerificationPurpose.VerifyEmail,
        "user@example.com")]
    [InlineData(
        VerificationChannel.Sms,
        VerificationPurpose.VerifyPhone,
        "+201001234567")]
    public async Task Handle_ShouldInvalidateCurrentRequestAndCreateReplacement(
        VerificationChannel channel,
        VerificationPurpose purpose,
        string target)
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        VerificationRequest currentRequest =
            await SeedRequestAsync(
                dbContext,
                channel,
                purpose,
                target,
                createdOnUtc:
                    FixedDateTimeProvider.UtcNowValue
                        .AddMinutes(-2));

        dbContext.ResetSaveChangesCalls();

        FakeOtpService otpService =
            new();

        RecordingEmailSender emailSender =
            new();

        RecordingSmsSender smsSender =
            new();

        ResendOtpCommandHandler handler =
            CreateHandler(
                dbContext,
                otpService,
                emailSender,
                smsSender);

        Result<ResendOtpResponse> result =
            await handler.Handle(
                new ResendOtpCommand(
                    currentRequest.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            VerificationStatus.Invalidated,
            currentRequest.Status);

        VerificationRequest[] requests =
            await dbContext
                .VerificationRequests
                .OrderBy(request =>
                    request.CreatedOnUtc)
                .ToArrayAsync();

        Assert.Equal(2, requests.Length);

        VerificationRequest replacement =
            requests.Single(
                request =>
                    request.Id ==
                    result.Value
                        .VerificationRequestId);

        Assert.Equal(
            VerificationStatus.Pending,
            replacement.Status);

        Assert.Equal(
            currentRequest.UserId,
            replacement.UserId);

        Assert.Equal(
            currentRequest.Target,
            replacement.Target);

        Assert.Equal(
            currentRequest.Channel,
            replacement.Channel);

        Assert.Equal(
            currentRequest.Purpose,
            replacement.Purpose);

        Assert.Equal(
            "new-otp-hash",
            replacement.OtpHash);

        Assert.Equal(
            FixedDateTimeProvider.UtcNowValue,
            replacement.CreatedOnUtc);

        Assert.Equal(
            FixedDateTimeProvider.UtcNowValue
                .Add(OtpPolicy.Lifetime),
            replacement.ExpiresOnUtc);

        Assert.Equal(
            replacement.ExpiresOnUtc,
            result.Value.ExpiresOnUtc);

        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);

        Assert.Equal(
            [OtpPolicy.CodeLength],
            otpService.RequestedLengths);

        if (channel ==
            VerificationChannel.Email)
        {
            EmailMessage message =
                Assert.Single(
                    emailSender.Messages);

            Assert.Equal(
                target,
                message.Email);

            Assert.Equal(
                "654321",
                message.Code);

            Assert.Equal(
                purpose,
                message.Purpose);

            Assert.Empty(
                smsSender.Messages);
        }
        else
        {
            SmsMessage message =
                Assert.Single(
                    smsSender.Messages);

            Assert.Equal(
                target,
                message.PhoneNumber);

            Assert.Equal(
                "654321",
                message.Code);

            Assert.Equal(
                purpose,
                message.Purpose);

            Assert.Empty(
                emailSender.Messages);
        }
    }

    [Fact]
    public async Task Handle_ShouldRejectCooldownBeforeSixtySeconds()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        VerificationRequest currentRequest =
            await SeedRequestAsync(
                dbContext,
                VerificationChannel.Email,
                VerificationPurpose.VerifyEmail,
                "user@example.com",
                createdOnUtc:
                    FixedDateTimeProvider.UtcNowValue
                        .AddSeconds(-30));

        dbContext.ResetSaveChangesCalls();

        RecordingEmailSender emailSender =
            new();

        ResendOtpCommandHandler handler =
            CreateHandler(
                dbContext,
                new FakeOtpService(),
                emailSender,
                new RecordingSmsSender());

        Result<ResendOtpResponse> result =
            await handler.Handle(
                new ResendOtpCommand(
                    currentRequest.Id),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            ResendOtpErrors.CooldownActive,
            result.Error);

        Assert.Equal(
            VerificationStatus.Pending,
            currentRequest.Status);

        Assert.Single(
            dbContext.VerificationRequests);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);

        Assert.Empty(emailSender.Messages);
    }

    [Fact]
    public async Task Handle_ShouldAllowResendAtExactCooldownBoundary()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        VerificationRequest currentRequest =
            await SeedRequestAsync(
                dbContext,
                VerificationChannel.Email,
                VerificationPurpose.VerifyEmail,
                "user@example.com",
                createdOnUtc:
                    FixedDateTimeProvider.UtcNowValue -
                    OtpPolicy.ResendCooldown);

        dbContext.ResetSaveChangesCalls();

        ResendOtpCommandHandler handler =
            CreateHandler(
                dbContext,
                new FakeOtpService(),
                new RecordingEmailSender(),
                new RecordingSmsSender());

        Result<ResendOtpResponse> result =
            await handler.Handle(
                new ResendOtpCommand(
                    currentRequest.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            VerificationStatus.Invalidated,
            currentRequest.Status);
    }

    [Fact]
    public async Task Handle_ShouldRejectSupersededRequest()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        VerificationRequest olderRequest =
            await SeedRequestAsync(
                dbContext,
                VerificationChannel.Email,
                VerificationPurpose.VerifyEmail,
                "user@example.com",
                createdOnUtc:
                    FixedDateTimeProvider.UtcNowValue
                        .AddMinutes(-3));

        await SeedRequestAsync(
            dbContext,
            VerificationChannel.Email,
            VerificationPurpose.VerifyEmail,
            "user@example.com",
            createdOnUtc:
                FixedDateTimeProvider.UtcNowValue
                    .AddMinutes(-2));

        dbContext.ResetSaveChangesCalls();

        ResendOtpCommandHandler handler =
            CreateHandler(
                dbContext,
                new FakeOtpService(),
                new RecordingEmailSender(),
                new RecordingSmsSender());

        Result<ResendOtpResponse> result =
            await handler.Handle(
                new ResendOtpCommand(
                    olderRequest.Id),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            ResendOtpErrors.RequestSuperseded,
            result.Error);

        Assert.Equal(
            VerificationStatus.Pending,
            olderRequest.Status);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldRejectNonPendingRequest()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        VerificationRequest currentRequest =
            await SeedRequestAsync(
                dbContext,
                VerificationChannel.Email,
                VerificationPurpose.VerifyEmail,
                "user@example.com",
                createdOnUtc:
                    FixedDateTimeProvider.UtcNowValue
                        .AddMinutes(-2));

        currentRequest.Invalidate(
            FixedDateTimeProvider.UtcNowValue
                .AddMinutes(-1));

        await dbContext.SaveChangesAsync();

        dbContext.ResetSaveChangesCalls();

        ResendOtpCommandHandler handler =
            CreateHandler(
                dbContext,
                new FakeOtpService(),
                new RecordingEmailSender(),
                new RecordingSmsSender());

        Result<ResendOtpResponse> result =
            await handler.Handle(
                new ResendOtpCommand(
                    currentRequest.Id),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            ResendOtpErrors.RequestNotPending,
            result.Error);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFoundForUnknownRequest()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        ResendOtpCommandHandler handler =
            CreateHandler(
                dbContext,
                new FakeOtpService(),
                new RecordingEmailSender(),
                new RecordingSmsSender());

        Result<ResendOtpResponse> result =
            await handler.Handle(
                new ResendOtpCommand(
                    VerificationRequestId.New()),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            ResendOtpErrors.RequestNotFound,
            result.Error);
    }

    private static ResendOtpCommandHandler CreateHandler(
        IdentityTestDbContext dbContext,
        IOtpService otpService,
        IEmailSender emailSender,
        ISmsSender smsSender)
    {
        return new ResendOtpCommandHandler(
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

    private static async Task<VerificationRequest>
        SeedRequestAsync(
            IdentityTestDbContext dbContext,
            VerificationChannel channel,
            VerificationPurpose purpose,
            string target,
            DateTime createdOnUtc)
    {
        VerificationRequest request =
            VerificationRequest.Create(
                UserId.New(),
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

    private sealed class FakeOtpService :
        IOtpService
    {
        internal List<int> RequestedLengths
        {
            get;
        } = [];

        public GeneratedOtpCode Generate(
            int length)
        {
            RequestedLengths.Add(length);

            return new GeneratedOtpCode(
                "654321",
                "new-otp-hash");
        }

        public bool Verify(
            string providedCode,
            string otpHash)
        {
            return providedCode ==
                       "654321" &&
                   otpHash ==
                       "new-otp-hash";
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