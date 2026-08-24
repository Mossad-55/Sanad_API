using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Authentication.SocialLogin;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Challenges;
using Sanad.Modules.Identity.Infrastructure.Persistence;

namespace Sanad.UnitTests.Identity.Infrastructure;

[Collection("LocalPostgres")]
public sealed class PostgresSocialRegistrationChallengeStoreTests
{
    private readonly LocalPostgresIdentityFixture
        _fixture;

    public PostgresSocialRegistrationChallengeStoreTests(
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
            new PostgresSocialRegistrationChallengeStore(
                _fixture.DbContext);

        string opaqueChallenge =
            await store.CreateAsync(
                CreateChallenge(
                    FixedUtcNow.AddMinutes(10)),
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
            await GetSingleHashAsync();

        Assert.NotEqual(
            opaqueChallenge,
            storedHash);

        Assert.Equal(
            64,
            storedHash.Length);
    }

    [LocalPostgresFact]
    public async Task StageConsumeAsync_ShouldReturnFullChallengeAndAllowOnlyOneConsume()
    {
        await ResetDatabaseAsync();

        var store =
            new PostgresSocialRegistrationChallengeStore(
                _fixture.DbContext);

        VerificationRequestId requestId =
            VerificationRequestId.New();

        SocialRegistrationChallenge challenge =
            CreateChallenge(
                FixedUtcNow.AddMinutes(10),
                requestId);

        string opaqueChallenge =
            await store.CreateAsync(
                challenge,
                CancellationToken.None);

        await _fixture.DbContext
            .SaveChangesAsync();

        SocialRegistrationChallenge? activeChallenge =
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
            "محمد أحمد",
            activeChallenge.ArabicFullName);

        Assert.Equal(
            "Mohamed Ahmed",
            activeChallenge.EnglishFullName);

        Assert.Equal(
            AccountType.Family,
            activeChallenge.AccountType);

        Assert.Equal(
            "+201001234567",
            activeChallenge.PhoneNumber);

        Assert.Equal(
            requestId,
            activeChallenge.PhoneVerificationRequestId);

        bool consumptionStaged =
            await store.StageConsumeAsync(
                opaqueChallenge,
                FixedUtcNow,
                CancellationToken.None);

        Assert.True(
            consumptionStaged);

        await _fixture.DbContext
            .SaveChangesAsync();

        SocialRegistrationChallenge? consumedChallenge =
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
            new PostgresSocialRegistrationChallengeStore(
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

        SocialRegistrationChallenge? wrong =
            await store.GetActiveAsync(
                "wrong",
                FixedUtcNow,
                CancellationToken.None);

        SocialRegistrationChallenge? expired =
            await store.GetActiveAsync(
                expiredOpaqueChallenge,
                FixedUtcNow,
                CancellationToken.None);

        SocialRegistrationChallenge? valid =
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
                    FROM identity.social_registration_challenges;
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

    private async Task<string> GetSingleHashAsync()
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
                    SELECT challenge_hash
                    FROM identity.social_registration_challenges;
                    """,
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

    private static SocialRegistrationChallenge
        CreateChallenge(
            DateTime expiresOnUtc,
            VerificationRequestId? requestId = null)
    {
        return new SocialRegistrationChallenge(
            ExternalLoginProvider.Google,
            "google-subject",
            "user@example.com",
            "محمد أحمد",
            "Mohamed Ahmed",
            AccountType.Family,
            "+201001234567",
            requestId ??
                VerificationRequestId.New(),
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