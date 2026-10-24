using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Families.Infrastructure.Payments;

namespace Sanad.UnitTests.Families;

/// <summary>
/// W4 payment-method selection and failure surface for the Paymob intention
/// calls, with the subscription wallet path as the focus (R5/R6).
/// <para>
/// Pinned behaviour:
/// <list type="bullet">
/// <item>wallet uses <c>WalletIntegrationId</c> and never sends
/// <c>subscription_plan_id</c> or opts into recurring renewal;</item>
/// <item>card without a plan id uses <c>CardIntegrationId</c> and is not a
/// card-enrollment intention;</item>
/// <item>card with a plan id uses <c>Card3dsIntegrationId</c>, sends the plan id
/// and reports recurring-renewal support;</item>
/// <item>a malformed/empty/non-positive configured integration id is refused
/// before any HTTP call as the existing safe <c>Paymob.MethodNotAvailable</c>,
/// without echoing the configured value;</item>
/// <item>a transport timeout is a safe <c>Paymob.GatewayError</c>, a caller
/// cancellation is rethrown, and a decline or malformed/truncated upstream body
/// stays a bounded, credential-free gateway error.</item>
/// </list>
/// </para>
/// <para>NOT RUN: authored for the W4 gate; the Mastermind runs it.</para>
/// </summary>
public sealed class PaymobSubscriptionWalletPathTests
{
    private const string ProviderSecret = "sk_test_wallet_path_secret";
    private const string WalletIntegrationId = "424242";
    private const string CardIntegrationId = "111111";
    private const string Card3dsIntegrationId = "987654";
    private const string MerchantReference = "sub_wallet_path";

    [Fact]
    public async Task Wallet_uses_the_wallet_integration_and_never_enrolls_a_card_plan()
    {
        var handler = StubHandler.Success();

        var result = await Client(handler).CreateSubscriptionPaymentIntentAsync(
            Input(SubscriptionPaymentMethod.Wallet));

        Assert.True(result.IsSuccess);
        Assert.Equal(MerchantReference, result.Value.PaymobOrderId);
        Assert.False(result.Value.RecurringRenewalSupported);

        using JsonDocument payload = Parse(handler.Body);
        Assert.Equal(new long[] { 424242 }, PaymentMethods(payload));
        Assert.False(payload.RootElement.TryGetProperty("subscription_plan_id", out _));
    }

    [Fact]
    public async Task Wallet_ignores_a_supplied_plan_id_and_stays_non_recurring()
    {
        // The plan id is only meaningful for a card enrollment. A wallet
        // intention must not silently become a recurring subscription.
        var handler = StubHandler.Success();

        var result = await Client(handler).CreateSubscriptionPaymentIntentAsync(
            Input(SubscriptionPaymentMethod.Wallet, subscriptionPlanId: 6755));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.RecurringRenewalSupported);

        using JsonDocument payload = Parse(handler.Body);
        Assert.False(payload.RootElement.TryGetProperty("subscription_plan_id", out _));
        Assert.Equal(new long[] { 424242 }, PaymentMethods(payload));
    }

    [Fact]
    public async Task Card_without_a_plan_uses_the_plain_card_integration()
    {
        var handler = StubHandler.Success();

        var result = await Client(handler).CreateSubscriptionPaymentIntentAsync(
            Input(SubscriptionPaymentMethod.Card));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.RecurringRenewalSupported);

        using JsonDocument payload = Parse(handler.Body);
        Assert.Equal(new long[] { 111111 }, PaymentMethods(payload));
        Assert.False(payload.RootElement.TryGetProperty("subscription_plan_id", out _));
    }

    [Fact]
    public async Task Card_enrollment_uses_the_3ds_integration_and_sends_the_plan()
    {
        var handler = StubHandler.Success();

        var result = await Client(handler).CreateSubscriptionPaymentIntentAsync(
            Input(SubscriptionPaymentMethod.Card, subscriptionPlanId: 6755));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.RecurringRenewalSupported);

        using JsonDocument payload = Parse(handler.Body);
        Assert.Equal(new long[] { 987654 }, PaymentMethods(payload));
        Assert.Equal(6755, payload.RootElement.GetProperty("subscription_plan_id").GetInt32());
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("12.5")]
    [InlineData("0")]
    [InlineData("-424242")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Malformed_wallet_integration_id_is_refused_before_any_http_call(
        string configuredIntegrationId)
    {
        var handler = StubHandler.Success();

        var result = await Client(handler, walletIntegrationId: configuredIntegrationId)
            .CreateSubscriptionPaymentIntentAsync(Input(SubscriptionPaymentMethod.Wallet));

        Assert.False(result.IsSuccess);
        Assert.Equal("Paymob.MethodNotAvailable", result.Error.Code);
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-987654")]
    public async Task Malformed_card_integration_ids_are_refused_before_any_http_call(
        string configuredIntegrationId)
    {
        var handler = StubHandler.Success();

        PaymobClient client = Client(
            handler, cardIntegrationId: configuredIntegrationId, card3dsIntegrationId: configuredIntegrationId);

        var plainCard = await client.CreateSubscriptionPaymentIntentAsync(
            Input(SubscriptionPaymentMethod.Card));
        var enrollment = await client.CreateSubscriptionPaymentIntentAsync(
            Input(SubscriptionPaymentMethod.Card, subscriptionPlanId: 6755));

        Assert.Equal("Paymob.MethodNotAvailable", plainCard.Error.Code);
        Assert.Equal("Paymob.MethodNotAvailable", enrollment.Error.Code);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Malformed_integration_id_is_never_echoed_in_the_error()
    {
        const string leakyValue = "sk_live_should_not_leak";
        var handler = StubHandler.Success();

        var result = await Client(handler, walletIntegrationId: leakyValue)
            .CreateSubscriptionPaymentIntentAsync(Input(SubscriptionPaymentMethod.Wallet));

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain(leakyValue, result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sk_live", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transport_timeout_on_the_subscription_intention_is_a_safe_gateway_error()
    {
        var handler = StubHandler.Throws(new TaskCanceledException("simulated transport timeout"));

        var result = await Client(handler).CreateSubscriptionPaymentIntentAsync(
            Input(SubscriptionPaymentMethod.Wallet));

        Assert.False(result.IsSuccess);
        Assert.Equal("Paymob.GatewayError", result.Error.Code);
        Assert.Equal("The payment gateway did not respond in time.", result.Error.Message);
    }

    [Fact]
    public async Task Transport_timeout_on_the_booking_intention_is_a_safe_gateway_error()
    {
        var handler = StubHandler.Throws(new TaskCanceledException("simulated transport timeout"));

        var result = await Client(handler).CreatePaymentIntentAsync(BookingInput());

        Assert.False(result.IsSuccess);
        Assert.Equal("Paymob.GatewayError", result.Error.Code);
        Assert.Equal("The payment gateway did not respond in time.", result.Error.Message);
    }

    [Fact]
    public async Task Caller_cancellation_is_rethrown_and_not_turned_into_a_gateway_error()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var handler = StubHandler.Throws(new OperationCanceledException(cancellation.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Client(handler).CreateSubscriptionPaymentIntentAsync(
                Input(SubscriptionPaymentMethod.Wallet), cancellation.Token));
    }

    [Fact]
    public async Task Provider_decline_keeps_the_bounded_sanitized_reason()
    {
        var handler = StubHandler.WithBody(
            HttpStatusCode.PaymentRequired,
            """{"detail":"Wallet number is not registered for this merchant"}""");

        var result = await Client(handler).CreateSubscriptionPaymentIntentAsync(
            Input(SubscriptionPaymentMethod.Wallet));

        Assert.False(result.IsSuccess);
        Assert.Equal("Paymob.GatewayError", result.Error.Code);
        Assert.Equal(
            "Paymob intention request failed with status 402. upstream: Wallet number is not registered for this merchant",
            result.Error.Message);
    }

    [Fact]
    public async Task Provider_decline_never_echoes_a_credential()
    {
        var handler = StubHandler.WithBody(
            HttpStatusCode.Unauthorized,
            $$"""{"detail":"Rejected with token={{ProviderSecret}}"}""");

        var result = await Client(handler).CreateSubscriptionPaymentIntentAsync(
            Input(SubscriptionPaymentMethod.Wallet));

        Assert.False(result.IsSuccess);
        Assert.Equal("Paymob.GatewayError", result.Error.Code);
        Assert.Equal("Paymob intention request failed with status 401.", result.Error.Message);
        Assert.DoesNotContain(ProviderSecret, result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_success_body_is_a_safe_gateway_error()
    {
        var handler = StubHandler.WithBody(HttpStatusCode.OK, "<html>not json</html>");

        var result = await Client(handler).CreateSubscriptionPaymentIntentAsync(
            Input(SubscriptionPaymentMethod.Wallet));

        Assert.False(result.IsSuccess);
        Assert.Equal("Paymob.GatewayError", result.Error.Code);
        Assert.Equal("Unexpected payment gateway response.", result.Error.Message);
    }

    [Fact]
    public async Task Truncated_success_body_is_a_safe_gateway_error()
    {
        // Valid JSON, but the required intention_order_id is absent. This must
        // not escape as an unhandled 500.
        var handler = StubHandler.WithBody(
            HttpStatusCode.OK,
            """{"client_secret":"client_secret_value"}""");

        var result = await Client(handler).CreateSubscriptionPaymentIntentAsync(
            Input(SubscriptionPaymentMethod.Wallet));

        Assert.False(result.IsSuccess);
        Assert.Equal("Paymob.GatewayError", result.Error.Code);
        Assert.Equal("Unexpected payment gateway response.", result.Error.Message);
    }

    private static PaymobClient Client(
        StubHandler handler,
        string walletIntegrationId = WalletIntegrationId,
        string cardIntegrationId = CardIntegrationId,
        string card3dsIntegrationId = Card3dsIntegrationId) =>
        new(
            new SingleHttpClientFactory(new HttpClient(handler)),
            Options.Create(new PaymobOptions
            {
                SecretKey = ProviderSecret,
                PublicKey = "pk_test_wallet_path",
                CardIntegrationId = cardIntegrationId,
                Card3dsIntegrationId = card3dsIntegrationId,
                WalletIntegrationId = walletIntegrationId
            }));

    private static JsonDocument Parse(string? body)
    {
        Assert.False(string.IsNullOrWhiteSpace(body));
        return JsonDocument.Parse(body!);
    }

    private static long[] PaymentMethods(JsonDocument payload) =>
        payload.RootElement.GetProperty("payment_methods")
            .EnumerateArray()
            .Select(element => element.GetInt64())
            .ToArray();

    private static PaymobSubscriptionPaymentIntentInput Input(
        SubscriptionPaymentMethod method,
        int? subscriptionPlanId = null) =>
        new(
            MerchantReference,
            method,
            299m,
            "EGP",
            new PaymobBillingData("Ahmed", "Ali", "wallet@test.local", "+201000000000"),
            subscriptionPlanId);

    private static PaymobPaymentIntentInput BookingInput() =>
        new(
            BookingId.New(),
            PaymentMethod.Wallet,
            299m,
            "EGP",
            new PaymobBillingData("Ahmed", "Ali", "wallet@test.local", "+201000000000"));

    private sealed class SingleHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode? _statusCode;
        private readonly string? _responseBody;
        private readonly Exception? _exception;

        private StubHandler(HttpStatusCode? statusCode, string? responseBody, Exception? exception)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
            _exception = exception;
        }

        public string? Body { get; private set; }

        public int CallCount { get; private set; }

        public static StubHandler Success() =>
            new(
                HttpStatusCode.Created,
                """{"client_secret":"client_secret_value","intention_order_id":123456}""",
                exception: null);

        public static StubHandler WithBody(HttpStatusCode statusCode, string body) =>
            new(statusCode, body, exception: null);

        public static StubHandler Throws(Exception exception) =>
            new(statusCode: null, responseBody: null, exception);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;

            if (_exception is not null)
            {
                throw _exception;
            }

            Body = await request.Content!.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_statusCode!.Value)
            {
                Content = new StringContent(_responseBody!, Encoding.UTF8, "application/json")
            };
        }
    }
}
