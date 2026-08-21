using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

public sealed class UserExternalLogin :
    Entity<UserExternalLoginId>
{
    public const int MaximumProviderSubjectLength = 255;

    private UserExternalLogin()
    {
    }

    private UserExternalLogin(
        UserExternalLoginId id,
        ExternalLoginProvider provider,
        string providerSubject,
        DateTime linkedOnUtc)
        : base(id)
    {
        Provider = provider;
        ProviderSubject = providerSubject;
        LinkedOnUtc = linkedOnUtc;
    }

    public ExternalLoginProvider Provider
    {
        get;
        private set;
    }

    public string ProviderSubject { get; private set; } =
        string.Empty;

    public DateTime LinkedOnUtc { get; private set; }

    internal static UserExternalLogin Create(
        ExternalLoginProvider provider,
        string providerSubject,
        DateTime linkedOnUtc)
    {
        if (!Enum.IsDefined(provider))
        {
            throw new DomainException(
                "External login provider is invalid.");
        }

        if (string.IsNullOrWhiteSpace(
            providerSubject))
        {
            throw new DomainException(
                "External provider subject is required.");
        }

        string normalizedSubject =
            providerSubject.Trim();

        if (normalizedSubject.Length >
            MaximumProviderSubjectLength)
        {
            throw new DomainException(
                $"External provider subject cannot exceed " +
                $"{MaximumProviderSubjectLength} characters.");
        }

        if (linkedOnUtc.Kind !=
            DateTimeKind.Utc)
        {
            throw new DomainException(
                "External login time must be in UTC.");
        }

        return new UserExternalLogin(
            UserExternalLoginId.New(),
            provider,
            normalizedSubject,
            linkedOnUtc);
    }
}