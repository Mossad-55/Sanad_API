using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;
using Sanad.Modules.Identity.Infrastructure.Nonces;

namespace Sanad.UnitTests.Identity.Infrastructure;

[Collection("LocalPostgres")]
public sealed class PostgresExternalAuthenticationNonceStoreTests
{
    private readonly LocalPostgresIdentityFixture
        _fixture;

    public PostgresExternalAuthenticationNonceStoreTests(
        LocalPostgresIdentityFixture fixture)
    {
        _fixture =
            fixture;
    }

    [LocalPostgresFact]
    public async Task Nonce_ShouldBeHashedProviderBoundExpiringAndOneTime()
    {
        await ResetDatabaseAsync();

        var store =
            new PostgresExternalAuthenticationNonceStore(
                _fixture.DbContext);

        string nonce =
            await store.CreateAsync(
                ExternalLoginProvider.Google,
                FixedUtcNow,
                FixedUtcNow.AddMinutes(5),
                CancellationToken.None);

        string storedHash =
            await GetSingleHashAsync();

        Assert.NotEqual(
            nonce,
            storedHash);

        Assert.Equal(
            64,
            storedHash.Length);

        Assert.False(
            await store.ConsumeAsync(
                ExternalLoginProvider.Apple,
                nonce,
                FixedUtcNow,
                CancellationToken.None));

        Assert.True(
            await store.ConsumeAsync(
                ExternalLoginProvider.Google,
                nonce,
                FixedUtcNow,
                CancellationToken.None));

        Assert.False(
            await store.ConsumeAsync(
                ExternalLoginProvider.Google,
                nonce,
                FixedUtcNow,
                CancellationToken.None));

        string expiredNonce =
            await store.CreateAsync(
                ExternalLoginProvider.Apple,
                FixedUtcNow.AddMinutes(-10),
                FixedUtcNow.AddMinutes(-5),
                CancellationToken.None);

        Assert.False(
            await store.ConsumeAsync(
                ExternalLoginProvider.Apple,
                expiredNonce,
                FixedUtcNow,
                CancellationToken.None));
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
                    SELECT nonce_hash
                    FROM identity.external_authentication_nonces
                    ORDER BY created_on_utc
                    LIMIT 1;
                    """,
                    connection);

            return Assert.IsType<string>(
                await command.ExecuteScalarAsync());
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static readonly DateTime FixedUtcNow =
        new(
            2026,
            8,
            24,
            10,
            0,
            0,
            DateTimeKind.Utc);
}