using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Authentication.SocialLogin;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;
using Sanad.Modules.Identity.Infrastructure.Challenges;
using Sanad.Modules.Identity.Infrastructure.Persistence;

namespace Sanad.UnitTests.Identity.Infrastructure;

[Collection("LocalPostgres")]
public sealed class PostgresSocialAuthenticationChallengeStoreTests
{
    private readonly LocalPostgresIdentityFixture
        _fixture;

    public PostgresSocialAuthenticationChallengeStoreTests(
        LocalPostgresIdentityFixture fixture)
    {
        _fixture =
            fixture;
    }

    [LocalPostgresFact]
    public async Task CreateAsync_ShouldStageHashUntilCallerSaves()
    {
        await ResetDatabaseAsync();

        var store =
            new PostgresSocialAuthenticationChallengeStore(
                _fixture.DbContext);

        SocialAuthenticationChallenge challenge =
            CreateChallenge(
                FixedUtcNow.AddMinutes(10));

        string opaqueChallenge =
            await store.CreateAsync(
                challenge,
                CancellationToken.None);

        Assert.False(
            string.IsNullOrWhiteSpace(
                opaqueChallenge));

        long countBeforeSave =
            await GetChallengeCountAsync();

        Assert.Equal(
            0,
            countBeforeSave);

        await _fixture.DbContext
            .SaveChangesAsync();

        string storedHash =
            await GetSingleStringAsync(
                """
                SELECT challenge_hash
                FROM identity.social_authentication_challenges;
                """);

        Assert.NotEqual(
            opaqueChallenge,
            storedHash);

        Assert.Equal(
            64,
            storedHash.Length);
    }

    [LocalPostgresFact]
    public async Task StageConsumeAsync_ShouldReturnStoredChallengeAndAllowOnlyOneConsume()
    {
        await ResetDatabaseAsync();

        var store =
            new PostgresSocialAuthenticationChallengeStore(
                _fixture.DbContext);

        UserId existingUserId =
            UserId.New();

        VerificationRequestId requestId =
            VerificationRequestId.New();

        SocialAuthenticationChallenge challenge =
            new(
                ExternalLoginProvider.Google,
                "google-subject",
                "user@example.com",
                existingUserId,
                requestId,
                FixedUtcNow.AddMinutes(10));

        string opaqueChallenge =
            await store.CreateAsync(
                challenge,
                CancellationToken.None);

        await _fixture.DbContext
            .SaveChangesAsync();

        SocialAuthenticationChallenge? activeChallenge =
            await store.GetActiveAsync(
                opaqueChallenge,
                FixedUtcNow,
                CancellationToken.None);

        Assert.NotNull(
            activeChallenge);

        Assert.Equal(
            ExternalLoginProvider.Google,
            activeChallenge.Provider);

        Assert.Equal(
            "google-subject",
            activeChallenge.ProviderSubject);

        Assert.Equal(
            "user@example.com",
            activeChallenge.VerifiedEmail);

        Assert.Equal(
            existingUserId,
            activeChallenge.ExistingUserId);

        Assert.Equal(
            requestId,
            activeChallenge.LinkVerificationRequestId);

        bool consumptionStaged =
            await store.StageConsumeAsync(
                opaqueChallenge,
                FixedUtcNow,
                CancellationToken.None);

        Assert.True(
            consumptionStaged);

        await _fixture.DbContext
            .SaveChangesAsync();

        SocialAuthenticationChallenge? consumedChallenge =
            await store.GetActiveAsync(
                opaqueChallenge,
                FixedUtcNow,
                CancellationToken.None);

        Assert.Null(
            consumedChallenge);

        bool secondConsumptionStaged =
            await store.StageConsumeAsync(
                opaqueChallenge,
                FixedUtcNow,
                CancellationToken.None);

        Assert.False(
            secondConsumptionStaged);
    }

    [LocalPostgresFact]
    public async Task GetActiveAsync_ShouldReturnNull_ForWrongOrExpiredChallenge()
    {
        await ResetDatabaseAsync();

        var store =
            new PostgresSocialAuthenticationChallengeStore(
                _fixture.DbContext);

        string validOpaqueChallenge =
            await store.CreateAsync(
                CreateChallenge(
                    FixedUtcNow.AddMinutes(10)),
                CancellationToken.None);

        string expiredOpaqueChallenge =
            await store.CreateAsync(
                CreateChallenge(
                    FixedUtcNow),
                CancellationToken.None);

        await _fixture.DbContext
            .SaveChangesAsync();

        SocialAuthenticationChallenge? wrong =
            await store.GetActiveAsync(
                "wrong-opaque-challenge",
                FixedUtcNow,
                CancellationToken.None);

        SocialAuthenticationChallenge? expired =
            await store.GetActiveAsync(
                expiredOpaqueChallenge,
                FixedUtcNow,
                CancellationToken.None);

        SocialAuthenticationChallenge? valid =
            await store.GetActiveAsync(
                validOpaqueChallenge,
                FixedUtcNow,
                CancellationToken.None);

        Assert.Null(
            wrong);

        Assert.Null(
            expired);

        Assert.NotNull(
            valid);
    }

    [LocalPostgresFact]
    public async Task StageConsumeAsync_ShouldAllowExactlyOneConcurrentSave()
    {
        await ResetDatabaseAsync();

        var creationStore =
            new PostgresSocialAuthenticationChallengeStore(
                _fixture.DbContext);

        string opaqueChallenge =
            await creationStore.CreateAsync(
                CreateChallenge(
                    FixedUtcNow.AddMinutes(10)),
                CancellationToken.None);

        await _fixture.DbContext
            .SaveChangesAsync();

        string connectionString =
            _fixture.ConnectionString;

        await using IdentityDbContext firstContext =
            CreateContext(
                connectionString);

        await using IdentityDbContext secondContext =
            CreateContext(
                connectionString);

        var firstStore =
            new PostgresSocialAuthenticationChallengeStore(
                firstContext);

        var secondStore =
            new PostgresSocialAuthenticationChallengeStore(
                secondContext);

        SocialAuthenticationChallenge? firstChallenge =
            await firstStore.GetActiveAsync(
                opaqueChallenge,
                FixedUtcNow,
                CancellationToken.None);

        SocialAuthenticationChallenge? secondChallenge =
            await secondStore.GetActiveAsync(
                opaqueChallenge,
                FixedUtcNow,
                CancellationToken.None);

        Assert.NotNull(
            firstChallenge);

        Assert.NotNull(
            secondChallenge);

        bool firstConsumptionStaged =
            await firstStore.StageConsumeAsync(
                opaqueChallenge,
                FixedUtcNow,
                CancellationToken.None);

        bool secondConsumptionStaged =
            await secondStore.StageConsumeAsync(
                opaqueChallenge,
                FixedUtcNow,
                CancellationToken.None);

        Assert.True(
            firstConsumptionStaged);

        Assert.True(
            secondConsumptionStaged);

        bool[] saveResults =
            await Task.WhenAll(
                TrySaveAsync(
                    firstContext),
                TrySaveAsync(
                    secondContext));

        Assert.Single(
            saveResults,
            result =>
                result);

        Assert.Single(
            saveResults,
            result =>
                !result);

        await using IdentityDbContext verificationContext =
            CreateContext(
                connectionString);

        var verificationStore =
            new PostgresSocialAuthenticationChallengeStore(
                verificationContext);

        SocialAuthenticationChallenge? finalChallenge =
            await verificationStore.GetActiveAsync(
                opaqueChallenge,
                FixedUtcNow,
                CancellationToken.None);

        Assert.Null(
            finalChallenge);
    }

    private async Task ResetDatabaseAsync()
    {
        _fixture.DbContext
            .ChangeTracker
            .Clear();

        await _fixture.DbContext
            .Database
            .EnsureDeletedAsync();

        await _fixture.DbContext
            .Database
            .EnsureCreatedAsync();

        _fixture.DbContext
            .ChangeTracker
            .Clear();
    }

    private async Task<long> GetChallengeCountAsync()
    {
        NpgsqlConnection connection =
            (NpgsqlConnection)_fixture
                .DbContext
                .Database
                .GetDbConnection();

        bool shouldClose =
            connection.State !=
            ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using NpgsqlCommand command =
                new(
                    """
                    SELECT COUNT(*)
                    FROM identity.social_authentication_challenges;
                    """,
                    connection);

            object? value =
                await command.ExecuteScalarAsync();

            return Convert.ToInt64(
                value);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<string> GetSingleStringAsync(
        string sql)
    {
        NpgsqlConnection connection =
            (NpgsqlConnection)_fixture
                .DbContext
                .Database
                .GetDbConnection();

        bool shouldClose =
            connection.State !=
            ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using NpgsqlCommand command =
                new(
                    sql,
                    connection);

            object? value =
                await command.ExecuteScalarAsync();

            return Assert.IsType<string>(
                value);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<bool> TrySaveAsync(
        IdentityDbContext dbContext)
    {
        try
        {
            await dbContext.SaveChangesAsync();

            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private static IdentityDbContext CreateContext(
        string connectionString)
    {
        DbContextOptions<IdentityDbContext> options =
            new DbContextOptionsBuilder<
                IdentityDbContext>()
                .UseNpgsql(
                    connectionString)
                .Options;

        return new IdentityDbContext(
            options);
    }

    private static SocialAuthenticationChallenge
        CreateChallenge(
            DateTime expiresOnUtc)
    {
        return new SocialAuthenticationChallenge(
            ExternalLoginProvider.Google,
            "google-subject",
            "user@example.com",
            ExistingUserId: null,
            LinkVerificationRequestId: null,
            expiresOnUtc);
    }

    private static readonly DateTime FixedUtcNow =
        new(
            2026,
            8,
            23,
            10,
            0,
            0,
            DateTimeKind.Utc);
}