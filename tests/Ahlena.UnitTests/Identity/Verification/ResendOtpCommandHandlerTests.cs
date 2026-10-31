using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Application.Abstractions.Messaging;
using Ahlena.Modules.Identity.Application.Abstractions.Security;
using Ahlena.Modules.Identity.Application.Authentication;
using Ahlena.Modules.Identity.Application.Authentication.Verification;
using Ahlena.Modules.Identity.Domain.Authentication.VerificationRequests;
using Ahlena.UnitTests.Identity.Registration;

namespace Ahlena.UnitTests.Identity.Verification;

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

        UserId userId = UserId.New();

        VerificationRequest olderRequest =
            await SeedRequestAsync(
                dbContext,
                VerificationChannel.Email,
                VerificationPurpose.VerifyEmail,
                "user@example.com",
                createdOnUtc:
                    FixedDateTimeProvider.UtcNowValue
                        .AddMinutes(-3),
                userId: userId);

        await SeedRequestAsync(
            dbContext,
            VerificationChannel.Email,
            VerificationPurpose.VerifyEmail,
            "user@example.com",
            createdOnUtc:
                FixedDateTimeProvider.UtcNowValue
                        .AddMinutes(-2),
                userId: userId);

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

    [Fact]
    public async Task Handle_ShouldAcceptExpiredRequestAndReplaceIt()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        VerificationRequest expiredRequest =
            await SeedRequestAsync(
                dbContext,
                VerificationChannel.Sms,
                VerificationPurpose.VerifyPhone,
                "+201001234567",
                createdOnUtc:
                    FixedDateTimeProvider.UtcNowValue
                        .AddMinutes(-10));

        expiredRequest.MarkExpired(
            FixedDateTimeProvider.UtcNowValue);

        await dbContext.SaveChangesAsync();

        dbContext.ResetSaveChangesCalls();

        FakeOtpService otpService =
            new();

        RecordingSmsSender smsSender =
            new();

        ResendOtpCommandHandler handler =
            CreateHandler(
                dbContext,
                otpService,
                new RecordingEmailSender(),
                smsSender);

        Result<ResendOtpResponse> result =
            await handler.Handle(
                new ResendOtpCommand(
                    expiredRequest.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            VerificationStatus.Invalidated,
            expiredRequest.Status);

        Assert.Equal(3, expiredRequest.Version);

        VerificationRequest[] requests =
            await dbContext
                .VerificationRequests
                .ToArrayAsync();

        Assert.Equal(2, requests.Length);

        VerificationRequest replacement =
            Assert.Single(
                requests,
                request =>
                    request.Id ==
                    result.Value
                        .VerificationRequestId);

        Assert.Equal(
            VerificationStatus.Pending,
            replacement.Status);

        Assert.Equal(1, replacement.Version);

        Assert.Equal(
            FixedDateTimeProvider.UtcNowValue
                .Add(OtpPolicy.Lifetime),
            result.Value.ExpiresOnUtc);

        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);

        SmsMessage message =
            Assert.Single(
                smsSender.Messages);

        Assert.Equal(
            "654321",
            message.Code);
    }

    [Theory]
    [InlineData(VerificationStatus.Verified)]
    [InlineData(VerificationStatus.Invalidated)]
    public async Task Handle_ShouldRejectVerifiedAndInvalidatedRequests(
        VerificationStatus terminalStatus)
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

        if (terminalStatus ==
            VerificationStatus.Verified)
        {
            currentRequest.Verify(
                FixedDateTimeProvider.UtcNowValue);
        }
        else
        {
            currentRequest.Invalidate(
                FixedDateTimeProvider.UtcNowValue
                    .AddMinutes(-1));
        }

        await dbContext.SaveChangesAsync();

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
            ResendOtpErrors.RequestNotPending,
            result.Error);

        Assert.Equal(
            terminalStatus,
            currentRequest.Status);

        Assert.Single(
            dbContext.VerificationRequests);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);

        Assert.Empty(emailSender.Messages);
    }

    [Fact]
    public async Task Handle_ShouldReturnProviderDeliveryFailedAndPreserveState_WhenSenderThrows()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        VerificationRequest currentRequest =
            await SeedRequestAsync(
                dbContext,
                VerificationChannel.Sms,
                VerificationPurpose.VerifyPhone,
                "+201001234567",
                createdOnUtc:
                    FixedDateTimeProvider.UtcNowValue
                        .AddMinutes(-2));

        dbContext.ResetSaveChangesCalls();

        FakeOtpService otpService =
            new();

        ResendOtpCommandHandler handler =
            CreateHandler(
                dbContext,
                otpService,
                new RecordingEmailSender(),
                new ThrowingSmsSender(
                    new InvalidOperationException(
                        "SMS provider OTP delivery failed.")));

        Result<ResendOtpResponse> result =
            await handler.Handle(
                new ResendOtpCommand(
                    currentRequest.Id),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            ResendOtpErrors.ProviderDeliveryFailed,
            result.Error);

        Assert.Equal(
            VerificationStatus.Pending,
            currentRequest.Status);

        Assert.Equal(1, currentRequest.Version);

        Assert.Single(
            dbContext.VerificationRequests);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);

        Assert.Equal(
            [OtpPolicy.CodeLength],
            otpService.RequestedLengths);
    }

    [Fact]
    public async Task Handle_ShouldReturnProviderDeliveryFailedAndPreserveState_WhenSenderTimesOut()
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

        dbContext.ResetSaveChangesCalls();

        ResendOtpCommandHandler handler =
            CreateHandler(
                dbContext,
                new FakeOtpService(),
                new ThrowingEmailSender(
                    new TimeoutException(
                        "Email provider timed out.")),
                new RecordingSmsSender());

        Result<ResendOtpResponse> result =
            await handler.Handle(
                new ResendOtpCommand(
                    currentRequest.Id),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            ResendOtpErrors.ProviderDeliveryFailed,
            result.Error);

        Assert.Equal(
            VerificationStatus.Pending,
            currentRequest.Status);

        Assert.Single(
            dbContext.VerificationRequests);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldReturnProviderDeliveryFailedAndPreserveState_WhenProviderCancelsWithLiveToken()
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

        dbContext.ResetSaveChangesCalls();

        ResendOtpCommandHandler handler =
            CreateHandler(
                dbContext,
                new FakeOtpService(),
                new ThrowingEmailSender(
                    new TaskCanceledException(
                        "Provider HTTP request timed out.")),
                new RecordingSmsSender());

        Result<ResendOtpResponse> result =
            await handler.Handle(
                new ResendOtpCommand(
                    currentRequest.Id),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            ResendOtpErrors.ProviderDeliveryFailed,
            result.Error);

        Assert.Equal(
            VerificationStatus.Pending,
            currentRequest.Status);

        Assert.Single(
            dbContext.VerificationRequests);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldPropagateCancellationAndPreserveState_WhenRequestTokenIsCanceled()
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

        dbContext.ResetSaveChangesCalls();

        ResendOtpCommandHandler handler =
            CreateHandler(
                dbContext,
                new FakeOtpService(),
                new ThrowingEmailSender(
                    new OperationCanceledException()),
                new RecordingSmsSender());

        using CancellationTokenSource canceledSource =
            new();

        canceledSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => handler.Handle(
                new ResendOtpCommand(
                    currentRequest.Id),
                canceledSource.Token));

        Assert.Equal(
            VerificationStatus.Pending,
            currentRequest.Status);

        Assert.Single(
            dbContext.VerificationRequests);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldPropagateCancellationAndPreserveState_WhenRequestTokenCanceledDuringSend()
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

        dbContext.ResetSaveChangesCalls();

        using CancellationTokenSource requestSource =
            new();

        CancelDuringSendEmailSender emailSender =
            new(requestSource);

        ResendOtpCommandHandler handler =
            CreateHandler(
                dbContext,
                new FakeOtpService(),
                emailSender,
                new RecordingSmsSender());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => handler.Handle(
                new ResendOtpCommand(
                    currentRequest.Id),
                requestSource.Token));

        Assert.True(
            emailSender.WasEntered,
            "The sender must be entered before cancellation.");

        Assert.True(
            requestSource.IsCancellationRequested);

        Assert.Equal(
            VerificationStatus.Pending,
            currentRequest.Status);

        Assert.Single(
            dbContext.VerificationRequests);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldAllowRetryWithSameRequestId_AfterProviderFailure()
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

        dbContext.ResetSaveChangesCalls();

        ResendOtpCommandHandler failingHandler =
            CreateHandler(
                dbContext,
                new FakeOtpService(),
                new ThrowingEmailSender(
                    new InvalidOperationException(
                        "Email provider rejected the message.")),
                new RecordingSmsSender());

        Result<ResendOtpResponse> failedResult =
            await failingHandler.Handle(
                new ResendOtpCommand(
                    currentRequest.Id),
                CancellationToken.None);

        Assert.True(failedResult.IsFailure);

        RecordingEmailSender emailSender =
            new();

        ResendOtpCommandHandler retryHandler =
            CreateHandler(
                dbContext,
                new FakeOtpService(),
                emailSender,
                new RecordingSmsSender());

        Result<ResendOtpResponse> retryResult =
            await retryHandler.Handle(
                new ResendOtpCommand(
                    currentRequest.Id),
                CancellationToken.None);

        Assert.True(retryResult.IsSuccess);

        Assert.Equal(
            VerificationStatus.Invalidated,
            currentRequest.Status);

        Assert.Equal(2, await dbContext
            .VerificationRequests
            .CountAsync());

        EmailMessage message =
            Assert.Single(
                emailSender.Messages);

        Assert.Equal(
            "654321",
            message.Code);
    }

    [Fact]
    public async Task Handle_ShouldNotSupersede_WhenNewerRequestBelongsToAnotherUser()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        VerificationRequest ownRequest =
            await SeedRequestAsync(
                dbContext,
                VerificationChannel.Email,
                VerificationPurpose.VerifyEmail,
                "user@example.com",
                createdOnUtc:
                    FixedDateTimeProvider.UtcNowValue
                        .AddMinutes(-3),
                userId: UserId.New());

        await SeedRequestAsync(
            dbContext,
            VerificationChannel.Email,
            VerificationPurpose.VerifyEmail,
            "user@example.com",
            createdOnUtc:
                FixedDateTimeProvider.UtcNowValue
                    .AddMinutes(-2),
                userId: UserId.New());

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
                    ownRequest.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            VerificationStatus.Invalidated,
            ownRequest.Status);
    }

    [Fact]
    public async Task Handle_ShouldRejectOlderRequest_WhenLatestIsExpired()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        UserId userId = UserId.New();

        VerificationRequest olderRequest =
            await SeedRequestAsync(
                dbContext,
                VerificationChannel.Email,
                VerificationPurpose.VerifyEmail,
                "user@example.com",
                createdOnUtc:
                    FixedDateTimeProvider.UtcNowValue
                        .AddMinutes(-12),
                userId: userId);

        VerificationRequest latestRequest =
            await SeedRequestAsync(
                dbContext,
                VerificationChannel.Email,
                VerificationPurpose.VerifyEmail,
                "user@example.com",
                createdOnUtc:
                    FixedDateTimeProvider.UtcNowValue
                        .AddMinutes(-8),
                userId: userId);

        latestRequest.MarkExpired(
            FixedDateTimeProvider.UtcNowValue);

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
                    olderRequest.Id),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            ResendOtpErrors.RequestSuperseded,
            result.Error);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldRejectResend_AfterMaxAttemptsInvalidatedRequest()
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

        for (int attempt = 0;
             attempt < VerificationRequest.MaximumAttemptsAllowed;
             attempt++)
        {
            currentRequest.RegisterFailedAttempt(
                FixedDateTimeProvider.UtcNowValue);
        }

        Assert.Equal(
            VerificationStatus.Invalidated,
            currentRequest.Status);

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
            DateTime createdOnUtc,
            UserId? userId = null)
    {
        VerificationRequest request =
            VerificationRequest.Create(
                userId ?? UserId.New(),
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
        private readonly Exception _exception;

        internal ThrowingEmailSender(
            Exception exception)
        {
            _exception = exception;
        }

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
            throw _exception;
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

    private sealed class CancelDuringSendEmailSender :
        IEmailSender
    {
        private readonly CancellationTokenSource _requestSource;

        internal bool WasEntered { get; private set; }

        internal CancelDuringSendEmailSender(
            CancellationTokenSource requestSource)
        {
            _requestSource = requestSource;
        }

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
            WasEntered = true;

            _requestSource.Cancel();

            cancellationToken.ThrowIfCancellationRequested();

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

    private sealed class ThrowingSmsSender :
        ISmsSender
    {
        private readonly Exception _exception;

        internal ThrowingSmsSender(
            Exception exception)
        {
            _exception = exception;
        }

        public Task SendVerificationCodeAsync(
            string phoneNumber,
            string code,
            VerificationPurpose purpose,
            CancellationToken cancellationToken)
        {
            throw _exception;
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
