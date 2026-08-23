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
    private readonly LocalPostgresIdentityFixture _fixture;

    public PostgresSocialAuthenticationChallengeStoreTests(
        LocalPostgresIdentityFixture fixture)
    {
        _fixture = fixture;
    }

    [LocalPostgresFact]
    public async Task CreateAsync_ShouldStoreHashAndReturnOpaqueChallenge()
    {
        await ResetDatabaseAsync();

        var store = new PostgresSocialAuthenticationChallengeStore(
            _fixture.DbContext);

        SocialAuthenticationChallenge challenge = CreateChallenge(
            FixedUtcNow.AddMinutes(10));

        string opaqueChallenge = await store.CreateAsync(
            challenge,
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(opaqueChallenge));

        string storedHash = await GetSingleStringAsync(
            "SELECT challenge_hash FROM identity.social_authentication_challenges;");

        Assert.NotEqual(opaqueChallenge, storedHash);
        Assert.Equal(64, storedHash.Length);
    }

    [LocalPostgresFact]
    public async Task ConsumeAsync_ShouldReturnStoredChallengeAndAllowOnlyOneConsume()
    {
        await ResetDatabaseAsync();

        var store = new PostgresSocialAuthenticationChallengeStore(
            _fixture.DbContext);

        UserId existingUserId = UserId.New();
        VerificationRequestId requestId = VerificationRequestId.New();

        SocialAuthenticationChallenge challenge = new(
            ExternalLoginProvider.Google,
            "google-subject",
            "user@example.com",
            existingUserId,
            requestId,
            FixedUtcNow.AddMinutes(10));

        string opaqueChallenge = await store.CreateAsync(
            challenge,
            CancellationToken.None);

        SocialAuthenticationChallenge? first = await store.ConsumeAsync(
            opaqueChallenge,
            FixedUtcNow,
            CancellationToken.None);

        SocialAuthenticationChallenge? second = await store.ConsumeAsync(
            opaqueChallenge,
            FixedUtcNow,
            CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal(ExternalLoginProvider.Google, first!.Provider);
        Assert.Equal("google-subject", first.ProviderSubject);
        Assert.Equal("user@example.com", first.VerifiedEmail);
        Assert.Equal(existingUserId, first.ExistingUserId);
        Assert.Equal(requestId, first.LinkVerificationRequestId);
    }

    [LocalPostgresFact]
    public async Task ConsumeAsync_ShouldReturnNull_ForWrongOrExpiredChallenge()
    {
        await ResetDatabaseAsync();

        var store = new PostgresSocialAuthenticationChallengeStore(
            _fixture.DbContext);

        string validOpaqueChallenge = await store.CreateAsync(
            CreateChallenge(FixedUtcNow.AddMinutes(10)),
            CancellationToken.None);

        string expiredOpaqueChallenge = await store.CreateAsync(
            CreateChallenge(FixedUtcNow),
            CancellationToken.None);

        SocialAuthenticationChallenge? wrong = await store.ConsumeAsync(
            "wrong-opaque-challenge",
            FixedUtcNow,
            CancellationToken.None);

        SocialAuthenticationChallenge? expired = await store.ConsumeAsync(
            expiredOpaqueChallenge,
            FixedUtcNow,
            CancellationToken.None);

        SocialAuthenticationChallenge? valid = await store.ConsumeAsync(
            validOpaqueChallenge,
            FixedUtcNow,
            CancellationToken.None);

        Assert.Null(wrong);
        Assert.Null(expired);
        Assert.NotNull(valid);
    }

    [LocalPostgresFact]
    public async Task ConsumeAsync_ShouldAllowExactlyOneConcurrentConsumer()
    {
        await ResetDatabaseAsync();

        var creationStore = new PostgresSocialAuthenticationChallengeStore(
            _fixture.DbContext);

        string opaqueChallenge = await creationStore.CreateAsync(
            CreateChallenge(FixedUtcNow.AddMinutes(10)),
            CancellationToken.None);

        string connectionString = _fixture.ConnectionString;

        await using IdentityDbContext firstContext = CreateContext(connectionString);
        await using IdentityDbContext secondContext = CreateContext(connectionString);

        var firstStore = new PostgresSocialAuthenticationChallengeStore(firstContext);
        var secondStore = new PostgresSocialAuthenticationChallengeStore(secondContext);

        Task<SocialAuthenticationChallenge?> firstTask = firstStore.ConsumeAsync(
            opaqueChallenge,
            FixedUtcNow,
            CancellationToken.None);

        Task<SocialAuthenticationChallenge?> secondTask = secondStore.ConsumeAsync(
            opaqueChallenge,
            FixedUtcNow,
            CancellationToken.None);

        SocialAuthenticationChallenge?[] results = await Task.WhenAll(
            firstTask,
            secondTask);

        Assert.Single(results, result => result is not null);
        Assert.Single(results, result => result is null);
    }

    private async Task ResetDatabaseAsync()
    {
        _fixture.DbContext.ChangeTracker.Clear();

        await _fixture.DbContext.Database.EnsureDeletedAsync();
        await _fixture.DbContext.Database.EnsureCreatedAsync();

        _fixture.DbContext.ChangeTracker.Clear();
    }

    private async Task<string> GetSingleStringAsync(string sql)
    {
        NpgsqlConnection connection = (NpgsqlConnection)_fixture.DbContext
            .Database.GetDbConnection();

        await connection.OpenAsync();

        try
        {
            await using NpgsqlCommand command = new(sql, connection);
            object? value = await command.ExecuteScalarAsync();

            return Assert.IsType<string>(value);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static IdentityDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new IdentityDbContext(options);
    }

    private static SocialAuthenticationChallenge CreateChallenge(
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

    private static readonly DateTime FixedUtcNow = new(
        2026, 8, 23, 10, 0, 0, DateTimeKind.Utc);
}
