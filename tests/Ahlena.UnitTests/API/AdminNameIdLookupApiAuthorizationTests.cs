using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Identity.Application.Abstractions.Security;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.UnitTests.API;

/// <summary>
/// Round-trip runtime evidence for the lookup role matrix: real HTTP requests
/// through the isolated <see cref="PortalApiMetadataHost"/> — JWT authentication,
/// claim validation and the live authorization middleware.
/// <list type="bullet">
/// <item>the caregiver lookup's dedicated policy stays closed at the HTTP seam
/// for the roles it must deny: anonymous 401, app audience / ContentAdmin /
/// SupportAdmin 403 (SupportAdmin's prior access via the old shared policy is
/// explicitly removed);</item>
/// <item>the elderly lookup admits SuperAdmin and SupportAdmin with real 200
/// pages and denies ContentAdmin and FinanceAdmin with real 403s;</item>
/// <item>the admitted-role policy decisions themselves (SuperAdmin/FinanceAdmin
/// on the caregiver lookup) are proven over the app's real registered policy by
/// <see cref="AdminNameIdLookupRoleMatrixTests"/>: the caregiver lookup handler
/// reads through a PostgreSQL-specific raw-SQL projection that no in-memory
/// test store can serve, so this host truthfully cannot produce an admitted
/// 200 there — the facts below assert only that the request passes
/// authentication + policy and reaches the handler.</item>
/// </list>
/// No remote session, database or provider is touched.
/// </summary>
public sealed class AdminNameIdLookupApiAuthorizationTests : IClassFixture<PortalApiMetadataHost>
{
    private const string CaregiverLookupPath = "/api/v1/admin/caregivers/lookup";
    private const string ElderlyLookupPath = "/api/v1/admin/elderly/lookup";

    private readonly PortalApiMetadataHost _host;

    public AdminNameIdLookupApiAuthorizationTests(PortalApiMetadataHost host) => _host = host;

    // ================= caregiver lookup (CaregiverLookupAdmin) ===================

    [Fact]
    public async Task CaregiverLookup_Anonymous_IsUnauthorized()
    {
        using HttpClient client = AnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(CaregiverLookupPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CaregiverLookup_AppAudience_IsForbidden()
    {
        using HttpClient client = AuthenticatedClient(AccountType.Family, 1);

        using HttpResponseMessage response = await client.GetAsync(CaregiverLookupPath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CaregiverLookup_ContentAdmin_IsForbidden()
    {
        using HttpClient client = AuthenticatedClient(AccountType.ContentAdmin, 2);

        using HttpResponseMessage response = await client.GetAsync(CaregiverLookupPath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CaregiverLookup_SupportAdmin_PriorAccess_IsRemoved()
    {
        // The explicit matrix change: SupportAdmin had access through the old
        // shared review policy and must now be denied on the lookup only.
        using HttpClient client = AuthenticatedClient(AccountType.SupportAdmin, 3);

        using HttpResponseMessage response = await client.GetAsync(CaregiverLookupPath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CaregiverLookup_SuperAdmin_PassesPolicy_AndReachesTheHandler()
    {
        using HttpClient client = AuthenticatedClient(AccountType.SuperAdmin, 4);

        using HttpResponseMessage response = await client.GetAsync(CaregiverLookupPath);

        AssertPolicyAdmitted(response);
    }

    [Fact]
    public async Task CaregiverLookup_FinanceAdmin_PassesPolicy_AndReachesTheHandler()
    {
        using HttpClient client = AuthenticatedClient(AccountType.FinanceAdmin, 5);

        using HttpResponseMessage response = await client.GetAsync(CaregiverLookupPath);

        AssertPolicyAdmitted(response);
    }

    // ============== elderly lookup (unchanged OperationalReadAdmin) ==============

    [Fact]
    public async Task ElderlyLookup_Anonymous_IsUnauthorized()
    {
        using HttpClient client = AnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(ElderlyLookupPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ElderlyLookup_AppAudience_IsForbidden()
    {
        using HttpClient client = AuthenticatedClient(AccountType.Family, 6);

        using HttpResponseMessage response = await client.GetAsync(ElderlyLookupPath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ElderlyLookup_ContentAdmin_IsForbidden()
    {
        using HttpClient client = AuthenticatedClient(AccountType.ContentAdmin, 7);

        using HttpResponseMessage response = await client.GetAsync(ElderlyLookupPath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ElderlyLookup_FinanceAdmin_IsForbidden()
    {
        using HttpClient client = AuthenticatedClient(AccountType.FinanceAdmin, 8);

        using HttpResponseMessage response = await client.GetAsync(ElderlyLookupPath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ElderlyLookup_SupportAdmin_PassesPolicy_AndReturnsAnEmptyPage()
    {
        using HttpClient client = AuthenticatedClient(AccountType.SupportAdmin, 9);

        using HttpResponseMessage response = await client.GetAsync(ElderlyLookupPath);

        await AssertEmptyPageAsync(response);
    }

    [Fact]
    public async Task ElderlyLookup_SuperAdmin_PassesPolicy_AndReturnsAnEmptyPage()
    {
        using HttpClient client = AuthenticatedClient(AccountType.SuperAdmin, 10);

        using HttpResponseMessage response = await client.GetAsync(ElderlyLookupPath);

        await AssertEmptyPageAsync(response);
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
            FullName.Create($"Lookup User {index}"),
            Email.Create($"lookupboundary{index}@example.test"),
            PhoneNumber.Create($"+20110000{index:D4}"));
        user.AddAccount(accountType);
        return user;
    }

    /// <summary>
    /// An admitted role passes authentication + policy and reaches the
    /// handler; the handler's raw-SQL projection then fails on the isolated
    /// non-relational store (a 500) — the denial statuses are exactly what
    /// this fact proves absent. The admitted roles' real policy evaluation is
    /// covered by <see cref="AdminNameIdLookupRoleMatrixTests"/>, and the
    /// handler's paging/projection behavior by the existing caregiver lookup
    /// provider facts.
    /// </summary>
    private static void AssertPolicyAdmitted(HttpResponseMessage response)
    {
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task AssertEmptyPageAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("totalCount").GetInt32());
    }
}
