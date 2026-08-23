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
    private readonly LocalPostgresIdentityFixture _fixture;

    public PostgresSocialRegistrationChallengeStoreTests(LocalPostgresIdentityFixture fixture)
    {
        _fixture = fixture;
    }

    [LocalPostgresFact]
    public async Task CreateAsync_ShouldStoreHashAndReturnOpaqueChallenge()
    {
        await ResetDatabaseAsync();
        var store = new PostgresSocialRegistrationChallengeStore(_fixture.DbContext);

        string opaqueChallenge = await store.CreateAsync(
            CreateChallenge(FixedUtcNow.AddMinutes(10)),
            CancellationToken.None);

        string storedHash = await GetSingleHashAsync();

        Assert.False(string.IsNullOrWhiteSpace(opaqueChallenge));
        Assert.NotEqual(opaqueChallenge, storedHash);
        Assert.Equal(64, storedHash.Length);
    }

    [LocalPostgresFact]
    public async Task ConsumeAsync_ShouldReturnFullChallengeAndAllowOnlyOneConsume()
    {
        await ResetDatabaseAsync();
        var store = new PostgresSocialRegistrationChallengeStore(_fixture.DbContext);
        VerificationRequestId requestId = VerificationRequestId.New();

        SocialRegistrationChallenge challenge = CreateChallenge(
            FixedUtcNow.AddMinutes(10), requestId);

        string opaqueChallenge = await store.CreateAsync(challenge, CancellationToken.None);
        SocialRegistrationChallenge? first = await store.ConsumeAsync(opaqueChallenge, FixedUtcNow, CancellationToken.None);
        SocialRegistrationChallenge? second = await store.ConsumeAsync(opaqueChallenge, FixedUtcNow, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal(ExternalLoginProvider.Google, first!.Provider);
        Assert.Equal("google-subject", first.ProviderSubject);
        Assert.Equal("user@example.com", first.VerifiedEmail);
        Assert.Equal("محمد أحمد", first.ArabicFullName);
        Assert.Equal("Mohamed Ahmed", first.EnglishFullName);
        Assert.Equal(AccountType.Family, first.AccountType);
        Assert.Equal("+201001234567", first.PhoneNumber);
        Assert.Equal(requestId, first.PhoneVerificationRequestId);
    }

    [LocalPostgresFact]
    public async Task ConsumeAsync_ShouldReturnNull_ForWrongOrExpiredChallenge()
    {
        await ResetDatabaseAsync();
        var store = new PostgresSocialRegistrationChallengeStore(_fixture.DbContext);

        string valid = await store.CreateAsync(CreateChallenge(FixedUtcNow.AddMinutes(10)), CancellationToken.None);
        string expired = await store.CreateAsync(CreateChallenge(FixedUtcNow), CancellationToken.None);

        Assert.Null(await store.ConsumeAsync("wrong", FixedUtcNow, CancellationToken.None));
        Assert.Null(await store.ConsumeAsync(expired, FixedUtcNow, CancellationToken.None));
        Assert.NotNull(await store.ConsumeAsync(valid, FixedUtcNow, CancellationToken.None));
    }

    private async Task ResetDatabaseAsync()
    {
        _fixture.DbContext.ChangeTracker.Clear();
        await _fixture.DbContext.Database.EnsureDeletedAsync();
        await _fixture.DbContext.Database.EnsureCreatedAsync();
        _fixture.DbContext.ChangeTracker.Clear();
    }

    private async Task<string> GetSingleHashAsync()
    {
        NpgsqlConnection connection = (NpgsqlConnection)_fixture.DbContext.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using NpgsqlCommand command = new(
                "SELECT challenge_hash FROM identity.social_registration_challenges;", connection);
            return Assert.IsType<string>(await command.ExecuteScalarAsync());
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static SocialRegistrationChallenge CreateChallenge(DateTime expiry, VerificationRequestId? requestId = null)
    {
        return new SocialRegistrationChallenge(
            ExternalLoginProvider.Google,
            "google-subject",
            "user@example.com",
            "محمد أحمد",
            "Mohamed Ahmed",
            AccountType.Family,
            "+201001234567",
            requestId ?? VerificationRequestId.New(),
            expiry);
    }

    private static readonly DateTime FixedUtcNow = new(2026, 8, 23, 10, 0, 0, DateTimeKind.Utc);
}
