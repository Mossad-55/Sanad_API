using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Infrastructure.Payments;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;
using Sanad.Modules.Finance.Infrastructure;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.API;

/// <summary>
/// E04-A API contract by reflection: declared routes, policy, documented
/// statuses and the request body the admin update accepts. No host needed.
/// </summary>
public sealed class PlatformControlsControllerContractTests
{
    [Fact]
    public void AdminController_IsSuperAdminOnlyAndRoutesUnderAdminPlatformControls()
    {
        AuthorizeAttribute authorize = Assert.Single(
            typeof(AdminPlatformControlsController).GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(AuthorizationPolicies.PlatformControlsAdmin, authorize.Policy);
        Assert.Equal(
            "api/v1/admin/platform-controls",
            typeof(AdminPlatformControlsController).GetCustomAttributes<RouteAttribute>().Single().Template);
        Assert.DoesNotContain(
            typeof(AdminPlatformControlsController).GetCustomAttributes<AllowAnonymousAttribute>(),
            _ => true);
    }

    [Fact]
    public void AdminController_ExposesOnlyTheDocumentedReadAndUpdateOperations()
    {
        MethodInfo[] actions = typeof(AdminPlatformControlsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.Equal(
            ["Get", "Update"],
            actions.Select(action => action.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());

        MethodInfo get = typeof(AdminPlatformControlsController)
            .GetMethod(nameof(AdminPlatformControlsController.Get))!;
        Assert.Null(get.GetCustomAttribute<HttpGetAttribute>()!.Template);
        int[] getStatuses = get.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .OrderBy(code => code)
            .ToArray();
        Assert.Equal([200, 401, 403, 503], getStatuses);
        Assert.Contains(
            get.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == 200 && attribute.Type == typeof(PlatformControlResponse));

        MethodInfo update = typeof(AdminPlatformControlsController)
            .GetMethod(nameof(AdminPlatformControlsController.Update))!;
        Assert.NotNull(update.GetCustomAttribute<HttpPutAttribute>());
        Assert.Equal(typeof(UpdatePlatformControlRequest), update.GetParameters()[0].ParameterType);
        int[] putStatuses = update.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .OrderBy(code => code)
            .ToArray();
        Assert.Equal([200, 400, 401, 403, 409, 503], putStatuses);

        // Both admin actions document the auth failures with the existing
        // repository convention (the untyped 401/403 overload, i.e. no response
        // body type, as AdminUsersController does); the runtime 401/403 behavior
        // is proven by the host tests below.
        Assert.All(
            get.GetCustomAttributes<ProducesResponseTypeAttribute>()
                .Concat(update.GetCustomAttributes<ProducesResponseTypeAttribute>())
                .Where(attribute => attribute.StatusCode is 401 or 403),
            attribute => Assert.Equal(typeof(void), attribute.Type));
    }

    [Fact]
    public void UpdateRequest_IsAFullReplacementBodyThatKeepsInternalReasonOptional()
    {
        ParameterInfo[] parameters = typeof(UpdatePlatformControlRequest)
            .GetConstructors()
            .Single()
            .GetParameters();

        Assert.Equal(
            [
                "ExpectedVersion",
                "State",
                "CardEnabled",
                "WalletEnabled",
                "MaintenanceMessageArabic",
                "MaintenanceMessageEnglish",
                "InternalReason"
            ],
            parameters.Select(parameter => parameter.Name!).ToArray());

        // Omitted message and reason fields stay optional; the host test below
        // proves that omitted Card/Wallet switches cannot leave a method
        // silently enabled.
        Assert.All(
            parameters.Where(parameter =>
                parameter.Name is "MaintenanceMessageArabic" or "MaintenanceMessageEnglish" or "InternalReason"),
            parameter => Assert.True(parameter.HasDefaultValue));
    }

    [Fact]
    public void PublicStatusController_IsAnonymousAndExposesOnlyTheStatusRead()
    {
        Type type = typeof(PlatformStatusController);

        Assert.Empty(type.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
        Assert.Equal("api/v1/platform", type.GetCustomAttributes<RouteAttribute>().Single().Template);

        MethodInfo status = Assert.Single(
            type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

        Assert.Equal("status", status.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.NotNull(status.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Contains(
            status.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == 200 && attribute.Type == typeof(PublicPlatformStatusResponse));
        Assert.Contains(
            status.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == 503);
        // The public route is anonymous: it must not document auth failures.
        Assert.DoesNotContain(
            status.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode is 401 or 403);
    }
}

/// <summary>
/// E04-A runtime behavior over the isolated <see cref="PortalApiMetadataHost"/>:
/// real production MVC/authorization pipeline, isolated InMemory Finance
/// context, no live database, no migration and no provider call.
/// </summary>
public sealed class PlatformControlsHostTests : IClassFixture<PortalApiMetadataHost>
{
    private const string AdminRoute = "/api/v1/admin/platform-controls";

    private const string PublicRoute = "/api/v1/platform/status";

    private static readonly Guid SeedId =
        Guid.Parse("0198e2c2-4444-7777-8888-0000000000e4");

    private static readonly DateTime SeededOnUtc = new(2026, 10, 19, 6, 16, 22, DateTimeKind.Utc);

    private readonly PortalApiMetadataHost _host;

    public PlatformControlsHostTests(PortalApiMetadataHost host) => _host = host;

    [Fact]
    public async Task AdminRead_WithoutAToken_Returns401()
    {
        await using FinanceDbContext db = CreateFinanceDb();
        using var factory = CreateFactory(db);
        using HttpClient client = CreatePortalClient(factory);

        using HttpResponseMessage response = await client.GetAsync(AdminRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(AccountType.Family)]
    [InlineData(AccountType.SupportAdmin)]
    [InlineData(AccountType.FinanceAdmin)]
    [InlineData(AccountType.CareHomeOwner)]
    public async Task AdminRead_ForANonSuperAdminAccount_Returns403(AccountType accountType)
    {
        await using FinanceDbContext db = CreateFinanceDb();
        string token = CreateActor(accountType, $"e04.denied.{accountType}@example.test").Token;
        using var factory = CreateFactory(db);
        using HttpClient client = CreatePortalClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await client.GetAsync(AdminRoute);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminUpdate_ForAFamilyAccount_Returns403AndKeepsThePersistedState()
    {
        await using FinanceDbContext db = CreateFinanceDb();
        string token = CreateActor(AccountType.Family, "e04.write.denied@example.test").Token;
        using var factory = CreateFactory(db);
        using HttpClient client = CreatePortalClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await client.PutAsync(
            AdminRoute,
            JsonContent(new
            {
                expectedVersion = 1,
                state = (int)PlatformState.Maintenance,
                cardEnabled = false,
                walletEnabled = false
            }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        PlatformControlState persisted = await db.PlatformControlStates.AsNoTracking().SingleAsync();
        Assert.Equal(PlatformState.Operational, persisted.State);
        Assert.Equal(1, persisted.Version);
        Assert.Empty(db.PlatformControlAuditEntries);
    }

    [Fact]
    public async Task AdminRead_AsSuperAdmin_ReturnsThePersistedControlState()
    {
        await using FinanceDbContext db = CreateFinanceDb();
        string token = CreateActor(AccountType.SuperAdmin, "e04.read@example.test").Token;
        using var factory = CreateFactory(db);
        using HttpClient client = CreatePortalClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await client.GetAsync(AdminRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, body.RootElement.GetProperty("version").GetInt32());
        Assert.Equal((int)PlatformState.Operational, body.RootElement.GetProperty("state").GetInt32());
        Assert.True(body.RootElement.GetProperty("cardEnabled").GetBoolean());
        Assert.True(body.RootElement.GetProperty("walletEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("maintenanceMessageArabic").ValueKind);
    }

    [Fact]
    public async Task AdminUpdate_AsSuperAdmin_PersistsTheChangeAndWritesAttributableAudit()
    {
        await using FinanceDbContext db = CreateFinanceDb();
        (User actor, string token) = CreateActor(AccountType.SuperAdmin, "e04.update@example.test");
        using var factory = CreateFactory(db);
        using HttpClient client = CreatePortalClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await client.PutAsync(
            AdminRoute,
            JsonContent(new
            {
                expectedVersion = 1,
                state = (int)PlatformState.Maintenance,
                cardEnabled = false,
                walletEnabled = true,
                maintenanceMessageArabic = "الصيانة الدورية",
                maintenanceMessageEnglish = "Scheduled maintenance",
                internalReason = "E04 test reason"
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string payload = await response.Content.ReadAsStringAsync();
        using JsonDocument body = JsonDocument.Parse(payload);
        Assert.Equal(2, body.RootElement.GetProperty("version").GetInt32());
        Assert.Equal((int)PlatformState.Maintenance, body.RootElement.GetProperty("state").GetInt32());
        Assert.False(body.RootElement.GetProperty("cardEnabled").GetBoolean());
        Assert.True(body.RootElement.GetProperty("walletEnabled").GetBoolean());
        Assert.Equal(
            "Scheduled maintenance",
            body.RootElement.GetProperty("maintenanceMessageEnglish").GetString());
        Assert.Equal(
            "الصيانة الدورية",
            body.RootElement.GetProperty("maintenanceMessageArabic").GetString());

        // The internal reason is stored for audit and never returned.
        Assert.DoesNotContain("E04 test reason", payload);

        PlatformControlAuditEntry audit = await db.PlatformControlAuditEntries.SingleAsync();
        Assert.Equal(actor.Id.Value, audit.ActorUserId);
        Assert.Equal(1, audit.PreviousVersion);
        Assert.Equal(2, audit.NewVersion);
        Assert.Equal(PlatformState.Operational, audit.BeforeState);
        Assert.Equal(PlatformState.Maintenance, audit.AfterState);
        Assert.True(audit.BeforeCardEnabled);
        Assert.False(audit.AfterCardEnabled);
        Assert.True(audit.BeforeWalletEnabled);
        Assert.True(audit.AfterWalletEnabled);
        Assert.Null(audit.BeforeMaintenanceMessageArabic);
        Assert.Equal("الصيانة الدورية", audit.AfterMaintenanceMessageArabic);
        Assert.Null(audit.BeforeMaintenanceMessageEnglish);
        Assert.Equal("Scheduled maintenance", audit.AfterMaintenanceMessageEnglish);
        Assert.Equal("E04 test reason", audit.InternalReason);

        using HttpResponseMessage readback = await client.GetAsync(AdminRoute);
        Assert.Equal(HttpStatusCode.OK, readback.StatusCode);
        using JsonDocument readbackBody = JsonDocument.Parse(await readback.Content.ReadAsStringAsync());
        Assert.Equal(2, readbackBody.RootElement.GetProperty("version").GetInt32());
        Assert.Equal((int)PlatformState.Maintenance, readbackBody.RootElement.GetProperty("state").GetInt32());
    }

    [Fact]
    public async Task AdminUpdate_WithAStaleVersion_Returns409AndPreservesTheNewerState()
    {
        await using FinanceDbContext db = CreateFinanceDb();
        string token = CreateActor(AccountType.SuperAdmin, "e04.stale@example.test").Token;
        using var factory = CreateFactory(db);
        using HttpClient client = CreatePortalClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage applied = await client.PutAsync(
            AdminRoute,
            JsonContent(new
            {
                expectedVersion = 1,
                state = (int)PlatformState.Maintenance,
                cardEnabled = false,
                walletEnabled = false,
                maintenanceMessageEnglish = "Maintenance"
            }));
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);

        using HttpResponseMessage stale = await client.PutAsync(
            AdminRoute,
            JsonContent(new
            {
                expectedVersion = 1,
                state = (int)PlatformState.Operational,
                cardEnabled = true,
                walletEnabled = true
            }));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
        Assert.Equal("Platform.ControlVersionConflict", body.RootElement.GetProperty("code").GetString());

        PlatformControlState persisted = await db.PlatformControlStates.AsNoTracking().SingleAsync();
        Assert.Equal(PlatformState.Maintenance, persisted.State);
        Assert.Equal(2, persisted.Version);
        Assert.False(persisted.CardEnabled);
        Assert.Single(db.PlatformControlAuditEntries);
    }

    [Fact]
    public async Task AdminUpdate_WithAnInvalidMessage_Returns400WithoutChangingState()
    {
        await using FinanceDbContext db = CreateFinanceDb();
        string token = CreateActor(AccountType.SuperAdmin, "e04.invalid@example.test").Token;
        using var factory = CreateFactory(db);
        using HttpClient client = CreatePortalClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await client.PutAsync(
            AdminRoute,
            JsonContent(new
            {
                expectedVersion = 1,
                state = (int)PlatformState.Maintenance,
                cardEnabled = false,
                walletEnabled = false,
                maintenanceMessageEnglish = new string('x', PlatformControlState.MaximumMessageLength + 1)
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Platform.Control.Invalid", body.RootElement.GetProperty("code").GetString());

        PlatformControlState persisted = await db.PlatformControlStates.AsNoTracking().SingleAsync();
        Assert.Equal(PlatformState.Operational, persisted.State);
        Assert.Equal(1, persisted.Version);
        Assert.Empty(db.PlatformControlAuditEntries);
    }

    [Fact]
    public async Task AdminUpdate_WithOmittedSwitches_DisablesBothMethods()
    {
        await using FinanceDbContext db = CreateFinanceDb();
        string token = CreateActor(AccountType.SuperAdmin, "e04.omitted@example.test").Token;
        using var factory = CreateFactory(db);
        using HttpClient client = CreatePortalClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await client.PutAsync(
            AdminRoute,
            JsonContent(new { expectedVersion = 1, state = (int)PlatformState.Operational }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("cardEnabled").GetBoolean());
        Assert.False(body.RootElement.GetProperty("walletEnabled").GetBoolean());

        PlatformControlState persisted = await db.PlatformControlStates.AsNoTracking().SingleAsync();
        Assert.False(persisted.CardEnabled);
        Assert.False(persisted.WalletEnabled);
    }

    [Fact]
    public async Task MissingControlRecord_FailsClosedForAdminReadUpdateAndPublicStatus()
    {
        await using FinanceDbContext db = CreateFinanceDb(seeded: false);
        string token = CreateActor(AccountType.SuperAdmin, "e04.unavailable@example.test").Token;
        using var factory = CreateFactory(db);
        using HttpClient client = CreatePortalClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage read = await client.GetAsync(AdminRoute);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, read.StatusCode);
        using (JsonDocument body = JsonDocument.Parse(await read.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Platform.ControlStateUnavailable", body.RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain("finance", body.RootElement.GetProperty("detail").GetString()!);
        }

        using HttpResponseMessage update = await client.PutAsync(
            AdminRoute,
            JsonContent(new
            {
                expectedVersion = 1,
                state = (int)PlatformState.Operational,
                cardEnabled = true,
                walletEnabled = true
            }));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, update.StatusCode);

        using HttpClient anonymous = CreatePortalClient(factory);
        using HttpResponseMessage status = await anonymous.GetAsync(PublicRoute);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, status.StatusCode);
        using JsonDocument statusBody = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.Equal("Platform.ControlStateUnavailable", statusBody.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PublicStatus_IsAnonymousAndExposesOnlyTheSanitizedShape()
    {
        await using FinanceDbContext db = CreateFinanceDb(
            state: PlatformState.Maintenance,
            messageArabic: "صيانة",
            messageEnglish: "Maintenance");
        using var factory = CreateFactory(db, ConfiguredGateway());
        using HttpClient client = CreatePortalClient(factory);

        using HttpResponseMessage response = await client.GetAsync(PublicRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string payload = await response.Content.ReadAsStringAsync();
        using JsonDocument body = JsonDocument.Parse(payload);

        Assert.Equal(
            ["availablePaymentMethods", "maintenanceMessageArabic", "maintenanceMessageEnglish", "state"],
            body.RootElement.EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
        Assert.Equal((int)PlatformState.Maintenance, body.RootElement.GetProperty("state").GetInt32());
        Assert.Equal("صيانة", body.RootElement.GetProperty("maintenanceMessageArabic").GetString());
        Assert.Equal("Maintenance", body.RootElement.GetProperty("maintenanceMessageEnglish").GetString());
        Assert.Equal(
            [(int)PlatformPaymentMethod.Card, (int)PlatformPaymentMethod.Wallet],
            body.RootElement.GetProperty("availablePaymentMethods").EnumerateArray()
                .Select(value => value.GetInt32())
                .ToArray());

        foreach (string hidden in new[] { "version", "cardEnabled", "walletEnabled", "internalReason", "actor" })
            Assert.DoesNotContain(hidden, payload, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, true, true, true, new[] { 1, 2 })]
    [InlineData(true, true, true, false, new[] { 1 })]
    [InlineData(true, true, false, true, new[] { 2 })]
    [InlineData(true, true, false, false, new int[0])]
    [InlineData(false, false, true, true, new int[0])]
    public async Task PublicStatus_AdvertisesOnlyEnabledAndConfiguredMethods(
        bool cardEnabled,
        bool walletEnabled,
        bool cardConfigured,
        bool walletConfigured,
        int[] expected)
    {
        await using FinanceDbContext db = CreateFinanceDb(
            cardEnabled: cardEnabled,
            walletEnabled: walletEnabled);
        var paymob = new PaymobOptions
        {
            SecretKey = "e04-test-secret",
            CardIntegrationId = cardConfigured ? "card-integration" : string.Empty,
            WalletIntegrationId = walletConfigured ? "wallet-integration" : string.Empty
        };
        using var factory = CreateFactory(db, paymob);
        using HttpClient client = CreatePortalClient(factory);

        using HttpResponseMessage response = await client.GetAsync(PublicRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            expected,
            body.RootElement.GetProperty("availablePaymentMethods").EnumerateArray()
                .Select(value => value.GetInt32())
                .ToArray());
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("card-3ds-enrollment-integration", true)]
    public async Task PaymentMethodConfiguration_RequiresTheEnrollmentIntegrationOnlyForTheEnrollmentBranch(
        string card3dsIntegrationId,
        bool enrollmentConfigured)
    {
        await using FinanceDbContext db = CreateFinanceDb();
        var paymob = new PaymobOptions
        {
            SecretKey = "e04-test-secret",
            CardIntegrationId = "card-integration",
            Card3dsIntegrationId = card3dsIntegrationId,
            WalletIntegrationId = "wallet-integration"
        };
        using var factory = CreateFactory(db, paymob);
        using HttpClient client = CreatePortalClient(factory);

        IPaymentMethodConfiguration configuration =
            factory.Services.GetRequiredService<IPaymentMethodConfiguration>();

        // The generic Card truth stays driven by the generic Card integration id,
        // so the enrollment prerequisite neither adds nor removes an advertised
        // method: the public list is unchanged.
        Assert.True(configuration.CardConfigured);
        Assert.True(configuration.WalletConfigured);
        Assert.Equal(enrollmentConfigured, configuration.CardSubscriptionEnrollmentConfigured);

        using HttpResponseMessage response = await client.GetAsync(PublicRoute);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            [1, 2],
            body.RootElement.GetProperty("availablePaymentMethods").EnumerateArray()
                .Select(value => value.GetInt32())
                .ToArray());
    }

    [Fact]
    public async Task PublicStatus_WithoutAConfiguredGatewaySecret_AdvertisesNoMethod()
    {
        await using FinanceDbContext db = CreateFinanceDb();
        using var factory = CreateFactory(
            db,
            new PaymobOptions
            {
                SecretKey = string.Empty,
                CardIntegrationId = "card-integration",
                WalletIntegrationId = "wallet-integration"
            });
        using HttpClient client = CreatePortalClient(factory);

        using HttpResponseMessage response = await client.GetAsync(PublicRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(body.RootElement.GetProperty("availablePaymentMethods").EnumerateArray());
    }

    [Fact]
    public async Task PlatformControlsPolicy_RequiresANormalAuthenticatedSuperAdmin()
    {
        IAuthorizationPolicyProvider provider =
            _host.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        AuthorizationPolicy? policy = await provider.GetPolicyAsync(AuthorizationPolicies.PlatformControlsAdmin);

        Assert.NotNull(policy);
        Assert.Single(policy!.Requirements.OfType<DenyAnonymousAuthorizationRequirement>());

        ClaimsAuthorizationRequirement accountType = Assert.Single(
            policy.Requirements.OfType<ClaimsAuthorizationRequirement>(),
            requirement => requirement.ClaimType == AuthClaimNames.AccountType);
        Assert.Equal([nameof(AccountType.SuperAdmin)], accountType.AllowedValues!);

        ClaimsAuthorizationRequirement accessType = Assert.Single(
            policy.Requirements.OfType<ClaimsAuthorizationRequirement>(),
            requirement => requirement.ClaimType == AuthClaimNames.AccessType);
        Assert.Equal([nameof(AuthAccessType.Normal)], accessType.AllowedValues!);
    }

    private static PaymobOptions ConfiguredGateway() => new()
    {
        SecretKey = "e04-test-secret",
        CardIntegrationId = "card-integration",
        WalletIntegrationId = "wallet-integration"
    };

    private static FinanceDbContext CreateFinanceDb(
        bool seeded = true,
        PlatformState state = PlatformState.Operational,
        bool cardEnabled = true,
        bool walletEnabled = true,
        string? messageArabic = null,
        string? messageEnglish = null)
    {
        var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        db.Database.EnsureCreated();

        if (seeded)
        {
            db.PlatformControlStates.Add(PlatformControlState.Create(
                SeedId,
                state,
                messageArabic,
                messageEnglish,
                cardEnabled,
                walletEnabled,
                version: 1,
                SeededOnUtc));
            db.SaveChanges();
        }

        return db;
    }

    private WebApplicationFactory<Program> CreateFactory(FinanceDbContext db, PaymobOptions? paymob = null) =>
        _host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                // The production module registrations capture the configured
                // (dummy, unreachable) connection string, so the Finance
                // application boundary is re-pointed at an isolated InMemory
                // store that this test owns.
                services.RemoveAll<IFinanceDbContext>();
                services.AddSingleton<IFinanceDbContext>(db);

                if (paymob is not null)
                {
                    services.RemoveAll<IOptions<PaymobOptions>>();
                    services.AddSingleton<IOptions<PaymobOptions>>(Options.Create(paymob));
                }
            }));

    private static HttpClient CreatePortalClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private (User User, string Token) CreateActor(AccountType accountType, string email)
    {
        User user = User.Create(
            FullName.Create("مستخدم"),
            FullName.Create("User"),
            Email.Create(email),
            PhoneNumber.Create("+201000004100"));
        user.AddAccount(accountType);

        IAuthTokenService tokens = _host.Services.GetRequiredService<IAuthTokenService>();
        string token = tokens.GenerateAccessToken(user, DateTime.UtcNow.AddSeconds(-5)).PlainTextToken;

        return (user, token);
    }
}
