namespace Sanad.Modules.Families.Infrastructure.Payments;

public sealed class PaymobOptions
{
    public const string SectionName = "Paymob";

    public string BaseUrl { get; set; } = "https://accept.paymob.com";

    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Merchant API key used only to obtain an auth token for the subscription
    /// management API (amount updates). That namespace rejects the secret key,
    /// so it must never be reused for it.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    public string PublicKey { get; set; } = string.Empty;

    public string HmacSecret { get; set; } = string.Empty;

    public string CardIntegrationId { get; set; } = string.Empty;

    public string Card3dsIntegrationId { get; set; } = string.Empty;

    public string WalletIntegrationId { get; set; } = string.Empty;

    public string WebhookUrl { get; set; } = string.Empty;

    public string RedirectionUrl { get; set; } = string.Empty;
}
