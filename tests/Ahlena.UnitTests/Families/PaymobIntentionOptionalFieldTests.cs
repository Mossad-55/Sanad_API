using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Abstractions.Payments;
using Ahlena.Modules.Families.Domain.Bookings;
using Ahlena.Modules.Families.Domain.Subscriptions;
using Ahlena.Modules.Families.Infrastructure.Payments;

namespace Ahlena.UnitTests.Families;

/// <summary>
/// Paymob validates optional URL fields with "This field may not be null." and
/// rejects the whole intention. The client used to always serialize
/// <c>notification_url</c>/<c>redirection_url</c>, sending an explicit null on
/// any environment that had not configured them, so every Card enrollment
/// intent failed with a bare 400. These tests pin that unconfigured URLs are
/// omitted, configured ones are still sent, and that a field-level provider
/// rejection is surfaced instead of a status-only error.
/// </summary>
public sealed class PaymobIntentionOptionalFieldTests
{
    private const string SecretKey = "egy_sk_test_secret_value";
    private const string WebhookUrl = "https://api.example.test/api/v1/payments/webhooks/paymob";
    private const string RedirectionUrl = "https://app.example.test/payment/result";

    [Fact]
    public async Task Booking_intention_omits_notification_url_when_no_webhook_is_configured()
    {
        var handler = RecordingHandler.Created();

        var result = await BookingClient(handler, webhookUrl: null).CreatePaymentIntentAsync(BookingInput());

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("notification_url", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("null", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Card_enrollment_intention_omits_redirection_url_when_it_is_not_configured()
    {
        var handler = RecordingHandler.Created();

        var result = await SubscriptionClient(handler, webhookUrl: WebhookUrl, redirectionUrl: null)
            .CreateSubscriptionPaymentIntentAsync(SubscriptionInput(subscriptionPlanId: 12863));

        Assert.True(result.IsSuccess);
        Assert.Contains("\"subscription_plan_id\":12863", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"notification_url\":\"" + WebhookUrl + "\"", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("redirection_url", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Subscription_intention_sends_both_urls_when_configured()
    {
        var handler = RecordingHandler.Created();

        var result = await SubscriptionClient(handler, WebhookUrl, RedirectionUrl)
            .CreateSubscriptionPaymentIntentAsync(SubscriptionInput(subscriptionPlanId: 12863));

        Assert.True(result.IsSuccess);
        Assert.Contains("\"notification_url\":\"" + WebhookUrl + "\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"redirection_url\":\"" + RedirectionUrl + "\"", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Field_level_provider_rejection_is_surfaced_with_its_field_name()
    {
        var handler = RecordingHandler.With(
            HttpStatusCode.BadRequest,
            """{"redirection_url": ["This field may not be null."]}""");

        var result = await SubscriptionClient(handler, WebhookUrl, RedirectionUrl)
            .CreateSubscriptionPaymentIntentAsync(SubscriptionInput(subscriptionPlanId: 12863));

        Assert.True(result.IsFailure);
        Assert.Equal("Paymob.GatewayError", result.Error.Code);
        Assert.Contains("status 400", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains(
            "redirection_url: This field may not be null.",
            result.Error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Field_level_reason_still_redacts_billing_values()
    {
        var handler = RecordingHandler.With(
            HttpStatusCode.BadRequest,
            """{"special_reference": ["order for family.owner@test.sanad.local rejected"]}""");

        var result = await SubscriptionClient(handler, WebhookUrl, RedirectionUrl)
            .CreateSubscriptionPaymentIntentAsync(SubscriptionInput(subscriptionPlanId: 12863));

        Assert.True(result.IsFailure);
        Assert.DoesNotContain("family.owner@test.sanad.local", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal("Paymob intention request failed with status 400.", result.Error.Message);
    }

    private static PaymobClient SubscriptionClient(
        RecordingHandler handler,
        string? webhookUrl,
        string? redirectionUrl) =>
        new(
            new SingleHttpClientFactory(new HttpClient(handler)),
            Options.Create(new PaymobOptions
            {
                BaseUrl = "https://accept.paymob.com",
                SecretKey = SecretKey,
                PublicKey = "pk_test",
                CardIntegrationId = "111111",
                Card3dsIntegrationId = "987654",
                WalletIntegrationId = "123456",
                WebhookUrl = webhookUrl ?? string.Empty,
                RedirectionUrl = redirectionUrl ?? string.Empty
            }));

    private static PaymobClient BookingClient(RecordingHandler handler, string? webhookUrl) =>
        new(
            new SingleHttpClientFactory(new HttpClient(handler)),
            Options.Create(new PaymobOptions
            {
                BaseUrl = "https://accept.paymob.com",
                SecretKey = SecretKey,
                PublicKey = "pk_test",
                CardIntegrationId = "111111",
                WalletIntegrationId = "123456",
                WebhookUrl = webhookUrl ?? string.Empty
            }));

    private static PaymobSubscriptionPaymentIntentInput SubscriptionInput(int? subscriptionPlanId) =>
        new(
            "sub_intent_optional_fields",
            SubscriptionPaymentMethod.Card,
            57.50m,
            "EGP",
            new PaymobBillingData("Hasnaa", "Rehearsal", "family.owner@example.test", "+201000000001"),
            subscriptionPlanId);

    private static PaymobPaymentIntentInput BookingInput() =>
        new(
            new BookingId(Guid.Parse("01a0b6f0-6a38-7252-bbe4-b35fdb201d04")),
            PaymentMethod.Card,
            299m,
            "EGP",
            new PaymobBillingData("Hasnaa", "Rehearsal", "family.owner@example.test", "+201000000001"),
            "booking_reference");

    private sealed class SingleHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public string Body { get; private set; } = string.Empty;

        public static RecordingHandler Created() =>
            new(
                HttpStatusCode.Created,
                """{"client_secret":"client_secret_value","intention_order_id":638954301}""");

        public static RecordingHandler With(HttpStatusCode statusCode, string body) =>
            new(statusCode, body);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
