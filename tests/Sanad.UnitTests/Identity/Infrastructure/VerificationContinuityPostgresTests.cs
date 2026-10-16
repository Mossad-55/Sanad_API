using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication;
using Sanad.Modules.Identity.Application.Authentication.Verification;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Xunit;

namespace Sanad.UnitTests.Identity.Infrastructure;

/// <summary>
/// FUR-02 relational proof against a validated disposable PostgreSQL target.
/// Never resets, seeds broad data, or touches production. The target connection
/// string must be supplied as ConnectionStrings__Fur02VerificationDatabase in
/// the explicit test child process only; the guarded suite leaves it absent
/// and these tests skip. Endpoint and database name are validated before any
/// write; anything else fails closed without touching a database.
/// </summary>
[Collection("Fur02Verification")]
public sealed class VerificationContinuityPostgresTests
{
    private const string ConnectionStringVariable =
        "ConnectionStrings__Fur02VerificationDatabase";

    private const string ExpectedDatabase =
        "sanad_fur02_verify";

    private const string ExpectedMigrationId =
        "20261016133316_AddVerificationRequestVersion";

    private static readonly DateTime FixedUtcNow =
        new(
            2026,
            10,
            16,
            12,
            0,
            0,
            DateTimeKind.Utc);

    [Fur02PostgresFact]
    public async Task Migration_ShouldApplyCleanly_AndContainVerificationVersion()
    {
        string connectionString =
            GetValidatedConnectionString();

        await using IdentityDbContext dbContext =
            CreateDbContext(connectionString);

        await dbContext.Database.MigrateAsync();

        IEnumerable<string> appliedMigrations =
            await dbContext.Database.GetAppliedMigrationsAsync();

        Assert.Contains(
            ExpectedMigrationId,
            appliedMigrations);

        int versionColumns =
            await dbContext.Database.SqlQueryRaw<int>(
                """SELECT COUNT(*)::int AS "Value" FROM "information_schema"."columns" WHERE "table_schema" = 'identity' AND "table_name" = 'verification_requests' AND "column_name" = 'version'""")
                .SingleAsync();

        Assert.Equal(1, versionColumns);
    }

    [Fur02PostgresFact]
    public async Task ConcurrentResend_ShouldPersistExactlyOneReplacement()
    {
        string connectionString =
            GetValidatedConnectionString();

        string runKey =
            Guid.NewGuid().ToString("N")[..8];

        List<VerificationRequestId> requestIdsToDelete = [];
        UserId? userIdToDelete = null;

        try
        {
            await using (IdentityDbContext setupContext =
                CreateDbContext(connectionString))
            {
                await setupContext.Database.MigrateAsync();

                User user = CreateUser(runKey);

                DateTime createdOnUtc =
                    FixedUtcNow.AddMinutes(-2);

                VerificationRequest currentRequest =
                    VerificationRequest.Create(
                        user.Id,
                        user.Email!.Value,
                        "old-otp-hash",
                        VerificationChannel.Email,
                        VerificationPurpose.VerifyEmail,
                        createdOnUtc,
                        createdOnUtc.Add(
                            OtpPolicy.Lifetime));

                setupContext.Users.Add(user);
                setupContext.VerificationRequests.Add(
                    currentRequest);

                await setupContext.SaveChangesAsync();

                userIdToDelete = user.Id;
                requestIdsToDelete.Add(currentRequest.Id);
            }

            using Barrier sendBarrier =
                new(participantCount: 2);

            RecordingBarrierEmailSender senderA =
                new(sendBarrier);

            RecordingBarrierEmailSender senderB =
                new(sendBarrier);

            await using IdentityDbContext contextA =
                CreateDbContext(connectionString);

            await using IdentityDbContext contextB =
                CreateDbContext(connectionString);

            ResendOtpCommandHandler handlerA =
                CreateHandler(
                    contextA,
                    senderA);

            ResendOtpCommandHandler handlerB =
                CreateHandler(
                    contextB,
                    senderB);

            VerificationRequestId requestId =
                await ReadSingleRequestIdAsync(
                    connectionString,
                    userIdToDelete.Value);

            Task<Result<ResendOtpResponse>> callA =
                handlerA.Handle(
                    new ResendOtpCommand(requestId),
                    CancellationToken.None);

            Task<Result<ResendOtpResponse>> callB =
                handlerB.Handle(
                    new ResendOtpCommand(requestId),
                    CancellationToken.None);

            Result<ResendOtpResponse>[] outcomes =
                await Task.WhenAll(callA, callB);

            Result<ResendOtpResponse> winner =
                Assert.Single(outcomes, outcome => outcome.IsSuccess);

            Result<ResendOtpResponse> loser =
                Assert.Single(outcomes, outcome => outcome.IsFailure);

            Assert.Equal(
                ResendOtpErrors.RequestSuperseded,
                loser.Error);

            await using (IdentityDbContext readbackContext =
                CreateDbContext(connectionString))
            {
                VerificationRequest[] requests =
                    await readbackContext.VerificationRequests
                        .Where(request =>
                            request.UserId ==
                                userIdToDelete.Value)
                        .OrderBy(request =>
                            request.CreatedOnUtc)
                        .ToArrayAsync();

                Assert.Equal(2, requests.Length);

                VerificationRequest oldRequest =
                    requests[0];

                VerificationRequest replacement =
                    requests[1];

                Assert.Equal(
                    VerificationStatus.Invalidated,
                    oldRequest.Status);

                Assert.Equal(
                    VerificationStatus.Pending,
                    replacement.Status);

                Assert.Equal(
                    winner.Value.VerificationRequestId,
                    replacement.Id);

                Assert.Equal(
                    winner.Value.ExpiresOnUtc,
                    replacement.ExpiresOnUtc);

                requestIdsToDelete.Add(replacement.Id);
            }

            Assert.Equal(
                2,
                senderA.SentCount + senderB.SentCount);
        }
        finally
        {
            await DeleteFixtureRowsAsync(
                connectionString,
                userIdToDelete,
                requestIdsToDelete);
        }
    }

    [Fur02PostgresFact]
    public async Task Resend_ShouldReplaceAlreadyExpiredRequest_Atomically()
    {
        string connectionString =
            GetValidatedConnectionString();

        string runKey =
            Guid.NewGuid().ToString("N")[..8];

        List<VerificationRequestId> requestIdsToDelete = [];
        UserId? userIdToDelete = null;

        try
        {
            VerificationRequestId expiredRequestId;

            await using (IdentityDbContext setupContext =
                CreateDbContext(connectionString))
            {
                await setupContext.Database.MigrateAsync();

                User user = CreateUser(runKey);

                DateTime createdOnUtc =
                    FixedUtcNow.AddMinutes(-12);

                VerificationRequest expiredRequest =
                    VerificationRequest.Create(
                        user.Id,
                        user.Email!.Value,
                        "old-otp-hash",
                        VerificationChannel.Email,
                        VerificationPurpose.VerifyEmail,
                        createdOnUtc,
                        createdOnUtc.Add(
                            OtpPolicy.Lifetime));

                expiredRequest.MarkExpired(FixedUtcNow);

                setupContext.Users.Add(user);
                setupContext.VerificationRequests.Add(
                    expiredRequest);

                await setupContext.SaveChangesAsync();

                userIdToDelete = user.Id;
                expiredRequestId = expiredRequest.Id;
                requestIdsToDelete.Add(expiredRequest.Id);
            }

            Result<ResendOtpResponse> result;

            await using (IdentityDbContext handlerContext =
                CreateDbContext(connectionString))
            {
                RecordingEmailSender sender =
                    new();

                ResendOtpCommandHandler handler =
                    CreateHandler(
                        handlerContext,
                        sender);

                result = await handler.Handle(
                    new ResendOtpCommand(expiredRequestId),
                    CancellationToken.None);

                Assert.True(result.IsSuccess);
                Assert.Single(sender.Messages);
            }

            await using (IdentityDbContext readbackContext =
                CreateDbContext(connectionString))
            {
                VerificationRequest[] requests =
                    await readbackContext.VerificationRequests
                        .Where(request =>
                            request.UserId ==
                                userIdToDelete.Value)
                        .OrderBy(request =>
                            request.CreatedOnUtc)
                        .ToArrayAsync();

                Assert.Equal(2, requests.Length);

                Assert.Equal(
                    VerificationStatus.Invalidated,
                    requests[0].Status);

                Assert.Equal(
                    VerificationStatus.Pending,
                    requests[1].Status);

                Assert.Equal(
                    result.Value.VerificationRequestId,
                    requests[1].Id);

                Assert.Equal(
                    FixedUtcNow.Add(OtpPolicy.Lifetime),
                    requests[1].ExpiresOnUtc);

                requestIdsToDelete.Add(requests[1].Id);
            }
        }
        finally
        {
            await DeleteFixtureRowsAsync(
                connectionString,
                userIdToDelete,
                requestIdsToDelete);
        }
    }

    [Fur02PostgresFact]
    public async Task Supersession_ShouldPreferHigherId_WhenCreatedTimesTie()
    {
        string connectionString =
            GetValidatedConnectionString();

        string runKey =
            Guid.NewGuid().ToString("N")[..8];

        List<VerificationRequestId> requestIdsToDelete = [];
        UserId? userIdToDelete = null;

        try
        {
            VerificationRequest firstRequest;
            VerificationRequest secondRequest;

            await using (IdentityDbContext setupContext =
                CreateDbContext(connectionString))
            {
                await setupContext.Database.MigrateAsync();

                User user = CreateUser(runKey);

                DateTime createdOnUtc =
                    FixedUtcNow.AddMinutes(-2);

                firstRequest = VerificationRequest.Create(
                    user.Id,
                    user.Email!.Value,
                    "old-otp-hash",
                    VerificationChannel.Email,
                    VerificationPurpose.VerifyEmail,
                    createdOnUtc,
                    createdOnUtc.Add(
                        OtpPolicy.Lifetime));

                secondRequest = VerificationRequest.Create(
                    user.Id,
                    user.Email!.Value,
                    "old-otp-hash",
                    VerificationChannel.Email,
                    VerificationPurpose.VerifyEmail,
                    createdOnUtc,
                    createdOnUtc.Add(
                        OtpPolicy.Lifetime));

                Assert.NotEqual(
                    firstRequest.Id,
                    secondRequest.Id);

                setupContext.Users.Add(user);
                setupContext.VerificationRequests.AddRange(
                    firstRequest,
                    secondRequest);

                await setupContext.SaveChangesAsync();

                userIdToDelete = user.Id;
                requestIdsToDelete.Add(firstRequest.Id);
                requestIdsToDelete.Add(secondRequest.Id);
            }

            VerificationRequest olderById =
                firstRequest.Id.Value.CompareTo(
                    secondRequest.Id.Value) < 0
                    ? firstRequest
                    : secondRequest;

            Result<ResendOtpResponse> result;

            await using (IdentityDbContext handlerContext =
                CreateDbContext(connectionString))
            {
                ResendOtpCommandHandler handler =
                    CreateHandler(
                        handlerContext,
                        new RecordingEmailSender());

                result = await handler.Handle(
                    new ResendOtpCommand(olderById.Id),
                    CancellationToken.None);
            }

            Assert.True(result.IsFailure);

            Assert.Equal(
                ResendOtpErrors.RequestSuperseded,
                result.Error);
        }
        finally
        {
            await DeleteFixtureRowsAsync(
                connectionString,
                userIdToDelete,
                requestIdsToDelete);
        }
    }

    private static string GetValidatedConnectionString()
    {
        string? raw =
            Environment.GetEnvironmentVariable(
                ConnectionStringVariable);

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException(
                $"Test requires {ConnectionStringVariable}.");
        }

        var builder = new NpgsqlConnectionStringBuilder(raw);

        if (builder.Host is not (
            "localhost" or "127.0.0.1" or "::1"))
        {
            throw new InvalidOperationException(
                "Refusing to write: the FUR-02 verification target " +
                "must be a local disposable PostgreSQL instance.");
        }

        if (!string.Equals(
            builder.Database,
            ExpectedDatabase,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refusing to write: the FUR-02 verification target " +
                $"must be database '{ExpectedDatabase}'.");
        }

        return builder.ConnectionString;
    }

    private static IdentityDbContext CreateDbContext(
        string connectionString)
    {
        DbContextOptions<IdentityDbContext> options =
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(
                    connectionString,
                    npgsqlOptions =>
                        npgsqlOptions.MigrationsHistoryTable(
                            "__EFMigrationsHistory",
                            IdentityDbContext.Schema))
                .Options;

        return new IdentityDbContext(options);
    }

    private static async Task<VerificationRequestId> ReadSingleRequestIdAsync(
        string connectionString,
        UserId userId)
    {
        await using IdentityDbContext readContext =
            CreateDbContext(connectionString);

        VerificationRequest request =
            await readContext.VerificationRequests.SingleAsync(
                item => item.UserId == userId);

        return request.Id;
    }

    private static User CreateUser(string runKey)
    {
        int phoneSuffix =
            Math.Abs(runKey.GetHashCode()) % 900000 + 100000;

        User user =
            User.Create(
                FullName.Create("مستخدم تحقق"),
                FullName.Create("Verification User"),
                Email.Create($"fur02.{runKey}@example.test"),
                PhoneNumber.Create($"+20101{phoneSuffix:000000}"));

        user.AddAccount(AccountType.Family);

        return user;
    }

    private static ResendOtpCommandHandler CreateHandler(
        IdentityDbContext dbContext,
        IEmailSender emailSender)
    {
        return new ResendOtpCommandHandler(
            dbContext,
            new FixedOtpService(),
            emailSender,
            new RecordingSmsSender(),
            new FixedDateTimeProvider());
    }

    private static async Task DeleteFixtureRowsAsync(
        string connectionString,
        UserId? userId,
        List<VerificationRequestId> requestIds)
    {
        await using IdentityDbContext cleanupContext =
            CreateDbContext(connectionString);

        if (requestIds.Count > 0)
        {
            await cleanupContext.VerificationRequests
                .Where(request =>
                    requestIds.Contains(request.Id))
                .ExecuteDeleteAsync();
        }

        if (userId.HasValue)
        {
            await cleanupContext.Users
                .Where(user => user.Id == userId.Value)
                .ExecuteDeleteAsync();
        }
    }

    private sealed class Fur02PostgresFactAttribute :
        FactAttribute
    {
        public Fur02PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(
                    ConnectionStringVariable)))
            {
                Skip =
                    "FUR-02 PostgreSQL proof is skipped. Set " +
                    "ConnectionStrings__Fur02VerificationDatabase " +
                    "to a validated disposable target to run it.";
            }
        }
    }

    private sealed class FixedDateTimeProvider :
        IDateTimeProvider
    {
        public DateTime UtcNow => FixedUtcNow;
    }

    private sealed class FixedOtpService :
        IOtpService
    {
        public GeneratedOtpCode Generate(int length)
        {
            return new GeneratedOtpCode(
                "654321",
                "new-otp-hash");
        }

        public bool Verify(
            string providedCode,
            string otpHash)
        {
            return false;
        }
    }

    private sealed class RecordingEmailSender :
        IEmailSender
    {
        internal List<string> Messages { get; } = [];

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
            Messages.Add(email);

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

    private sealed class RecordingBarrierEmailSender :
        IEmailSender
    {
        private readonly Barrier _barrier;

        internal int SentCount { get; private set; }

        internal RecordingBarrierEmailSender(
            Barrier barrier)
        {
            _barrier = barrier;
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
            _barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            SentCount++;

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
        public Task SendVerificationCodeAsync(
            string phoneNumber,
            string code,
            VerificationPurpose purpose,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}

[CollectionDefinition("Fur02Verification", DisableParallelization = true)]
public sealed class Fur02VerificationCollection
{
}
