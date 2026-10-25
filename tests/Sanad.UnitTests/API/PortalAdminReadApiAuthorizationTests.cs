using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.API;

/// <summary>
/// Round-2 runtime evidence for the round-1 role boundaries. The reflection
/// policy pins in <c>AdminFeedbackAppRatingsTests</c>,
/// <c>AdminSubscriptionsControllerTests</c> and
/// <c>AdminOperationalReadAuthorizationTests</c> only prove which policy an
/// action declares; these facts drive real HTTP requests through the isolated
/// <see cref="PortalApiMetadataHost"/> — JWT authentication, claim validation
/// and the live authorization middleware:
/// <list type="bullet">
/// <item>the app-rating investigation read and the family-subscription
/// list/detail reads admit SuperAdmin and SupportAdmin on Normal tokens
/// (200/404), stay 403 for every other account type (a Family token stands in
/// for all app audiences) and stay 401 for anonymous callers;</item>
/// <item>plan authoring (the tax-rule write stands in for the SuperAdmin-only
/// <c>SubscriptionPlanAdmin</c> surface) stays denied to SupportAdmin (403),
/// while SuperAdmin passes authorization and reaches the handler.</item>
/// </list>
/// The host's module stores are isolated InMemory registrations, so the 200s
/// are provably empty pages, the unknown-id detail is a handler 404, and the
/// SuperAdmin plan-write 400 is the handler's own shared-configuration guard —
/// no remote session, database or provider is touched.
/// </summary>
public sealed class PortalAdminReadApiAuthorizationTests : IClassFixture<PortalApiMetadataHost>
{
    private const string AppRatingsPath = "/api/v1/admin/feedback/app-ratings";
    private const string SubscriptionsPath = "/api/v1/admin/subscriptions";

    private readonly PortalApiMetadataHost _host;

    public PortalAdminReadApiAuthorizationTests(PortalApiMetadataHost host) => _host = host;

    // ================= app-rating read (OperationalReadAdmin) ====================

    [Fact]
    public async Task AppRatingRead_Anonymous_IsUnauthorized()
    {
        using HttpClient client = AnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(AppRatingsPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AppRatingRead_AppAudience_IsForbidden()
    {
        using HttpClient client = AuthenticatedClient(AccountType.Family, 1);

        using HttpResponseMessage response = await client.GetAsync(AppRatingsPath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AppRatingRead_SupportAdmin_PassesPolicy_AndReturnsAnEmptyPage()
    {
        using HttpClient client = AuthenticatedClient(AccountType.SupportAdmin, 2);

        using HttpResponseMessage response = await client.GetAsync(AppRatingsPath);

        await AssertEmptyPageAsync(response);
    }

    [Fact]
    public async Task AppRatingRead_SuperAdmin_PassesPolicy_AndReturnsAnEmptyPage()
    {
        using HttpClient client = AuthenticatedClient(AccountType.SuperAdmin, 3);

        using HttpResponseMessage response = await client.GetAsync(AppRatingsPath);

        await AssertEmptyPageAsync(response);
    }

    // ============= family-subscription reads (OperationalReadAdmin) ==============

    [Fact]
    public async Task FamilySubscriptionList_Anonymous_IsUnauthorized()
    {
        using HttpClient client = AnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(SubscriptionsPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FamilySubscriptionList_AppAudience_IsForbidden()
    {
        using HttpClient client = AuthenticatedClient(AccountType.Family, 4);

        using HttpResponseMessage response = await client.GetAsync(SubscriptionsPath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FamilySubscriptionList_SupportAdmin_PassesPolicy_AndReturnsAnEmptyPage()
    {
        using HttpClient client = AuthenticatedClient(AccountType.SupportAdmin, 5);

        using HttpResponseMessage response = await client.GetAsync(SubscriptionsPath);

        await AssertEmptyPageAsync(response);
    }

    [Fact]
    public async Task FamilySubscriptionDetail_SupportAdmin_PassesPolicy_WithAnUnknownId()
    {
        using HttpClient client = AuthenticatedClient(AccountType.SupportAdmin, 6);

        using HttpResponseMessage response = await client.GetAsync(
            $"{SubscriptionsPath}/{Guid.NewGuid():D}");

        // 404 — not 401/403 — is the proof that the policy admitted the token
        // and the handler itself answered.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "Subscriptions.Subscription.NotFound",
            body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task FamilySubscriptionDetail_AppAudience_IsForbidden()
    {
        using HttpClient client = AuthenticatedClient(AccountType.Family, 7);

        using HttpResponseMessage response = await client.GetAsync(
            $"{SubscriptionsPath}/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ============== plan authoring (SuperAdmin-only writes) ======================

    [Fact]
    public async Task PlanWrite_SupportAdmin_IsForbidden()
    {
        using HttpClient client = AuthenticatedClient(AccountType.SupportAdmin, 8);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{SubscriptionsPath}/tax-rules",
            new { ratePercentage = 14m, version = 1, effectiveOnUtc = DateTime.UtcNow });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PlanWrite_Anonymous_IsUnauthorized()
    {
        using HttpClient client = AnonymousClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{SubscriptionsPath}/tax-rules",
            new { ratePercentage = 14m, version = 1, effectiveOnUtc = DateTime.UtcNow });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PlanWrite_SuperAdmin_PassesPolicy_AndReachesTheHandler()
    {
        using HttpClient client = AuthenticatedClient(AccountType.SuperAdmin, 9);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{SubscriptionsPath}/tax-rules",
            new { ratePercentage = 14m, version = 1, effectiveOnUtc = DateTime.UtcNow });

        // The isolated host has no finance shared charge rule seeded, so the
        // handler's own guard answers 400 — proving the request passed
        // authentication and the SuperAdmin-only policy.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "Subscriptions.Tax.SharedConfigurationRequired",
            body.RootElement.GetProperty("code").GetString());
    }

    // ================================ harness ====================================

    private HttpClient AnonymousClient() =>
        _host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private HttpClient AuthenticatedClient(AccountType accountType, int index)
    {
        HttpClient client = AnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CreateToken(CreateUser(accountType, index)));
        return client;
    }

    private string CreateToken(User user)
    {
        IAuthTokenService tokens = _host.Services.GetRequiredService<IAuthTokenService>();
        return tokens.GenerateAccessToken(user, DateTime.UtcNow.AddSeconds(-5)).PlainTextToken;
    }

    private static User CreateUser(AccountType accountType, int index)
    {
        User user = User.Create(
            FullName.Create($"مستخدم {index}"),
            FullName.Create($"Portal User {index}"),
            Email.Create($"portalboundary{index}@example.test"),
            PhoneNumber.Create($"+20110000{index:D4}"));
        user.AddAccount(accountType);
        return user;
    }

    private static async Task AssertEmptyPageAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("items").GetArrayLength());
    }
}
