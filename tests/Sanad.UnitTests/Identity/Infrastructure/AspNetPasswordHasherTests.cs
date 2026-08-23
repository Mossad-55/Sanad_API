using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Infrastructure.Security;

namespace Sanad.UnitTests.Identity.Infrastructure;

public sealed class AspNetPasswordHasherTests
{
    [Fact]
    public void Hash_ShouldProduceNonPlaintextHash()
    {
        var hasher = new AspNetPasswordHasher();

        string hash = hasher.Hash("SecurePassword123");

        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.NotEqual("SecurePassword123", hash);
    }

    [Fact]
    public void Verify_ShouldReturnSuccess_ForCorrectPassword()
    {
        var hasher = new AspNetPasswordHasher();

        string hash = hasher.Hash("SecurePassword123");

        PasswordVerificationResult result = hasher.Verify(
            hash,
            "SecurePassword123");

        Assert.True(result is
            PasswordVerificationResult.Success or
            PasswordVerificationResult.SuccessRehashNeeded);
    }

    [Fact]
    public void Verify_ShouldReturnFailed_ForIncorrectPassword()
    {
        var hasher = new AspNetPasswordHasher();

        string hash = hasher.Hash("SecurePassword123");

        PasswordVerificationResult result = hasher.Verify(
            hash,
            "DifferentPassword123");

        Assert.Equal(
            PasswordVerificationResult.Failed,
            result);
    }

    [Theory]
    [InlineData(null, "SecurePassword123")]
    [InlineData("", "SecurePassword123")]
    [InlineData("   ", "SecurePassword123")]
    [InlineData("valid-hash", null)]
    [InlineData("valid-hash", "")]
    [InlineData("valid-hash", "   ")]
    public void Verify_ShouldReturnFailed_ForMissingHashOrPassword(
        string? passwordHash,
        string? password)
    {
        var hasher = new AspNetPasswordHasher();

        PasswordVerificationResult result = hasher.Verify(
            passwordHash!,
            password!);

        Assert.Equal(
            PasswordVerificationResult.Failed,
            result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Hash_ShouldRejectMissingPassword(
        string? password)
    {
        var hasher = new AspNetPasswordHasher();

        Assert.ThrowsAny<ArgumentException>(
            () => hasher.Hash(password!));
    }
}
