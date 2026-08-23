using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;
using Sanad.Modules.Identity.Infrastructure.Security;

namespace Sanad.UnitTests.Identity.Infrastructure;

public sealed class DevelopmentExternalIdentityVerifierTests
{
    [Theory]
    [InlineData(ExternalLoginProvider.Google)]
    [InlineData(ExternalLoginProvider.Apple)]
    public async Task VerifyAsync_ShouldReturnNull(
        ExternalLoginProvider provider)
    {
        var verifier =
            new DevelopmentExternalIdentityVerifier();

        var result =
            await verifier.VerifyAsync(
                provider,
                "untrusted-provider-credential",
                CancellationToken.None);

        Assert.Null(result);
    }
}