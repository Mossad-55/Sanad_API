using System.Net;
using System.Text;
using Ahlena.Modules.Families.Infrastructure.Payments;

namespace Ahlena.UnitTests.Families;

/// <summary>
/// The Paymob subscription management namespace authenticates with the merchant
/// auth token exchanged from the API key; it rejects the secret key. These tests
/// pin that contract so a plan-change amount sync can never silently regress to
/// the secret key again (which the live gateway answers with 401).
/// </summary>
public sealed class PaymobSubscriptionManagementAuthTests
{
    private const string ApiKey = "api_key_test_value";
    private const string SecretKey = "egy_sk_test_secret_value";
    private const string AuthToken = "auth_token_value";

    private static PaymobOptions ConfiguredOptions(string apiKey = ApiKey) => new()
    {
        BaseUrl = "https://accept.paymob.com",
        SecretKey = SecretKey,
        ApiKey = apiKey,
        PublicKey = "pk_test"
    };

    [Fact]
    public async Task Amount_update_authenticates_with_the_api_key_token_and_never_the_secret_key()
    {
        var handler = new RecordingHandler();
        var client = new PaymobClient(new SingleHttpClientFactory(new HttpClient(handler)), Microsoft.Extensions.Options.Options.Create(ConfiguredOptions()));

        var result = await client.UpdateSubscriptionAmountAsync("77", 228.85m);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("/api/auth/tokens", handler.Requests[0].Path);
        Assert.Contains("\"api_key\":\"" + ApiKey + "\"", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        Assert.Equal("/api/acceptance/subscriptions/77", handler.Requests[1].Path);
        Assert.Equal($"Bearer {AuthToken}", handler.Requests[1].Authorization);
        Assert.DoesNotContain(SecretKey, handler.Requests[1].Authorization!, StringComparison.Ordinal);
        Assert.Contains("\"amount_cents\":22885", handler.Requests[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Amount_update_reuses_the_cached_auth_token_across_calls()
    {
        var handler = new RecordingHandler();
        var client = new PaymobClient(new SingleHttpClientFactory(new HttpClient(handler)), Microsoft.Extensions.Options.Options.Create(ConfiguredOptions()));

        Assert.True((await client.UpdateSubscriptionAmountAsync("77", 228.85m)).IsSuccess);
        Assert.True((await client.UpdateSubscriptionAmountAsync("77", 199m)).IsSuccess);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Single(handler.Requests, request => request.Path == "/api/auth/tokens");
        Assert.All(
            handler.Requests.Where(request => request.Method == HttpMethod.Put),
            request => Assert.Equal($"Bearer {AuthToken}", request.Authorization));
    }

    [Fact]
    public async Task Amount_update_without_an_api_key_fails_closed_without_calling_the_gateway()
    {
        var handler = new RecordingHandler();
        var client = new PaymobClient(new SingleHttpClientFactory(new HttpClient(handler)), Microsoft.Extensions.Options.Options.Create(ConfiguredOptions(apiKey: "   ")));

        var result = await client.UpdateSubscriptionAmountAsync("77", 228.85m);

        Assert.True(result.IsFailure);
        Assert.Equal("Paymob.NotConfigured", result.Error.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Amount_update_fails_closed_when_the_token_exchange_is_rejected()
    {
        var handler = new RecordingHandler { AuthTokenStatus = HttpStatusCode.Unauthorized, AuthTokenBody = "{\"detail\":\"invalid api key\"}" };
        var client = new PaymobClient(new SingleHttpClientFactory(new HttpClient(handler)), Microsoft.Extensions.Options.Options.Create(ConfiguredOptions()));

        var result = await client.UpdateSubscriptionAmountAsync("77", 228.85m);

        Assert.True(result.IsFailure);
        Assert.Equal("Paymob.GatewayError", result.Error.Code);
        Assert.Single(handler.Requests);
        Assert.Equal("/api/auth/tokens", handler.Requests[0].Path);
    }

    [Fact]
    public async Task Amount_update_reports_a_gateway_error_when_the_update_is_refused()
    {
        var handler = new RecordingHandler { UpdateStatus = HttpStatusCode.Unauthorized };
        var client = new PaymobClient(new SingleHttpClientFactory(new HttpClient(handler)), Microsoft.Extensions.Options.Options.Create(ConfiguredOptions()));

        var result = await client.UpdateSubscriptionAmountAsync("77", 228.85m);

        Assert.True(result.IsFailure);
        Assert.Equal("Paymob.GatewayError", result.Error.Code);
        Assert.Equal("Paymob subscription update failed with status 401.", result.Error.Message);
    }

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

        public HttpStatusCode UpdateStatus { get; init; } = HttpStatusCode.OK;

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
                : Json(UpdateStatus, "{}");
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) =>
            new(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
