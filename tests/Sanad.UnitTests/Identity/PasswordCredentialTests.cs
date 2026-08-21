using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Identity.Domain.Authentication;

namespace Sanad.UnitTests.Identity;

public sealed class PasswordCredentialTests
{
    [Fact]
    public void Create_ShouldStoreTrimmedPasswordHash()
    {
        PasswordCredential credential =
            PasswordCredential.Create(
                "  hashed-password-value  ");

        Assert.Equal(
            "hashed-password-value",
            credential.PasswordHash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldRejectMissingPasswordHash(
        string? passwordHash)
    {
        Assert.Throws<DomainException>(
            () => PasswordCredential.Create(
                passwordHash!));
    }

    [Fact]
    public void Create_ShouldRejectHashThatIsTooLong()
    {
        string longHash = new(
            'A',
            PasswordCredential
                .MaximumHashLength + 1);

        Assert.Throws<DomainException>(
            () => PasswordCredential.Create(
                longHash));
    }

    [Fact]
    public void EqualHashes_ShouldHaveValueEquality()
    {
        PasswordCredential first =
            PasswordCredential.Create(
                "same-hash");

        PasswordCredential second =
            PasswordCredential.Create(
                "same-hash");

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentHashes_ShouldNotBeEqual()
    {
        PasswordCredential first =
            PasswordCredential.Create(
                "first-hash");

        PasswordCredential second =
            PasswordCredential.Create(
                "second-hash");

        Assert.NotEqual(first, second);
    }
}