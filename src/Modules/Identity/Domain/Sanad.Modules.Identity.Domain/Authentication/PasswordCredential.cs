using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Identity.Domain.Authentication;

public sealed class PasswordCredential :
    ValueObject
{
    public const int MaximumHashLength = 2048;

    private PasswordCredential()
    {
    }

    private PasswordCredential(
        string passwordHash)
    {
        PasswordHash = passwordHash;
    }

    public string PasswordHash { get; private set; } =
        string.Empty;

    internal static PasswordCredential Create(
        string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(
            passwordHash))
        {
            throw new DomainException(
                "Password hash is required.");
        }

        string normalizedHash =
            passwordHash.Trim();

        if (normalizedHash.Length >
            MaximumHashLength)
        {
            throw new DomainException(
                $"Password hash cannot exceed " +
                $"{MaximumHashLength} characters.");
        }

        return new PasswordCredential(
            normalizedHash);
    }

    protected override IEnumerable<object?>
        GetEqualityComponents()
    {
        yield return PasswordHash;
    }
}