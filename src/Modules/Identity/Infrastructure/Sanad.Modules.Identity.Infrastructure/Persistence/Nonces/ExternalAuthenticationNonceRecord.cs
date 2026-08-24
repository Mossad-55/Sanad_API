using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

namespace Sanad.Modules.Identity.Infrastructure.Persistence.Nonces;

internal sealed class ExternalAuthenticationNonceRecord
{
    public Guid Id { get; set; }

    public ExternalLoginProvider Provider
    {
        get;
        set;
    }

    public string NonceHash
    {
        get;
        set;
    } = string.Empty;

    public DateTime CreatedOnUtc
    {
        get;
        set;
    }

    public DateTime ExpiresOnUtc
    {
        get;
        set;
    }

    public DateTime? ConsumedOnUtc
    {
        get;
        set;
    }
}