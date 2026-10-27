using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Infrastructure.Payments;

public sealed class PaymobClient : IPaymobClient
{
    /// <summary>Maximum length of the sanitized upstream reason appended to a
    /// gateway error description. Keeps operator-visible text bounded.</summary>
    private const int MaxUpstreamReasonLength = 300;

    /// <summary>Upstream body fields that may carry Paymob's rejection reason, in
    /// the order they are preferred when more than one is present.</summary>
    private static readonly string[] ReasonFieldNames = ["detail", "message", "status"];

    /// <summary>Request fields whose first validation message is surfaced when
    /// Paymob answers with a DRF-style field-error map on a 400.</summary>
    private static readonly string[] FieldErrorNames =
    [
        "amount",
        "currency",
        "payment_methods",
        "items",
        "billing_data",
        "special_reference",
        "notification_url",
        "redirection_url",
        "subscription_plan_id",
        "merchant_order_id"
    ];

    /// <summary>Field names whose value must never be echoed back — anything that
    /// can carry a credential rather than a rejection reason.</summary>
    private static readonly string[] SensitiveFieldNames =
        ["secret", "token", "key", "authorization", "password", "signature", "hmac", "auth"];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PaymobOptions _options;

    /// <summary>Merchant auth tokens are valid for 60 minutes; renew with a
    /// margin so a call never races the expiry.</summary>
    private static readonly TimeSpan AuthTokenLifetime = TimeSpan.FromMinutes(55);

    private readonly SemaphoreSlim _authTokenGate = new(1, 1);
    private string? _authToken;
    private DateTimeOffset _authTokenExpiresUtc;

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

        // Validate the configured integration id BEFORE any provider request.
        // A malformed/non-numeric value is a configuration problem, not a
        // provider outcome; it maps to the existing safe method-unavailable
        // error and the value is never echoed back to the caller.
        if (!TryParseIntegrationId(integrationId, out long paymobIntegrationId))
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
            string specialReference = string.IsNullOrWhiteSpace(input.MerchantReference)
                ? input.BookingId.Value.ToString()
                : input.MerchantReference;

            var payload = new Dictionary<string, object?>
            {
                ["amount"] = amountCents,
                ["currency"] = input.Currency,
                ["payment_methods"] = new[] { paymobIntegrationId },
                ["items"] = new[]
                {
                    new
                    {
                        name = "Care booking",
                        amount = amountCents,
                        description = "Sanad Care booking",
                        quantity = 1
                    }
                },
                ["billing_data"] = new
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
                ["special_reference"] = specialReference,
                ["expiration"] = 3600
            };

            // A null URL is not "unset" to Paymob: it answers 400
            // {"notification_url": ["This field may not be null."]}. Omit the
            // key entirely when unconfigured instead of sending null.
            AddUrlIfConfigured(payload, "notification_url", _options.WebhookUrl);

            using HttpRequestMessage request = new(HttpMethod.Post, $"{_options.BaseUrl}/v1/intention/");
            request.Headers.TryAddWithoutValidation("Authorization", $"Token {_options.SecretKey}");
            request.Content = JsonContent.Create(payload);

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string reason = DescribeUpstreamReason(body);

                return Result<PaymobPaymentIntent>.Failure(
                    new Error("Paymob.GatewayError",
                        $"Paymob intention request failed with status {(int)response.StatusCode}."
                        + (reason.Length == 0 ? string.Empty : $" upstream: {reason}")));
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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient timeout (or an equivalent transport cancellation not
            // initiated by the caller): surface a safe gateway error instead of
            // letting a 500 escape. A caller-cancelled request is rethrown by
            // the filter falling through to the runtime.
            return Result<PaymobPaymentIntent>.Failure(
                new Error("Paymob.GatewayError", "The payment gateway did not respond in time."));
        }
        // A truncated 200 body (valid JSON missing a required field) is an
        // unexpected upstream shape, not a server fault: KeyNotFoundException
        // belongs to the same safe outcome as malformed JSON.
        catch (Exception exception) when (exception is JsonException or HttpRequestException or KeyNotFoundException)
        {
            return Result<PaymobPaymentIntent>.Failure(
                new Error("Paymob.GatewayError", "Unexpected payment gateway response."));
        }
    }

    public async Task<Result<PaymobPaymentIntent>> CreateSubscriptionPaymentIntentAsync(
        PaymobSubscriptionPaymentIntentInput input,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            return Result<PaymobPaymentIntent>.Failure(
                new Error("Paymob.NotConfigured", "The payment gateway is not configured."));
        }

        bool cardEnrollment = input.Method == SubscriptionPaymentMethod.Card
            && input.SubscriptionPlanId is not null;
        string? integrationId = input.Method switch
        {
            SubscriptionPaymentMethod.Card when cardEnrollment => _options.Card3dsIntegrationId,
            SubscriptionPaymentMethod.Card => _options.CardIntegrationId,
            SubscriptionPaymentMethod.Wallet => _options.WalletIntegrationId,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(integrationId))
        {
            return Result<PaymobPaymentIntent>.Failure(
                new Error("Paymob.MethodNotAvailable", "The selected payment method is not available."));
        }

        // R5/R6: a non-numeric integration id previously reached long.Parse
        // inside the HTTP try and surfaced as a generic gateway error. Validate
        // it here so a configuration fault is reported as the existing safe
        // method-unavailable outcome, before any provider call, without echoing
        // the configured value.
        if (!TryParseIntegrationId(integrationId, out long paymobIntegrationId))
        {
            return Result<PaymobPaymentIntent>.Failure(
                new Error("Paymob.MethodNotAvailable", "The selected payment method is not available."));
        }

        if (string.IsNullOrWhiteSpace(input.MerchantReference))
        {
            return Result<PaymobPaymentIntent>.Failure(
                new Error("Paymob.InvalidReference", "A payment merchant reference is required."));
        }

        if (input.SubscriptionPlanId is <= 0)
        {
            return Result<PaymobPaymentIntent>.Failure(
                new Error("Paymob.InvalidSubscriptionPlan", "The Paymob subscription plan id is invalid."));
        }

        try
        {
            HttpClient httpClient = _httpClientFactory.CreateClient("Paymob");
            long amountCents = ToCents(input.Amount);
            var payload = new Dictionary<string, object?>
            {
                ["amount"] = amountCents,
                ["currency"] = input.Currency,
                ["payment_methods"] = new[] { paymobIntegrationId },
                ["items"] = new[]
                {
                    new
                    {
                        name = "Sanad subscription",
                        amount = amountCents,
                        description = "Sanad subscription purchase",
                        quantity = 1
                    }
                },
                ["billing_data"] = new
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
                ["special_reference"] = input.MerchantReference,
                ["expiration"] = 3600
            };

            // A null URL is rejected outright ("This field may not be null."),
            // so unconfigured URLs are omitted rather than sent as null.
            AddUrlIfConfigured(payload, "notification_url", _options.WebhookUrl);
            AddUrlIfConfigured(payload, "redirection_url", _options.RedirectionUrl);

            if (cardEnrollment)
                payload["subscription_plan_id"] = input.SubscriptionPlanId!.Value;

            using HttpRequestMessage request = new(HttpMethod.Post, $"{_options.BaseUrl}/v1/intention/");
            request.Headers.TryAddWithoutValidation("Authorization", $"Token {_options.SecretKey}");
            request.Content = JsonContent.Create(payload);
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string reason = DescribeUpstreamReason(body);

                return Result<PaymobPaymentIntent>.Failure(
                    new Error("Paymob.GatewayError",
                        $"Paymob intention request failed with status {(int)response.StatusCode}."
                        + (reason.Length == 0 ? string.Empty : $" upstream: {reason}")));
            }

            using JsonDocument document = JsonDocument.Parse(body);
            string clientSecret = document.RootElement.GetProperty("client_secret").GetString() ?? string.Empty;
            string intentionOrderId = document.RootElement.GetProperty("intention_order_id").GetRawText();
            return Result<PaymobPaymentIntent>.Success(
                new PaymobPaymentIntent(
                    input.MerchantReference,
                    intentionOrderId,
                    clientSecret,
                    _options.PublicKey,
                    RecurringRenewalSupported: cardEnrollment));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A transport timeout is not a provider decline and must not escape
            // as an unhandled 500. Caller cancellation remains a caller
            // cancellation and is rethrown.
            return Result<PaymobPaymentIntent>.Failure(
                new Error("Paymob.GatewayError", "The payment gateway did not respond in time."));
        }
        catch (Exception exception) when (exception is JsonException or HttpRequestException or KeyNotFoundException)
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
                // A 4xx response is an explicit rejection. A 5xx response can
                // follow a successful refund whose response was lost, so keep
                // that outcome distinct and do not make it retryable.
                string errorCode = (int)response.StatusCode is >= 400 and < 500
                    ? "Paymob.RefundRejected"
                    : "Paymob.RefundOutcomeUnknown";
                return Result<string?>.Failure(
                    new Error(errorCode,
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
                new Error("Paymob.RefundOutcomeUnknown", "The refund outcome could not be confirmed."));
        }
    }

    /// <summary>
    /// Authenticated read-only readback of a single transaction
    /// (<c>GET /api/acceptance/transactions/{id}</c>). The refund API's 2xx
    /// carries only a refund <c>id</c>, which does not prove a settled refund, so
    /// this lets a caller verify the exact refund against the provider before
    /// treating it as settled. The merchant auth token is exchanged from the API
    /// key (the same credentials the subscription-management namespace accepts;
    /// the secret key is rejected there), and no credential ever reaches a URL or
    /// the log. A missing scalar field is surfaced as <c>null</c>, and a response
    /// without the required identity/outcome is refused — never guessed.
    /// </summary>
    public async Task<Result<PaymobRefundVerification>> VerifyRefundTransactionAsync(
        string refundTransactionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return Result<PaymobRefundVerification>.Failure(
                new Error("Paymob.NotConfigured", "The payment gateway is not configured."));
        }

        if (string.IsNullOrWhiteSpace(refundTransactionId))
        {
            return Result<PaymobRefundVerification>.Failure(
                new Error("Paymob.VerificationUnavailable", "No refund transaction was supplied to verify."));
        }

        try
        {
            string? authToken = await GetSubscriptionAuthTokenAsync(cancellationToken);
            if (authToken is null)
            {
                return Result<PaymobRefundVerification>.Failure(
                    new Error("Paymob.VerificationUnavailable", "The refund could not be verified with the payment gateway."));
            }

            HttpClient httpClient = _httpClientFactory.CreateClient("Paymob");

            using HttpRequestMessage request = new(
                HttpMethod.Get,
                $"{_options.BaseUrl}/api/acceptance/transactions/{Uri.EscapeDataString(refundTransactionId.Trim())}");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {authToken}");

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Result<PaymobRefundVerification>.Failure(
                    new Error("Paymob.VerificationUnavailable",
                        $"Paymob refund verification failed with status {(int)response.StatusCode}."));
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!TryParseJson(body, out JsonElement root) || root.ValueKind != JsonValueKind.Object)
            {
                return Result<PaymobRefundVerification>.Failure(
                    new Error("Paymob.VerificationUnavailable", "The refund verification response could not be read."));
            }

            // Refuse an incomplete readback rather than infer a settled refund
            // from absent fields: identity and outcome must all be present.
            if (!TryReadLong(root, "id", out long transactionId)
                || !TryReadBool(root, "is_refund", out bool isRefund)
                || !TryReadBool(root, "success", out bool success)
                || !TryReadBool(root, "pending", out bool pending))
            {
                return Result<PaymobRefundVerification>.Failure(
                    new Error("Paymob.VerificationUnavailable", "The refund verification response was incomplete."));
            }

            return Result<PaymobRefundVerification>.Success(new PaymobRefundVerification(
                transactionId,
                isRefund,
                success,
                pending,
                TryReadLong(root, "amount_cents", out long amountCents) ? amountCents : null,
                root.TryGetProperty("currency", out JsonElement currency) && currency.ValueKind == JsonValueKind.String
                    ? currency.GetString()
                    : null,
                root.TryGetProperty("order", out JsonElement order) && order.ValueKind == JsonValueKind.Object
                    && TryReadLong(order, "id", out long orderId)
                        ? orderId
                        : null,
                ReadParentTransactionId(root)));
        }
        catch (Exception exception) when (exception is JsonException or HttpRequestException)
        {
            return Result<PaymobRefundVerification>.Failure(
                new Error("Paymob.VerificationUnavailable", "The refund could not be verified with the payment gateway."));
        }
    }

    private static long? ReadParentTransactionId(JsonElement root)
    {
        if (!root.TryGetProperty("parent_transaction", out JsonElement parent))
        {
            return null;
        }

        return parent.ValueKind switch
        {
            JsonValueKind.Number when parent.TryGetInt64(out long value) => value,
            JsonValueKind.Object when TryReadLong(parent, "id", out long nested) => nested,
            _ => null
        };
    }

    private static bool TryReadLong(JsonElement element, string propertyName, out long value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt64(out value);
    }

    private static bool TryReadBool(JsonElement element, string propertyName, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.True) { value = true; return true; }
        if (property.ValueKind == JsonValueKind.False) { return true; }
        return false;
    }

    public async Task<Result> UpdateSubscriptionAmountAsync(string providerSubscriptionId, decimal targetRecurringGross, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey)) return Result.Failure(new Error("Paymob.NotConfigured", "The payment gateway is not configured."));
        if (string.IsNullOrWhiteSpace(providerSubscriptionId) || targetRecurringGross < 0m) return Result.Failure(new Error("Paymob.InvalidSubscription", "The provider subscription update is invalid."));
        try
        {
            // The subscription management namespace authenticates with a merchant
            // auth token derived from the API key. It rejects the secret key
            // outright (401 "credentials were not provided"), so using the secret
            // key here made every plan-change amount sync fail.
            string? authToken = await GetSubscriptionAuthTokenAsync(cancellationToken);
            if (authToken is null) return Result.Failure(new Error("Paymob.GatewayError", "Unexpected payment gateway response."));

            HttpClient client = _httpClientFactory.CreateClient("Paymob");
            using var request = new HttpRequestMessage(HttpMethod.Put, $"{_options.BaseUrl}/api/acceptance/subscriptions/{Uri.EscapeDataString(providerSubscriptionId.Trim())}");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {authToken}");
            request.Content = JsonContent.Create(new { amount_cents = ToCents(targetRecurringGross) });
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode ? Result.Success() : Result.Failure(new Error("Paymob.GatewayError", $"Paymob subscription update failed with status {(int)response.StatusCode}."));
        }
        catch (HttpRequestException) { return Result.Failure(new Error("Paymob.GatewayError", "Unexpected payment gateway response.")); }
    }

    /// <summary>
    /// Exchanges the merchant API key for the auth token the subscription
    /// management API accepts, caching it until shortly before expiry. Returns
    /// null when no key is configured or the exchange fails, so callers fail
    /// closed instead of sending a request that cannot be authorised.
    /// </summary>
    private async Task<string?> GetSubscriptionAuthTokenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey)) return null;

        string? cached = _authToken;
        if (cached is not null && DateTimeOffset.UtcNow < _authTokenExpiresUtc) return cached;

        await _authTokenGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = _authToken;
            if (cached is not null && DateTimeOffset.UtcNow < _authTokenExpiresUtc) return cached;

            HttpClient client = _httpClientFactory.CreateClient("Paymob");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/api/auth/tokens");
            request.Content = JsonContent.Create(new { api_key = _options.ApiKey });
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!TryParseJson(body, out JsonElement root)
                || !root.TryGetProperty("token", out JsonElement token)
                || token.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(token.GetString()))
            {
                return null;
            }

            _authToken = token.GetString();
            _authTokenExpiresUtc = DateTimeOffset.UtcNow.Add(AuthTokenLifetime);
            return _authToken;
        }
        catch (Exception exception) when (exception is JsonException or HttpRequestException)
        {
            return null;
        }
        finally
        {
            _authTokenGate.Release();
        }
    }

    /// <summary>
    /// Turns a non-success intention response body into a bounded, sanitized
    /// operator- and client-facing reason (the surfaced <c>Paymob.GatewayError</c>
    /// message reaches mobile clients in problem-detail <c>detail</c>, so it is
    /// sanitized to the stricter client bar). The upstream provider text is only
    /// ever surfaced through this allowlisted path: a body that is not JSON,
    /// contains no recognized reason field, or would echo a credential or
    /// billing-identifying value contributes nothing and the caller keeps its
    /// status-only description.
    /// </summary>
    /// <summary>
    /// Adds an optional URL field only when it is actually configured. Paymob
    /// validates these fields with "This field may not be null." and rejects the
    /// whole intention, so sending an explicit null breaks every intent on an
    /// environment that has not set the URL.
    /// </summary>
    private static void AddUrlIfConfigured(
        Dictionary<string, object?> payload,
        string fieldName,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            payload[fieldName] = value;
        }
    }

    private static string DescribeUpstreamReason(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        if (!TryParseJson(body, out JsonElement root) || root.ValueKind != JsonValueKind.Object)
        {
            // Non-JSON or non-object payloads are not echoed: they are exactly
            // the shapes most likely to embed a credential or stray markup.
            return string.Empty;
        }

        foreach (string fieldName in ReasonFieldNames)
        {
            if (!root.TryGetProperty(fieldName, out JsonElement element))
            {
                continue;
            }

            string? value = element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.ToString(),
                _ => null
            };

            string sanitized = SanitizeReason(value);

            if (sanitized.Length > 0)
            {
                return sanitized;
            }
        }

        // Paymob answers field-level validation failures with a DRF-style map
        // instead of detail/message, e.g. {"redirection_url": ["This field may
        // not be null."]}. Status-only errors hid that entirely, so surface the
        // field name plus its first message — but only for request fields we
        // send, and only through the same sanitizer, since a message may still
        // quote provider-side values.
        foreach (string fieldName in FieldErrorNames)
        {
            if (!root.TryGetProperty(fieldName, out JsonElement element))
            {
                continue;
            }

            string? first = element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Array when element.GetArrayLength() > 0
                    && element[0].ValueKind == JsonValueKind.String => element[0].GetString(),
                _ => null
            };

            string sanitized = SanitizeReason(first);

            if (sanitized.Length > 0)
            {
                return $"{fieldName}: {sanitized}";
            }
        }

        return string.Empty;
    }

    private static bool TryParseJson(string body, out JsonElement root)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            root = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            root = default;
            return false;
        }
    }

    /// <summary>
    /// Conservative allowlist over a candidate reason: refuse anything that looks
    /// like a credential, then collapse and bound the remaining text. Only ever
    /// applied to the scalar reason fields, never to the whole provider body.
    /// </summary>
    private static string SanitizeReason(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();

        if (LooksSensitive(trimmed))
        {
            return string.Empty;
        }

        string flattened = FlattenControlCharacters(trimmed);

        if (flattened.Length <= MaxUpstreamReasonLength)
        {
            return flattened;
        }

        string bounded = flattened[..MaxUpstreamReasonLength];

        // Never end on a split surrogate pair: a lone high surrogate would
        // serialize as a replacement character downstream.
        if (char.IsHighSurrogate(bounded[^1]))
        {
            bounded = bounded[..^1];
        }

        return bounded;
    }

    /// <summary>
    /// True when the text carries the shape of a credential — a <c>key=value</c>
    /// pair, a JSON field, a bearer/jwt token (with or without a separator
    /// space), a JWT-like payload, an email-like or phone-like value — or when
    /// a Paymob secret/token prefix appears anywhere. The check is deliberately
    /// textual and over-inclusive: dropping a legitimate reason is acceptable,
    /// echoing a credential or any billing-identifying value is not.
    /// </summary>
    private static bool LooksSensitive(string value)
    {
        // "bearer" with or without a following space, so "Bearer:x" is caught.
        if (value.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || value.Contains("bearer", StringComparison.OrdinalIgnoreCase)
            || value.Contains("sk_", StringComparison.Ordinal)
            || value.Contains("pk_", StringComparison.Ordinal))
        {
            return true;
        }

        string[] fragments = value.Split(
            [' ', '\t', '\r', '\n', ':', '=', '"', '\'', ',', ';', '{', '}', '[', ']', '(', ')'],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string fragment in fragments)
        {
            string candidate = fragment.TrimStart('"', '\'');

            // JWT-shaped payloads ("eyJ…" base64url headers) are always
            // credential material, whatever the surrounding identifier says.
            if (candidate.StartsWith("eyJ", StringComparison.Ordinal))
            {
                return true;
            }

            // Email-shaped and phone-shaped fragments are billing-identifying
            // values, not rejection reasons.
            if (candidate.Contains('@') && candidate.Contains('.'))
            {
                return true;
            }

            if (candidate.Count(char.IsDigit) >= 9)
            {
                return true;
            }

            foreach (string sensitive in SensitiveFieldNames)
            {
                if (candidate.StartsWith(sensitive, StringComparison.OrdinalIgnoreCase)
                    && candidate.Length > sensitive.Length)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Replaces control characters (newlines, tabs) with spaces so the
    /// reason stays a single readable line, then collapses runs of whitespace.</summary>
    private static string FlattenControlCharacters(string value)
    {
        var builder = new StringBuilder(value.Length);
        bool previousWasSpace = false;

        foreach (char character in value)
        {
            bool isControl = char.IsControl(character);

            if (isControl || char.IsWhiteSpace(character))
            {
                if (previousWasSpace)
                {
                    continue;
                }

                builder.Append(' ');
                previousWasSpace = true;
                continue;
            }

            builder.Append(character);
            previousWasSpace = false;
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Parses a configured Paymob integration id as a positive invariant
    /// integer. Paymob's intention payload accepts numeric integration ids
    /// only; the configured text is never embedded in an error message, so a
    /// malformed value cannot leak through diagnostics.
    /// </summary>
    private static bool TryParseIntegrationId(string? configuredValue, out long integrationId)
    {
        integrationId = 0;

        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return false;
        }

        return long.TryParse(
                configuredValue.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out integrationId)
            && integrationId > 0;
    }

    private static long ToCents(decimal amount)
    {
        return (long)decimal.Round(amount * 100m, 0, MidpointRounding.ToEven);
    }
}
