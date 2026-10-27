using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Sanad.Modules.Families.Infrastructure.Payments;
using Xunit;

namespace Sanad.UnitTests.Families;

/// <summary>
/// The care-home refund processor verifies a settled refund by reading the exact
/// returned transaction back from Paymob over the merchant channel
/// (<c>GET /api/acceptance/transactions/{id}</c>). These tests pin that HTTP
/// contract: the auth token is exchanged from the API key (never the secret
/// key), the transaction is read at the exact id, the documented outcome fields
/// are parsed — including the real, already-known transaction shape — and any
/// refusal / incomplete response fails closed rather than fabricating a settled
/// refund.
/// </summary>
public sealed class PaymobRefundTransactionReadbackTests
{
    private const string ApiKey = "api_key_test_value";
    private const string SecretKey = "egy_sk_test_secret_value";
    private const string AuthToken = "auth_token_value";

    // The proven session/refund fixture ids (no secret material).
    private const long RefundId = 561442898;
    private const long CaptureId = 561442756;
    private const long OrderId = 640412142;
    private const long AmountCents = 1_150_000;

    private static PaymobOptions ConfiguredOptions(string apiKey = ApiKey) => new()
    {
        BaseUrl = "https://accept.paymob.com",
        SecretKey = SecretKey,
        ApiKey = apiKey,
        PublicKey = "pk_test"
    };

    private static PaymobClient Client(RecordingHandler handler, string apiKey = ApiKey) =>
        new(new SingleHttpClientFactory(new HttpClient(handler)), Options.Create(ConfiguredOptions(apiKey)));

    [Fact]
    public async Task Readback_authenticates_with_the_api_key_token_and_reads_the_exact_transaction()
    {
        var handler = new RecordingHandler { TransactionBody = SettledRefundJson() };
        var client = Client(handler);

        var result = await client.VerifyRefundTransactionAsync(RefundId.ToString());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);

        // Auth exchange first: the API key, never the secret key.
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("/api/auth/tokens", handler.Requests[0].Path);
        Assert.Contains("\"api_key\":\"" + ApiKey + "\"", handler.Requests[0].Body, StringComparison.Ordinal);

        // Then the transaction read, at the exact id, with the bearer token.
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.Equal($"/api/acceptance/transactions/{RefundId}", handler.Requests[1].Path);
        Assert.Equal($"Bearer {AuthToken}", handler.Requests[1].Authorization);
        Assert.DoesNotContain(SecretKey, handler.Requests[1].Authorization!, StringComparison.Ordinal);

        var view = result.Value;
        Assert.Equal(RefundId, view.TransactionId);
        Assert.True(view.IsRefund);
        Assert.True(view.Success);
        Assert.False(view.Pending);
        Assert.Equal(AmountCents, view.AmountCents);
        Assert.Equal("EGP", view.Currency);
        Assert.Equal(OrderId, view.OrderId);
        Assert.Equal(CaptureId, view.ParentTransactionId);
    }

    [Fact]
    public async Task Readback_reuses_the_cached_auth_token_across_calls()
    {
        var handler = new RecordingHandler { TransactionBody = SettledRefundJson() };
        var client = Client(handler);

        Assert.True((await client.VerifyRefundTransactionAsync(RefundId.ToString())).IsSuccess);
        Assert.True((await client.VerifyRefundTransactionAsync(CaptureId.ToString())).IsSuccess);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Single(handler.Requests, request => request.Path == "/api/auth/tokens");
    }

    [Fact]
    public async Task Readback_without_an_api_key_fails_closed_without_calling_the_gateway()
    {
        var handler = new RecordingHandler();

        var result = await Client(handler, apiKey: "   ").VerifyRefundTransactionAsync(RefundId.ToString());

        Assert.True(result.IsFailure);
        Assert.Equal("Paymob.NotConfigured", result.Error.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Readback_fails_closed_when_the_token_exchange_is_rejected()
    {
        var handler = new RecordingHandler { AuthTokenStatus = HttpStatusCode.Unauthorized };

        var result = await Client(handler).VerifyRefundTransactionAsync(RefundId.ToString());

        Assert.True(result.IsFailure);
        Assert.Equal("Paymob.VerificationUnavailable", result.Error.Code);
        Assert.Single(handler.Requests);
        Assert.Equal("/api/auth/tokens", handler.Requests[0].Path);
    }

    [Fact]
    public async Task Readback_reports_failure_when_the_transaction_is_refused()
    {
        var handler = new RecordingHandler { TransactionStatus = HttpStatusCode.NotFound };

        var result = await Client(handler).VerifyRefundTransactionAsync(RefundId.ToString());

        Assert.True(result.IsFailure);
        Assert.Equal("Paymob.VerificationUnavailable", result.Error.Code);
        Assert.Contains("404", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Readback_refuses_an_incomplete_response_rather_than_guessing()
    {
        // The identity/outcome fields are mandatory: a response with no
        // is_refund flag must fail closed, never be read as a refund.
        var handler = new RecordingHandler
        {
            TransactionBody = "{\"id\":561442898,\"success\":true,\"pending\":false,"
                + "\"amount_cents\":1150000,\"currency\":\"EGP\"}"
        };

        var result = await Client(handler).VerifyRefundTransactionAsync(RefundId.ToString());

        Assert.True(result.IsFailure);
        Assert.Equal("Paymob.VerificationUnavailable", result.Error.Code);
    }

    [Fact]
    public async Task Readback_surfaces_a_missing_optional_field_as_null()
    {
        // amount/currency/order/parent are optional at the HTTP layer and are
        // surfaced as null; the APPLICATION refuses to complete unless they
        // match the stored booking.
        var handler = new RecordingHandler
        {
            TransactionBody = "{\"id\":561442898,\"is_refund\":true,\"success\":true,\"pending\":false}"
        };

        var result = await Client(handler).VerifyRefundTransactionAsync(RefundId.ToString());

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.AmountCents);
        Assert.Null(result.Value.Currency);
        Assert.Null(result.Value.OrderId);
        Assert.Null(result.Value.ParentTransactionId);
    }

    [Fact]
    public async Task Readback_reads_a_nested_parent_transaction_id()
    {
        var handler = new RecordingHandler
        {
            TransactionBody = "{\"id\":561442898,\"is_refund\":true,\"success\":true,\"pending\":false,"
                + "\"amount_cents\":1150000,\"currency\":\"EGP\","
                + "\"parent_transaction\":{\"id\":561442756},\"order\":{\"id\":640412142}}"
        };

        var result = await Client(handler).VerifyRefundTransactionAsync(RefundId.ToString());

        Assert.True(result.IsSuccess);
        Assert.Equal(CaptureId, result.Value.ParentTransactionId);
    }

    [Fact]
    public async Task Readback_refuses_a_non_json_response()
    {
        var handler = new RecordingHandler { TransactionBody = "<html>not json</html>" };

        var result = await Client(handler).VerifyRefundTransactionAsync(RefundId.ToString());

        Assert.True(result.IsFailure);
        Assert.Equal("Paymob.VerificationUnavailable", result.Error.Code);
    }

    [Fact]
    public async Task Readback_refuses_an_empty_transaction_id_without_calling_the_gateway()
    {
        var handler = new RecordingHandler();

        var result = await Client(handler).VerifyRefundTransactionAsync("   ");

        Assert.True(result.IsFailure);
        Assert.Equal("Paymob.VerificationUnavailable", result.Error.Code);
        Assert.Empty(handler.Requests);
    }

    /// <summary>The proven settled refund child, exactly as read back from the gateway.</summary>
    private static string SettledRefundJson() =>
        "{"
        + $"\"id\":{RefundId},"
        + $"\"amount_cents\":{AmountCents},"
        + "\"success\":true,\"pending\":false,\"currency\":\"EGP\","
        + "\"is_refund\":true,\"is_refunded\":false,"
        + "\"has_parent_transaction\":true,"
        + $"\"parent_transaction\":{CaptureId},"
        + $"\"order\":{{\"id\":{OrderId}}}}}";

    private sealed record RecordedRequest(HttpMethod Method, string Path, string? Authorization, string Body);

    private sealed class SingleHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        public HttpStatusCode AuthTokenStatus { get; init; } = HttpStatusCode.Created;

        public string AuthTokenBody { get; init; } = "{\"token\":\"" + AuthToken + "\"}";

        public HttpStatusCode TransactionStatus { get; init; } = HttpStatusCode.OK;

        public string TransactionBody { get; init; } = "{}";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            string path = request.RequestUri!.AbsolutePath;
            Requests.Add(new RecordedRequest(
                request.Method,
                path,
                request.Headers.TryGetValues("Authorization", out var values) ? values.FirstOrDefault() : null,
                body));

            return path == "/api/auth/tokens"
                ? Json(AuthTokenStatus, AuthTokenBody)
                : Json(TransactionStatus, TransactionBody);
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) =>
            new(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
