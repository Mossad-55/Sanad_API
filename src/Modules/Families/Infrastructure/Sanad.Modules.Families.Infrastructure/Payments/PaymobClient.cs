using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.Modules.Families.Infrastructure.Payments;

public sealed class PaymobClient : IPaymobClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PaymobOptions _options;

    private readonly object _tokenLock = new();
    private string? _authToken;
    private DateTime _tokenAcquiredAtUtc;

    public PaymobClient(
        IHttpClientFactory httpClientFactory,
        IOptions<PaymobOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(
        PaymobPaymentIntentInput input,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return Result<PaymobPaymentIntent>.Failure(
                new Error("Paymob.NotConfigured", "The payment gateway is not configured."));
        }

        string? integrationId = input.Method switch
        {
            PaymentMethod.Card => _options.CardIntegrationId,
            PaymentMethod.Wallet => _options.WalletIntegrationId,
            PaymentMethod.ApplePay => _options.ApplePayIntegrationId,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(integrationId))
        {
            return Result<PaymobPaymentIntent>.Failure(
                new Error("Paymob.MethodNotAvailable", "The selected payment method is not available."));
        }

        try
        {
            HttpClient httpClient = _httpClientFactory.CreateClient("Paymob");

            string authToken = await GetAuthTokenAsync(httpClient, cancellationToken);

            long amountCents = ToCents(input.Amount);

            var orderPayload = new
            {
                auth_token = authToken,
                delivery_needed = false,
                amount_cents = amountCents,
                currency = input.Currency,
                merchant_order_id = input.BookingId.Value.ToString(),
                items = Array.Empty<object>()
            };

            using JsonDocument order = await PostJsonAsync(
                httpClient, "/ecommerce/orders", orderPayload, cancellationToken);

            long paymobOrderId = order.RootElement.GetProperty("id").GetInt64();

            var paymentKeyPayload = new
            {
                auth_token = authToken,
                amount_cents = amountCents,
                currency = input.Currency,
                order_id = paymobOrderId,
                integration_id = long.Parse(integrationId),
                billing_data = new
                {
                    first_name = input.Billing.FirstName,
                    last_name = input.Billing.LastName,
                    email = input.Billing.Email,
                    phone_number = input.Billing.PhoneNumber,
                    street = "NA",
                    building = "NA",
                    floor = "NA",
                    apartment = "NA",
                    city = "NA",
                    state = "NA",
                    country = "EG",
                    postal_code = "NA"
                }
            };

            using JsonDocument paymentKey = await PostJsonAsync(
                httpClient, "/acceptance/payment_keys", paymentKeyPayload, cancellationToken);

            string paymentKeyToken =
                paymentKey.RootElement.GetProperty("token").GetString() ?? string.Empty;

            if (input.Method == PaymentMethod.Wallet)
            {
                var walletPayload = new
                {
                    source = new
                    {
                        identifier = input.WalletNumber,
                        subtype = "WALLET"
                    },
                    payment_token = paymentKeyToken
                };

                using JsonDocument wallet = await PostJsonAsync(
                    httpClient, "/acceptance/payments/pay", walletPayload, cancellationToken);

                string? redirectUrl = wallet.RootElement.TryGetProperty("redirect_url", out JsonElement url)
                    ? url.GetString()
                    : null;

                return Result<PaymobPaymentIntent>.Success(
                    new PaymobPaymentIntent(
                        paymobOrderId.ToString(),
                        null,
                        redirectUrl));
            }

            if (input.Method == PaymentMethod.ApplePay)
            {
                return Result<PaymobPaymentIntent>.Failure(
                    new Error("Paymob.MethodNotAvailable", "Apple Pay is not enabled yet."));
            }

            string iframeUrl =
                $"{_options.BaseUrl}/acceptance/iframes/{_options.IframeId}?payment_token={paymentKeyToken}";

            return Result<PaymobPaymentIntent>.Success(
                new PaymobPaymentIntent(
                    paymobOrderId.ToString(),
                    iframeUrl,
                    null));
        }
        catch (PaymobHttpException exception)
        {
            return Result<PaymobPaymentIntent>.Failure(
                new Error("Paymob.GatewayError", exception.Message));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return Result<PaymobPaymentIntent>.Failure(
                new Error("Paymob.GatewayError", "Unexpected payment gateway response."));
        }
    }

    private async Task<string> GetAuthTokenAsync(
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        lock (_tokenLock)
        {
            if (_authToken is not null
                && DateTime.UtcNow - _tokenAcquiredAtUtc < TimeSpan.FromMinutes(55))
            {
                return _authToken;
            }
        }

        using JsonDocument response = await PostJsonAsync(
            httpClient,
            "/auth/tokens",
            new { api_key = _options.ApiKey },
            cancellationToken);

        string token = response.RootElement.GetProperty("token").GetString() ?? string.Empty;

        lock (_tokenLock)
        {
            _authToken = token;
            _tokenAcquiredAtUtc = DateTime.UtcNow;
        }

        return token;
    }

    private static async Task<JsonDocument> PostJsonAsync(
        HttpClient httpClient,
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.PostAsync(
            path,
            new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"),
            cancellationToken);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new PaymobHttpException(
                $"Paymob request to '{path}' failed with status {(int)response.StatusCode}.");
        }

        return JsonDocument.Parse(body);
    }

    private static long ToCents(decimal amount)
    {
        return (long)decimal.Round(amount * 100m, 0, MidpointRounding.ToEven);
    }

    private sealed class PaymobHttpException : Exception
    {
        public PaymobHttpException(string message) : base(message)
        {
        }
    }
}