using FluentValidation.TestHelper;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication.SocialLogin;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

namespace Sanad.UnitTests.Identity.SocialLogin;

public sealed class ExternalAuthenticationNonceCommandTests
{
    [Theory]
    [InlineData(ExternalLoginProvider.Google)]
    [InlineData(ExternalLoginProvider.Apple)]
    public async Task Handler_ShouldCreateProviderBoundNonce(
        ExternalLoginProvider provider)
    {
        var store =
            new RecordingNonceStore();

        var handler =
            new RequestExternalAuthenticationNonceCommandHandler(
                store,
                new FixedDateTimeProvider());

        Result<RequestExternalAuthenticationNonceResponse> result =
            await handler.Handle(
                new RequestExternalAuthenticationNonceCommand(
                    provider),
                CancellationToken.None);

        Assert.True(
            result.IsSuccess);

        Assert.Equal(
            "test-nonce",
            result.Value.Nonce);

        Assert.Equal(
            FixedDateTimeProvider.UtcNowValue.Add(
                ExternalAuthenticationNoncePolicy.Lifetime),
            result.Value.ExpiresOnUtc);

        Assert.Equal(
            provider,
            store.Provider);

        Assert.Equal(
            FixedDateTimeProvider.UtcNowValue,
            store.CreatedOnUtc);

        Assert.Equal(
            result.Value.ExpiresOnUtc,
            store.ExpiresOnUtc);
    }

    [Theory]
    [InlineData(ExternalLoginProvider.Unknown)]
    [InlineData((ExternalLoginProvider)999)]
    public void Validator_ShouldRejectUnsupportedProvider(
        ExternalLoginProvider provider)
    {
        var validator =
            new RequestExternalAuthenticationNonceCommandValidator();

        validator
            .TestValidate(
                new RequestExternalAuthenticationNonceCommand(
                    provider))
            .ShouldHaveValidationErrorFor(
                command =>
                    command.Provider);
    }

    private sealed class RecordingNonceStore :
        IExternalAuthenticationNonceStore
    {
        internal ExternalLoginProvider Provider
        {
            get;
            private set;
        }

        internal DateTime CreatedOnUtc
        {
            get;
            private set;
        }

        internal DateTime ExpiresOnUtc
        {
            get;
            private set;
        }

        public Task<string> CreateAsync(
            ExternalLoginProvider provider,
            DateTime createdOnUtc,
            DateTime expiresOnUtc,
            CancellationToken cancellationToken)
        {
            Provider =
                provider;

            CreatedOnUtc =
                createdOnUtc;

            ExpiresOnUtc =
                expiresOnUtc;

            return Task.FromResult(
                "test-nonce");
        }

        public Task<bool> ConsumeAsync(
            ExternalLoginProvider provider,
            string nonce,
            DateTime utcNow,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
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
                    24,
                    10,
                    0,
                    0,
                    DateTimeKind.Utc);

        public DateTime UtcNow =>
            UtcNowValue;
    }
}