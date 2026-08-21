using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication.Verification;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.Verification;

public sealed class VerifyOtpCommandHandlerTests
{
    [Fact]
    public async Task Handle_ShouldVerifyEmailAndKeepUserPending_WhenPhoneIsUnverified()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext);

        VerificationRequest request =
            await SeedVerificationRequestAsync(
                dbContext,
                user,
                VerificationPurpose.VerifyEmail);

        dbContext.ResetSaveChangesCalls();

        VerifyOtpCommandHandler handler =
            CreateHandler(
                dbContext);

        Result<VerifyOtpResponse> result =
            await handler.Handle(
                new VerifyOtpCommand(
                    request.Id,
                    FakeOtpService.ValidCode),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.True(user.EmailVerified);
        Assert.False(user.PhoneVerified);

        Assert.Equal(
            UserStatus.PendingVerification,
            user.Status);

        Assert.False(
            result.Value.NormalAccessAllowed);

        Assert.Equal(
            VerificationStatus.Verified,
            request.Status);

        Assert.Equal(
            request.MaxAttempts,
            result.Value.AttemptesRemaining);

        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldVerifyPhoneAndActivateUser_WhenEmailIsAlreadyVerified()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext,
                emailVerified: true);

        VerificationRequest request =
            await SeedVerificationRequestAsync(
                dbContext,
                user,
                VerificationPurpose.VerifyPhone);

        dbContext.ResetSaveChangesCalls();

        VerifyOtpCommandHandler handler =
            CreateHandler(
                dbContext);

        Result<VerifyOtpResponse> result =
            await handler.Handle(
                new VerifyOtpCommand(
                    request.Id,
                    FakeOtpService.ValidCode),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.True(user.EmailVerified);
        Assert.True(user.PhoneVerified);

        Assert.Equal(
            UserStatus.Active,
            user.Status);

        Assert.True(
            result.Value.NormalAccessAllowed);

        Assert.Equal(
            VerificationStatus.Verified,
            request.Status);

        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldRegisterFailedAttempt_WhenCodeIsInvalid()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext);

        VerificationRequest request =
            await SeedVerificationRequestAsync(
                dbContext,
                user,
                VerificationPurpose.VerifyEmail);

        dbContext.ResetSaveChangesCalls();

        VerifyOtpCommandHandler handler =
            CreateHandler(
                dbContext);

        Result<VerifyOtpResponse> result =
            await handler.Handle(
                new VerifyOtpCommand(
                    request.Id,
                    "000000"),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            VerifyOtpErrors.InvalidCode,
            result.Error);

        Assert.Equal(1, request.Attempts);

        Assert.Equal(
            VerificationStatus.Pending,
            request.Status);

        Assert.False(user.EmailVerified);

        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldInvalidateRequest_AfterMaximumFailedAttempts()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext);

        VerificationRequest request =
            await SeedVerificationRequestAsync(
                dbContext,
                user,
                VerificationPurpose.VerifyEmail);

        VerifyOtpCommandHandler handler =
            CreateHandler(
                dbContext);

        for (int attempt = 1;
             attempt <=
             VerificationRequest.MaximumAttemptsAllowed;
             attempt++)
        {
            Result<VerifyOtpResponse> result =
                await handler.Handle(
                    new VerifyOtpCommand(
                        request.Id,
                        "000000"),
                    CancellationToken.None);

            Assert.True(result.IsFailure);

            Assert.Equal(
                VerifyOtpErrors.InvalidCode,
                result.Error);
        }

        Assert.Equal(
            VerificationRequest.MaximumAttemptsAllowed,
            request.Attempts);

        Assert.Equal(
            VerificationStatus.Invalidated,
            request.Status);

        Result<VerifyOtpResponse> repeatedResult =
            await handler.Handle(
                new VerifyOtpCommand(
                    request.Id,
                    FakeOtpService.ValidCode),
                CancellationToken.None);

        Assert.Equal(
            VerifyOtpErrors.RequestNotPending,
            repeatedResult.Error);
    }

    [Fact]
    public async Task Handle_ShouldMarkExpiredRequest()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext);

        VerificationRequest request =
            await SeedVerificationRequestAsync(
                dbContext,
                user,
                VerificationPurpose.VerifyEmail,
                isExpired: true);

        dbContext.ResetSaveChangesCalls();

        VerifyOtpCommandHandler handler =
            CreateHandler(
                dbContext);

        Result<VerifyOtpResponse> result =
            await handler.Handle(
                new VerifyOtpCommand(
                    request.Id,
                    FakeOtpService.ValidCode),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            VerifyOtpErrors.RequestExpired,
            result.Error);

        Assert.Equal(
            VerificationStatus.Expired,
            request.Status);

        Assert.Equal(
            1,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFound_ForUnknownRequest()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        VerifyOtpCommandHandler handler =
            CreateHandler(
                dbContext);

        Result<VerifyOtpResponse> result =
            await handler.Handle(
                new VerifyOtpCommand(
                    VerificationRequestId.New(),
                    FakeOtpService.ValidCode),
                CancellationToken.None);

        Assert.Equal(
            VerifyOtpErrors.RequestNotFound,
            result.Error);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldRejectUnsupportedPurpose()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedUserAsync(
                dbContext);

        VerificationRequest request =
            VerificationRequest.Create(
                user.Id,
                user.Email!.Value,
                "otp-hash",
                VerificationChannel.Email,
                VerificationPurpose.ResetPassword,
                FixedDateTimeProvider.UtcNowValue,
                FixedDateTimeProvider.UtcNowValue
                    .AddMinutes(5));

        dbContext.VerificationRequests.Add(
            request);

        await dbContext.SaveChangesAsync();

        dbContext.ResetSaveChangesCalls();

        VerifyOtpCommandHandler handler =
            CreateHandler(
                dbContext);

        Result<VerifyOtpResponse> result =
            await handler.Handle(
                new VerifyOtpCommand(
                    request.Id,
                    FakeOtpService.ValidCode),
                CancellationToken.None);

        Assert.Equal(
            VerifyOtpErrors.UnsupportedPurpose,
            result.Error);

        Assert.Equal(
            VerificationStatus.Pending,
            request.Status);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);
    }

    private static VerifyOtpCommandHandler CreateHandler(
        IIdentityDbContext dbContext)
    {
        return new VerifyOtpCommandHandler(
            dbContext,
            new FakeOtpService(),
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
        IdentityTestDbContext dbContext,
        bool emailVerified = false,
        bool phoneVerified = false)
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
            "password-hash",
            FixedDateTimeProvider.UtcNowValue);

        if (emailVerified)
        {
            user.VerifyEmail(
                FixedDateTimeProvider.UtcNowValue);
        }

        if (phoneVerified)
        {
            user.VerifyPhone(
                FixedDateTimeProvider.UtcNowValue);
        }

        dbContext.Users.Add(user);

        await dbContext.SaveChangesAsync();

        return user;
    }

    private static async Task<VerificationRequest>
        SeedVerificationRequestAsync(
            IdentityTestDbContext dbContext,
            User user,
            VerificationPurpose purpose,
            bool isExpired = false)
    {
        VerificationChannel channel =
            purpose switch
            {
                VerificationPurpose.VerifyEmail =>
                    VerificationChannel.Email,

                VerificationPurpose.VerifyPhone =>
                    VerificationChannel.Sms,

                _ => throw new InvalidOperationException()
            };

        string target =
            channel ==
            VerificationChannel.Email
                ? user.Email!.Value
                : user.PhoneNumber.Value;

        DateTime createdOnUtc =
            isExpired
                ? FixedDateTimeProvider.UtcNowValue
                    .AddMinutes(-10)
                : FixedDateTimeProvider.UtcNowValue;

        DateTime expiresOnUtc =
            isExpired
                ? FixedDateTimeProvider.UtcNowValue
                    .AddMinutes(-5)
                : FixedDateTimeProvider.UtcNowValue
                    .AddMinutes(5);

        VerificationRequest request =
            VerificationRequest.Create(
                user.Id,
                target,
                "otp-hash",
                channel,
                purpose,
                createdOnUtc,
                expiresOnUtc);

        dbContext.VerificationRequests.Add(
            request);

        await dbContext.SaveChangesAsync();

        return request;
    }

    private sealed class FakeOtpService :
        IOtpService
    {
        internal const string ValidCode =
            "123456";

        public GeneratedOtpCode Generate(
            int length)
        {
            return new GeneratedOtpCode(
                ValidCode,
                "otp-hash");
        }

        public bool Verify(
            string providedCode,
            string otpHash)
        {
            return providedCode ==
                       ValidCode &&
                   otpHash ==
                       "otp-hash";
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
}