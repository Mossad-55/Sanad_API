using System.Net.Http.Json;
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
        if (string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            return Result<PaymobPaymentIntent>.Failure(
                new Error("Paymob.NotConfigured", "The payment gateway is not configured."));
        }

        string? integrationId = input.Method switch
        {
            PaymentMethod.Card => _options.CardIntegrationId,
            PaymentMethod.Wallet => _options.WalletIntegrationId,
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

            long amountCents = ToCents(input.Amount);

            // special_reference is returned in transaction callbacks as merchant_order_id,
            // so it MUST be the booking id — that is the key the webhook confirms with.
            string specialReference = input.BookingId.Value.ToString();

            var payload = new
            {
                amount = amountCents,
                currency = input.Currency,
                payment_methods = new[] { long.Parse(integrationId) },
                items = new[]
                {
                    new
                    {
                        name = "Care booking",
                        amount = amountCents,
                        description = "Sanad Care booking",
                        quantity = 1
                    }
                },
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
                },
                special_reference = specialReference,
                expiration = 3600,
                notification_url = string.IsNullOrWhiteSpace(_options.WebhookUrl)
                    ? null
                    : _options.WebhookUrl
            };

            using HttpRequestMessage request = new(HttpMethod.Post, $"{_options.BaseUrl}/v1/intention/");
            request.Headers.TryAddWithoutValidation("Authorization", $"Token {_options.SecretKey}");
            request.Content = JsonContent.Create(payload);

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Result<PaymobPaymentIntent>.Failure(
                    new Error("Paymob.GatewayError",
                        $"Paymob intention request failed with status {(int)response.StatusCode}."));
            }

            using JsonDocument document = JsonDocument.Parse(body);

            string clientSecret =
                document.RootElement.GetProperty("client_secret").GetString() ?? string.Empty;
            string intentionOrderId =
                document.RootElement.GetProperty("intention_order_id").GetRawText();

            return Result<PaymobPaymentIntent>.Success(
                new PaymobPaymentIntent(
                    specialReference,
                    intentionOrderId,
                    clientSecret,
                    _options.PublicKey));
        }
        catch (Exception exception) when (exception is JsonException or HttpRequestException)
        {
            return Result<PaymobPaymentIntent>.Failure(
                new Error("Paymob.GatewayError", "Unexpected payment gateway response."));
        }
    }

    public async Task<Result<string?>> RefundPaymentAsync(
        string paymobTransactionId,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            return Result<string?>.Failure(
                new Error("Paymob.NotConfigured", "The payment gateway is not configured."));
        }

        try
        {
            HttpClient httpClient = _httpClientFactory.CreateClient("Paymob");

            var payload = new
            {
                transaction_id = paymobTransactionId,
                amount_cents = ToCents(amount).ToString()
            };

            using HttpRequestMessage request = new(
                HttpMethod.Post,
                $"{_options.BaseUrl}/api/acceptance/void_refund/refund");
            request.Headers.TryAddWithoutValidation("Authorization", $"Token {_options.SecretKey}");
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Result<string?>.Failure(
                    new Error("Paymob.GatewayError",
                        $"Paymob refund request failed with status {(int)response.StatusCode}."));
            }

            using JsonDocument document = JsonDocument.Parse(body);

            string? refundTransactionId = document.RootElement.TryGetProperty("id", out JsonElement id)
                ? id.GetRawText()
                : null;

            return Result<string?>.Success(refundTransactionId);
        }
        catch (Exception exception) when (exception is JsonException or HttpRequestException)
        {
            return Result<string?>.Failure(
                new Error("Paymob.GatewayError", "Unexpected payment gateway response."));
        }
    }

    private static long ToCents(decimal amount)
    {
        return (long)decimal.Round(amount * 100m, 0, MidpointRounding.ToEven);
    }
}