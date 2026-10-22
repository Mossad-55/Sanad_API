using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Families.Infrastructure.Payments;

namespace Sanad.UnitTests.Families;

/// <summary>
/// The Paymob intention paths surface the provider's rejection reason to
/// operators, but only through the bounded sanitizer in
/// <see cref="PaymobClient"/>: no credential, and nothing beyond the agreed
/// reason fields, may reach the error description.
/// </summary>
public sealed class PaymobIntentionErrorSurfaceTests
{
    private const string GatewayFailurePrefix = "Paymob intention request failed with status 400.";

    // Billing values that must never appear in an operator-visible error text.
    private const string BillingEmail = "ahmed@example.com";
    private const string BillingPhone = "+201012345678";
    private const string ProviderSecret = "sk_test_secret";

    [Fact]
    public async Task Subscription_intention_surfaces_the_sanitized_provider_reason()
    {
        var handler = StubHandler.WithBody(
            HttpStatusCode.BadRequest,
            """"{"statusCode":400,"detail":"Currency not supported for this integration"}"""");

        var result = await SubscriptionClient(handler).CreateSubscriptionPaymentIntentAsync(
            SubscriptionInput(SubscriptionPaymentMethod.Wallet));

        Assert.False(result.IsSuccess);
        Assert.Equal("Paymob.GatewayError", result.Error.Code);
        Assert.Equal(
            $"{GatewayFailurePrefix} upstream: Currency not supported for this integration",
            result.Error.Message);
    }

    [Fact]
    public async Task Booking_intention_surfaces_the_sanitized_provider_reason()
    {
        var handler = StubHandler.WithBody(
            HttpStatusCode.BadRequest,
            """"{"statusCode":400,"detail":"Currency not supported for this integration"}"""");

        var result = await BookingClient(handler).CreatePaymentIntentAsync(BookingInput());

        Assert.False(result.IsSuccess);
        Assert.Equal("Paymob.GatewayError", result.Error.Code);
        Assert.Equal(
            $"{GatewayFailurePrefix} upstream: Currency not supported for this integration",
            result.Error.Message);
    }

    [Fact]
    public async Task Subscription_intention_prefers_detail_over_status_when_both_are_present()
    {
        var handler = StubHandler.WithBody(
            HttpStatusCode.BadRequest,
            """"{"statusCode":400,"detail":"Invalid billing data","message":"Request failed"}"""");

        var result = await SubscriptionClient(handler).CreateSubscriptionPaymentIntentAsync(
            SubscriptionInput(SubscriptionPaymentMethod.Wallet));

        Assert.False(result.IsSuccess);
        Assert.Equal($"{GatewayFailurePrefix} upstream: Invalid billing data", result.Error.Message);
    }

    [Fact]
    public async Task Subscription_intention_falls_back_to_the_message_field()
    {
        var handler = StubHandler.WithBody(
            HttpStatusCode.BadRequest,
            """"{"message":"Merchant account is not allowed for this payment method"}"""");

        var result = await SubscriptionClient(handler).CreateSubscriptionPaymentIntentAsync(
            SubscriptionInput(SubscriptionPaymentMethod.Wallet));

        Assert.False(result.IsSuccess);
        Assert.Equal(
            $"{GatewayFailurePrefix} upstream: Merchant account is not allowed for this payment method",
            result.Error.Message);
    }

    [Fact]
    public async Task Subscription_intention_keeps_the_status_only_text_for_a_non_json_body()
    {
        var handler = StubHandler.WithBody(
            HttpStatusCode.BadRequest,
            "<html><body>400 Bad Request</body></html>");

        var result = await SubscriptionClient(handler).CreateSubscriptionPaymentIntentAsync(
            SubscriptionInput(SubscriptionPaymentMethod.Wallet));

        Assert.False(result.IsSuccess);
        Assert.Equal(GatewayFailurePrefix, result.Error.Message);
        Assert.DoesNotContain("upstream:", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Booking_intention_keeps_the_status_only_text_for_a_non_json_body()
    {
        var handler = StubHandler.WithBody(HttpStatusCode.BadRequest, "not json at all");

        var result = await BookingClient(handler).CreatePaymentIntentAsync(BookingInput());

        Assert.False(result.IsSuccess);
        Assert.Equal(GatewayFailurePrefix, result.Error.Message);
    }

    [Fact]
    public async Task Subscription_intention_keeps_the_status_only_text_when_json_carries_no_reason()
    {
        var handler = StubHandler.WithBody(
            HttpStatusCode.BadRequest,
            """"{"statusCode":400,"error":"Bad Request"}"""");

        var result = await SubscriptionClient(handler).CreateSubscriptionPaymentIntentAsync(
            SubscriptionInput(SubscriptionPaymentMethod.Wallet));

        Assert.False(result.IsSuccess);
        Assert.Equal(GatewayFailurePrefix, result.Error.Message);
    }

    [Fact]
    public async Task Subscription_intention_keeps_the_status_only_text_for_an_empty_body()
    {
        var handler = StubHandler.WithBody(HttpStatusCode.BadGateway, string.Empty);

        var result = await SubscriptionClient(handler).CreateSubscriptionPaymentIntentAsync(
            SubscriptionInput(SubscriptionPaymentMethod.Wallet));

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "Paymob intention request failed with status 502.", result.Error.Message);
    }

    [Fact]
    public async Task Subscription_intention_drops_a_reason_that_echoes_a_credential()
    {
        var handler = StubHandler.WithBody(
            HttpStatusCode.BadRequest,
            $$""""{"statusCode":400,"detail":"Rejected for token={{ProviderSecret}}"}"""");

        var result = await SubscriptionClient(handler).CreateSubscriptionPaymentIntentAsync(
            SubscriptionInput(SubscriptionPaymentMethod.Wallet));

        Assert.False(result.IsSuccess);
        Assert.Equal(GatewayFailurePrefix, result.Error.Message);
        Assert.DoesNotContain("secret", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ProviderSecret, result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Booking_intention_drops_a_reason_that_echoes_an_authorization_header()
    {
        var handler = StubHandler.WithBody(
            HttpStatusCode.Unauthorized,
            $$""""{"detail":"Authorization: Bearer {{ProviderSecret}}"}"""");

        var result = await BookingClient(handler).CreatePaymentIntentAsync(BookingInput());

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "Paymob intention request failed with status 401.", result.Error.Message);
        Assert.DoesNotContain(ProviderSecret, result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("bearer", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Subscription_intention_bounds_a_very_long_reason()
    {
        const int overLongReasonLength = 2000;
        const int expectedBoundedLength = 300;
        string longReason = new('x', overLongReasonLength);
        var handler = StubHandler.WithBody(
            HttpStatusCode.BadRequest,
            $$""""{"statusCode":400,"detail":"{{longReason}}"}"""");

        var result = await SubscriptionClient(handler).CreateSubscriptionPaymentIntentAsync(
            SubscriptionInput(SubscriptionPaymentMethod.Wallet));

        Assert.False(result.IsSuccess);
        Assert.Equal(
            $"{GatewayFailurePrefix} upstream: {new string('x', expectedBoundedLength)}",
            result.Error.Message);
    }

    [Fact]
    public async Task Subscription_intention_drops_a_reason_that_carries_billing_values()
    {
        var handler = StubHandler.WithBody(
            HttpStatusCode.BadRequest,
            """"{"statusCode":400,"detail":"Invalid billing email ahmed@example.com for phone +201012345678"}"""");

        var result = await SubscriptionClient(handler).CreateSubscriptionPaymentIntentAsync(
            SubscriptionInput(SubscriptionPaymentMethod.Wallet));

        Assert.False(result.IsSuccess);
        Assert.Equal(GatewayFailurePrefix, result.Error.Message);
        Assert.DoesNotContain(BillingEmail, result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(BillingPhone, result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Subscription_intention_drops_a_reason_that_embeds_a_jwt_shaped_token()
    {
        const string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJVadQssw5c";
        var handler = StubHandler.WithBody(
            HttpStatusCode.BadRequest,
            $$""""{"statusCode":400,"detail":"AccessToken: {{jwt}}"}"""");

        var result = await SubscriptionClient(handler).CreateSubscriptionPaymentIntentAsync(
            SubscriptionInput(SubscriptionPaymentMethod.Wallet));

        Assert.False(result.IsSuccess);
        Assert.Equal(GatewayFailurePrefix, result.Error.Message);
        Assert.DoesNotContain(jwt, result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Subscription_intention_surfaces_a_numeric_status_reason()
    {
        var handler = StubHandler.WithBody(
            HttpStatusCode.BadRequest,
            """"{"statusCode":400,"status":402}"""");

        var result = await SubscriptionClient(handler).CreateSubscriptionPaymentIntentAsync(
            SubscriptionInput(SubscriptionPaymentMethod.Wallet));

        Assert.False(result.IsSuccess);
        Assert.Equal(
            $"{GatewayFailurePrefix} upstream: 402",
            result.Error.Message);
    }

    [Fact]
    public async Task Subscription_intention_drops_an_authentication_failure_reason()
    {
        // Documented over-refusal: the "auth" prefix rule drops this benign
        // reason too — dropping legitimate text is acceptable, echoing a
        // credential is not.
        var handler = StubHandler.WithBody(
            HttpStatusCode.BadRequest,
            """"{"statusCode":400,"detail":"authentication failed for this method"}"""");

        var result = await SubscriptionClient(handler).CreateSubscriptionPaymentIntentAsync(
            SubscriptionInput(SubscriptionPaymentMethod.Wallet));

        Assert.False(result.IsSuccess);
        Assert.Equal(GatewayFailurePrefix, result.Error.Message);
    }

    [Fact]
    public async Task Subscription_intention_success_path_is_unchanged()
    {
        var handler = StubHandler.Success();

        var result = await SubscriptionClient(handler).CreateSubscriptionPaymentIntentAsync(
            SubscriptionInput(SubscriptionPaymentMethod.Card, subscriptionPlanId: 6755));

        Assert.True(result.IsSuccess);
        Assert.Equal("sub_intent_error_surface", result.Value.PaymobOrderId);
        Assert.Equal("123456", result.Value.IntentionOrderId);
        Assert.Equal("client_secret_value", result.Value.ClientSecret);
        Assert.Equal("pk_test", result.Value.PublicKey);
        Assert.True(result.Value.RecurringRenewalSupported);
    }

    [Fact]
    public async Task Booking_intention_success_path_is_unchanged()
    {
        var handler = StubHandler.Success();

        var result = await BookingClient(handler).CreatePaymentIntentAsync(BookingInput());

        Assert.True(result.IsSuccess);
        Assert.Equal("123456", result.Value.IntentionOrderId);
        Assert.Equal("client_secret_value", result.Value.ClientSecret);
        Assert.Equal("pk_test", result.Value.PublicKey);
    }

    private static PaymobClient SubscriptionClient(StubHandler handler) =>
        new(
            new SingleHttpClientFactory(new HttpClient(handler)),
            Options.Create(new PaymobOptions
            {
                SecretKey = ProviderSecret,
                PublicKey = "pk_test",
                CardIntegrationId = "111111",
                Card3dsIntegrationId = "987654",
                WalletIntegrationId = "123456"
            }));

    private static PaymobClient BookingClient(StubHandler handler) =>
        new(
            new SingleHttpClientFactory(new HttpClient(handler)),
            Options.Create(new PaymobOptions
            {
                SecretKey = ProviderSecret,
                PublicKey = "pk_test",
                CardIntegrationId = "111111",
                WalletIntegrationId = "123456"
            }));

    private static PaymobSubscriptionPaymentIntentInput SubscriptionInput(
        SubscriptionPaymentMethod method,
        int? subscriptionPlanId = null) =>
        new(
            "sub_intent_error_surface",
            method,
            299m,
            "EGP",
            Billing(),
            subscriptionPlanId);

    private static PaymobPaymentIntentInput BookingInput() =>
        new(
            BookingId.New(),
            PaymentMethod.Card,
            299m,
            "EGP",
            Billing());

    private static PaymobBillingData Billing() =>
        new("Ahmed", "Ali", BillingEmail, BillingPhone);

    private sealed class SingleHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        public static StubHandler WithBody(HttpStatusCode statusCode, string body) =>
            new(statusCode, body);

        public static StubHandler Success() =>
            new(
                HttpStatusCode.Created,
                """"{"client_secret":"client_secret_value","intention_order_id":123456}"""");

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);

            // The declared content type is intentionally JSON for every stub,
            // including the non-JSON bodies: the client must not trust the
            // declared type, only the body it can actually parse.
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
