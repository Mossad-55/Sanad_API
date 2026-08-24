using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Identity.Infrastructure.Persistence.Nonces;
using Sanad.Modules.Identity.Application.Authentication.SocialLogin;

namespace Sanad.Modules.Identity.Infrastructure.Nonces;

public sealed class PostgresExternalAuthenticationNonceStore :
    IExternalAuthenticationNonceStore
{
    private readonly IdentityDbContext
        _dbContext;

    public PostgresExternalAuthenticationNonceStore(
        IdentityDbContext dbContext)
    {
        _dbContext =
            dbContext;
    }

    public async Task<string> CreateAsync(
        ExternalLoginProvider provider,
        DateTime createdOnUtc,
        DateTime expiresOnUtc,
        CancellationToken cancellationToken)
    {
        EnsureSupportedProvider(
            provider);

        EnsureValidTimes(
            createdOnUtc,
            expiresOnUtc);

        string nonce =
            Base64UrlEncoder.Encode(
                RandomNumberGenerator.GetBytes(
                    ExternalAuthenticationNoncePolicy
                        .ByteLength));

        var record =
            new ExternalAuthenticationNonceRecord
            {
                Id =
                    Guid.CreateVersion7(),

                Provider =
                    provider,

                NonceHash =
                    Hash(
                        nonce),

                CreatedOnUtc =
                    createdOnUtc,

                ExpiresOnUtc =
                    expiresOnUtc
            };

        _dbContext.Set<
                ExternalAuthenticationNonceRecord>()
            .Add(
                record);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return nonce;
    }

    public async Task<bool> ConsumeAsync(
        ExternalLoginProvider provider,
        string nonce,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        EnsureSupportedProvider(
            provider);

        if (string.IsNullOrWhiteSpace(
                nonce))
        {
            return false;
        }

        if (utcNow.Kind !=
            DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Current time must be UTC.",
                nameof(utcNow));
        }

        string nonceHash =
            Hash(
                nonce);

        NpgsqlConnection connection =
            (NpgsqlConnection)_dbContext
                .Database
                .GetDbConnection();

        bool shouldClose =
            connection.State !=
            ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(
                cancellationToken);
        }

        try
        {
            await using NpgsqlCommand command =
                connection.CreateCommand();

            command.Transaction =
                _dbContext.Database
                    .CurrentTransaction?
                    .GetDbTransaction()
                    as NpgsqlTransaction;

            command.CommandText =
                """
                UPDATE identity.external_authentication_nonces
                SET consumed_on_utc = @utcNow
                WHERE nonce_hash = @nonceHash
                  AND provider = @provider
                  AND consumed_on_utc IS NULL
                  AND expires_on_utc > @utcNow
                RETURNING 1;
                """;

            command.Parameters.AddWithValue(
                "utcNow",
                utcNow);

            command.Parameters.AddWithValue(
                "nonceHash",
                nonceHash);

            command.Parameters.AddWithValue(
                "provider",
                (int)provider);

            object? result =
                await command.ExecuteScalarAsync(
                    cancellationToken);

            return result is not null;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static string Hash(
        string nonce)
    {
        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    nonce)));
    }

    private static void EnsureSupportedProvider(
        ExternalLoginProvider provider)
    {
        if (provider is not (
            ExternalLoginProvider.Google or
            ExternalLoginProvider.Apple))
        {
            throw new ArgumentOutOfRangeException(
                nameof(provider),
                "Only Google and Apple are supported.");
        }
    }

    private static void EnsureValidTimes(
        DateTime createdOnUtc,
        DateTime expiresOnUtc)
    {
        if (createdOnUtc.Kind !=
            DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Creation time must be UTC.",
                nameof(createdOnUtc));
        }

        if (expiresOnUtc.Kind !=
            DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Expiration time must be UTC.",
                nameof(expiresOnUtc));
        }

        if (expiresOnUtc <=
            createdOnUtc)
        {
            throw new ArgumentException(
                "Expiration time must be after creation time.",
                nameof(expiresOnUtc));
        }
    }
}