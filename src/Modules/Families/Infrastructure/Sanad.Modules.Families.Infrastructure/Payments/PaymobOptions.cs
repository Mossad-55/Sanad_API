namespace Sanad.Modules.Families.Infrastructure.Payments;

public sealed class PaymobOptions
{
    public const string SectionName = "Paymob";

    public string BaseUrl { get; set; } = "https://accept.paymob.com/api";

    public string ApiKey { get; set; } = string.Empty;

    public string HmacSecret { get; set; } = string.Empty;

    public string CardIntegrationId { get; set; } = string.Empty;

    public string WalletIntegrationId { get; set; } = string.Empty;

    public string ApplePayIntegrationId { get; set; } = string.Empty;

    public string IframeId { get; set; } = string.Empty;
}