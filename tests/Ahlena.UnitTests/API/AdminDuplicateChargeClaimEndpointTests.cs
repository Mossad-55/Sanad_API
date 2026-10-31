using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ahlena.API;
using Ahlena.API.Authorization;
using Ahlena.API.Controllers;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Families.Application.Subscriptions;
using Ahlena.Modules.Finance.Application;
using Ahlena.Modules.Finance.Domain;
using Ahlena.Modules.Identity.Application.Abstractions.Security;
using Ahlena.Modules.Identity.Application.Authentication.Tokens;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.UnitTests.API;

/// <summary>
/// Endpoint-level contract for the duplicate-charge support-claim surfaces;
/// executed in the combined focused and full Release suites.
/// <para>
/// Isolated on the shared <see cref="PortalApiMetadataHost"/> (production
/// controllers, policies and OpenAPI registration; unreachable dummy stores and
/// no seeders/jobs) with the sender replaced by a deterministic double, so the
/// production MVC pipeline is what runs while no database, provider, migration
/// or seeder is reachable. One file by design: the admin and family surfaces are
/// the two ends of one workflow, and the policy matrix, seam wiring, family
/// opt-in contract and DTO privacy ban share the host/sender/token scaffolding.
/// </para>
/// </summary>
public sealed class AdminDuplicateChargeClaimEndpointTests : IClassFixture<PortalApiMetadataHost>
{
    private const string AdminPath = "/api/v1/admin/finance/subscription-refunds/duplicate-charge";
    private const string FamilySubmitPath = "/api/v1/family/subscriptions/refunds/duplicate-charge";

    private static readonly Guid ClaimId = Guid.Parse("0198e2c1-4444-7777-8888-000000000004");

    private static readonly string[] FamilyForbiddenTokens =
    [
        "verifiedBasis", "decisionNote", "executionAttempt", "executedBy",
        "executionAward", "gatewayError", "providerRefund", "notMoved", "decidedBy"
    ];

    private readonly PortalApiMetadataHost _host;
    private JsonDocument? _document;

    public AdminDuplicateChargeClaimEndpointTests(PortalApiMetadataHost host) => _host = host;

    // ------------------------------------------------------------------
    // Static pins: routes, policies, verbs, status codes.
    // ------------------------------------------------------------------

    [Fact]
    public void AdminSurface_IsGatedByRefundWorkflowAdmin_OnThePinnedRoute()
    {
        var authorize = typeof(AdminFinanceDuplicateChargeClaimsController)
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(AuthorizationPolicies.RefundWorkflowAdmin, authorize!.Policy);
        Assert.Equal(
            AdminPath.TrimStart('/'),
            typeof(AdminFinanceDuplicateChargeClaimsController)
                .GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Empty(typeof(AdminFinanceDuplicateChargeClaimsController)
            .GetCustomAttributes<AllowAnonymousAttribute>());
    }

    [Fact]
    public void ExecuteAndReconcile_CarryTheNestedSuperAdminOnlyPolicy()
    {
        Type controller = typeof(AdminFinanceDuplicateChargeClaimsController);

        foreach (string action in new[] { nameof(AdminFinanceDuplicateChargeClaimsController.Execute),
                     nameof(AdminFinanceDuplicateChargeClaimsController.Reconcile) })
        {
            var nested = controller.GetMethod(action)!
                .GetCustomAttributes<AuthorizeAttribute>()
                .Single();
            Assert.Equal(AuthorizationPolicies.SuperAdminOnly, nested.Policy);
        }

        // The review actions carry no nested policy: SuperAdmin + SupportAdmin decide.
        foreach (string action in new[] { nameof(AdminFinanceDuplicateChargeClaimsController.Approve),
                     nameof(AdminFinanceDuplicateChargeClaimsController.Reject) })
        {
            Assert.Empty(controller.GetMethod(action)!.GetCustomAttributes<AuthorizeAttribute>());
        }
    }

    [Fact]
    public void AdminSurface_VerbsAndStatusCodes_ArePinned()
    {
        Type controller = typeof(AdminFinanceDuplicateChargeClaimsController);

        Assert.Equal<int>([200, 403],
            StatusCodesOf(controller, nameof(AdminFinanceDuplicateChargeClaimsController.List)));
        Assert.Equal<int>([200, 403, 404],
            StatusCodesOf(controller, nameof(AdminFinanceDuplicateChargeClaimsController.Detail)));
        Assert.Equal<int>([200, 400, 403, 404, 409],
            StatusCodesOf(controller, nameof(AdminFinanceDuplicateChargeClaimsController.Approve)));
        Assert.Equal<int>([200, 400, 403, 404, 409],
            StatusCodesOf(controller, nameof(AdminFinanceDuplicateChargeClaimsController.Reject)));
        Assert.Equal<int>([200, 400, 403, 404, 409],
            StatusCodesOf(controller, nameof(AdminFinanceDuplicateChargeClaimsController.Execute)));
        Assert.Equal<int>([200, 400, 403, 404, 409],
            StatusCodesOf(controller, nameof(AdminFinanceDuplicateChargeClaimsController.Reconcile)));

        Assert.Equal(
            ["GET", "GET", "POST", "POST", "POST", "POST"],
            controller.GetMethods()
                .Where(method => method.DeclaringType == controller)
                .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
                .SelectMany(attribute => attribute.HttpMethods)
                .OrderBy(verb => verb, StringComparer.Ordinal)
                .ToArray());
    }

    // ------------------------------------------------------------------
    // Policy registration pins: the reviewer/executor account split.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Policies_PinTheReviewerAndExecutorAccountSplits()
    {
        AuthorizationPolicy reviewer = await ResolvePolicyAsync(AuthorizationPolicies.RefundWorkflowAdmin);
        Assert.Contains(reviewer.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);
        Assert.Equal<string>(
            [AccountType.SuperAdmin.ToString(), AccountType.SupportAdmin.ToString()],
            AccountValues(reviewer));
        Assert.DoesNotContain(AccountType.FinanceAdmin.ToString(), AccountValues(reviewer));
        Assert.DoesNotContain(AccountType.Family.ToString(), AccountValues(reviewer));

        AuthorizationPolicy executor = await ResolvePolicyAsync(AuthorizationPolicies.SuperAdminOnly);
        Assert.Contains(executor.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);
        Assert.Equal<string>([AccountType.SuperAdmin.ToString()], AccountValues(executor));
        Assert.DoesNotContain(AccountType.SupportAdmin.ToString(), AccountValues(executor));
        Assert.DoesNotContain(AccountType.FinanceAdmin.ToString(), AccountValues(executor));
    }

    // ------------------------------------------------------------------
    // Runtime policy matrix over the production pipeline.
    // ------------------------------------------------------------------

    [Fact]
    public Task Anonymous_Receives401_OnEveryAdminAction() =>
        AssertAdminStatusesAsync(null, HttpStatusCode.Unauthorized);

    [Fact]
    public Task FamilyTokens_AreForbidden_OnEveryAdminAction() =>
        AssertAdminStatusesAsync(nameof(AccountType.Family), HttpStatusCode.Forbidden);

    [Fact]
    public Task FinanceAdmin_IsDeliberatelyExcluded_OnEveryAdminAction() =>
        AssertAdminStatusesAsync(nameof(AccountType.FinanceAdmin), HttpStatusCode.Forbidden);

    [Fact]
    public Task SupportAdmin_DecidesButNeverMovesMoney() =>
        AssertAdminStatusesAsync(nameof(AccountType.SupportAdmin), HttpStatusCode.OK, HttpStatusCode.Forbidden);

    [Fact]
    public Task SuperAdmin_DecidesAndMovesMoney() =>
        AssertAdminStatusesAsync(nameof(AccountType.SuperAdmin), HttpStatusCode.OK);

    [Fact]
    public async Task TheSeam_ReceivesTheForwardedAccountType_NeverARotatedRole()
    {
        DateTime beforeUtc = DateTime.UtcNow;
        DuplicateChargeClaimSender sender = new();
        using WebApplicationFactory<Program> factory = OverrideSender(_host, sender);
        using HttpClient client = CreateClient(factory);

        // SupportAdmin passes the review policy and reaches the seam as a
        // reviewer; execute/reconcile carry SuperAdmin, and the nested policy
        // refuses SupportAdmin before the seam is ever consulted.
        using (HttpResponseMessage approve = await SendAsync(
                   client, HttpMethod.Post, $"{AdminPath}/{ClaimId}/approve",
                   new { verifiedBasis = "Provider statement 8842." },
                   Token(AccountType.SupportAdmin)))
        {
            Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
            Assert.Equal(AccountType.SupportAdmin.ToString(), sender.LastDecisionAccountType);
        }
        using (HttpResponseMessage rejected = await SendAsync(
                   client, HttpMethod.Post, $"{AdminPath}/{ClaimId}/reconcile",
                   new
                   {
                       refundDidMove = false,
                       evidence = "Provider enquiry 8842 states no refund was issued.",
                       notMovedProviderEvidenceReference = "provider-case-8842"
                   },
                   Token(AccountType.SupportAdmin)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
            Assert.Null(sender.LastMoneyAccountType);
        }

        using (HttpResponseMessage executed = await SendAsync(
                   client, HttpMethod.Post, $"{AdminPath}/{ClaimId}/execute", new { },
                   Token(AccountType.SuperAdmin)))
        {
            Assert.Equal(HttpStatusCode.OK, executed.StatusCode);
            Assert.Equal(AccountType.SuperAdmin.ToString(), sender.LastMoneyAccountType);
        }

        using (HttpResponseMessage reconciled = await SendAsync(
                   client, HttpMethod.Post, $"{AdminPath}/{ClaimId}/reconcile",
                   new
                   {
                       refundDidMove = false,
                       evidence = "Provider enquiry 8843 (operator-verified) states no refund exists.",
                       notMovedProviderEvidenceReference = "provider-case-8843"
                   },
                   Token(AccountType.SuperAdmin)))
        {
            Assert.Equal(HttpStatusCode.OK, reconciled.StatusCode);
            Assert.Equal(AccountType.SuperAdmin.ToString(), sender.LastMoneyAccountType);
        }

        // Server-authored audit instants (R2): the request DTOs expose no
        // timestamp member at all, the controller stamps them from the injected
        // clock, and the seam receives fresh UTC at action time — never a
        // client-authored value. The window tolerates slow test-host starts.
        DateTime afterUtc = DateTime.UtcNow;
        DateTime low = beforeUtc.AddSeconds(-30);
        DateTime high = afterUtc.AddSeconds(30);
        Assert.Equal(DateTimeKind.Utc, sender.LastDecisionUtc!.Value.Kind);
        Assert.InRange(sender.LastDecisionUtc!.Value, low, high);
        Assert.Equal(DateTimeKind.Utc, sender.LastExecuteUtc!.Value.Kind);
        Assert.InRange(sender.LastExecuteUtc!.Value, low, high);
        Assert.Equal(DateTimeKind.Utc, sender.LastReconcileUtc!.Value.Kind);
        Assert.InRange(sender.LastReconcileUtc!.Value, low, high);
    }

    [Fact]
    public void RequestDtos_CarryNoTimestampMembers_ClientsCannotAuthorAuditTime()
    {
        // Accepted design: decision/execution/reconciliation/completion audit
        // instants are server-authoritative, so no money-surface request DTO
        // exposes a timestamp member a client could fill.
        Assert.Equal<string>(
            ["DecisionNote", "FamilyReason", "VerifiedBasis"],
            PropertyNames(typeof(ApproveDuplicateChargeClaimRequest)));
        Assert.Equal<string>(
            ["DecisionNote", "FamilyReason"],
            PropertyNames(typeof(RejectDuplicateChargeClaimRequest)));
        Assert.Equal<string>(
            ["ExpectedExecutionAttemptId"],
            PropertyNames(typeof(ExecuteDuplicateChargeClaimRequest)));
        Assert.Equal<string>(
            [
                "Evidence", "ExpectedExecutionAttemptId", "MovedEvidenceReference",
                "NotMovedProviderEvidenceReference", "RefundDidMove",
                "VerifiedProviderRefundId"
            ],
            PropertyNames(typeof(ReconcileDuplicateChargeClaimRequest)));
    }

    // ------------------------------------------------------------------
    // Family surface: the retained legacy address and its opt-in contract.
    // ------------------------------------------------------------------

    [Fact]
    public async Task LegacyDuplicatePost_IsFailClosed_WithoutTheOptIn()
    {
        DuplicateChargeClaimSender sender = new();
        using WebApplicationFactory<Program> factory = OverrideSender(_host, sender);
        using HttpClient client = CreateClient(factory);
        string token = Token(AccountType.Family);

        using HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Post, FamilySubmitPath,
            new { paymentAttemptId = ClaimId, idempotencyKey = "dup-key" }, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "Subscriptions.Refund.MigratedToClaim",
            body.RootElement.GetProperty("code").GetString());

        // The retired path dispatched nothing: no verdict, no money movement.
        Assert.Null(sender.LastDecisionAccountType);
        Assert.Null(sender.LastMoneyAccountType);
        Assert.Null(sender.LastSubmitActorId);
    }

    [Fact]
    public async Task TheOptIn_SubmitsANewClaimAs201_AndReplaysAs200()
    {
        DuplicateChargeClaimSender sender = new();
        using WebApplicationFactory<Program> factory = OverrideSender(_host, sender);
        using HttpClient client = CreateClient(factory);
        string token = Token(AccountType.Family);
        object optInBody = new
        {
            paymentAttemptId = ClaimId,
            idempotencyKey = "dup-key",
            claimSubmission = true
        };

        using (HttpResponseMessage created = await SendAsync(
                   client, HttpMethod.Post, FamilySubmitPath, optInBody, token))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            string? location = created.Headers.Location?.ToString();
            Assert.True(
                location is not null &&
                location.EndsWith("/refunds/duplicate-charge/" + ClaimId, StringComparison.Ordinal),
                $"Expected a claim-detail Location header, saw '{location}'.");
            using JsonDocument createdBody =
                JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            Assert.False(createdBody.RootElement.GetProperty("isReplay").GetBoolean());
        }

        sender.FamilyReplay = true;
        using (HttpResponseMessage replay = await SendAsync(
                   client, HttpMethod.Post, FamilySubmitPath, optInBody, token))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            using JsonDocument body = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
            Assert.True(body.RootElement.GetProperty("isReplay").GetBoolean());
        }
    }

    [Fact]
    public async Task FamilyReads_AreOwnerScoped_AndProbeSafe()
    {
        DuplicateChargeClaimSender sender = new();
        using WebApplicationFactory<Program> factory = OverrideSender(_host, sender);
        using HttpClient client = CreateClient(factory);
        string token = Token(AccountType.Family);

        // A family's own claim and its own list read.
        using (HttpResponseMessage own = await SendAsync(
                   client, HttpMethod.Get, $"{FamilySubmitPath}/{ClaimId}", null, token))
        {
            Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        }

        using (HttpResponseMessage list = await SendAsync(
                   client, HttpMethod.Get, FamilySubmitPath, null, token))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        }

        // A foreign or unknown claim id: the same probe-safe 404, never a 403 leak.
        sender.FamilyFailure = DuplicateChargeClaimSubmissionErrors.ClaimNotFound;
        using (HttpResponseMessage foreign = await SendAsync(
                   client, HttpMethod.Get, $"{FamilySubmitPath}/{Guid.NewGuid()}", null, token))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            using JsonDocument body = JsonDocument.Parse(await foreign.Content.ReadAsStringAsync());
            Assert.Equal(
                "Subscriptions.Refund.ClaimNotFound",
                body.RootElement.GetProperty("code").GetString());
        }

        // A non-owner session is a 403, not a data read.
        sender.FamilyFailure = DuplicateChargeClaimSubmissionErrors.NotOwner;
        using (HttpResponseMessage denied = await SendAsync(
                   client, HttpMethod.Get, $"{FamilySubmitPath}/{ClaimId}", null, token))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
    }

    [Fact]
    public async Task TheFamilyView_SerializesOnlyTheCustomerSafeProjection()
    {
        DuplicateChargeClaimSender sender = new();
        using WebApplicationFactory<Program> factory = OverrideSender(_host, sender);
        using HttpClient client = CreateClient(factory);

        using HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Get, $"{FamilySubmitPath}/{ClaimId}", null, Token(AccountType.Family));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string raw = await response.Content.ReadAsStringAsync();
        using JsonDocument body = JsonDocument.Parse(raw);

        // Expected in ordinal order: the actual property names are compared sorted.
        Assert.Equal<string>(
        [
            "claimId", "claimState", "currency", "decidedOnUtc",
            "eligibleMinorUnits", "familyReason", "isReplay", "paymentAttemptId",
            "payoutStatus", "refundCaseId", "submittedOnUtc", "subscriptionId"
        ],
        [.. body.RootElement.EnumerateObject().Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)]);

        foreach (string forbidden in FamilyForbiddenTokens)
        {
            Assert.False(
                raw.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"The family view must not serialize {forbidden}.");
        }
    }

    [Fact]
    public void TheFamilyView_Type_NeverCarriesInternalFields()
    {
        // Expected in ordinal order: the actual property names are compared sorted.
        Assert.Equal<string>(
        [
            "ClaimId", "ClaimState", "Currency", "DecidedOnUtc",
            "EligibleMinorUnits", "FamilyReason", "IsReplay", "PaymentAttemptId",
            "PayoutStatus", "RefundCaseId", "SubmittedOnUtc", "SubscriptionId"
        ],
        [.. typeof(FamilyDuplicateChargeClaimView).GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)]);
    }

    // ------------------------------------------------------------------
    // OpenAPI contract: the pinned routes and the customer-safe schema.
    // ------------------------------------------------------------------

    [Fact]
    public void OpenApi_PublishesThePinnedClaimRoutesAndTheCustomerSafeFamilySchema()
    {
        JsonElement root = Document();

        Assert.Equal<string>(["get"], PathMethods(root, AdminPath));
        Assert.Equal<string>(["get"], PathMethods(root, $"{AdminPath}/{{claimId}}"));
        foreach (string action in new[] { "approve", "reject", "execute", "reconcile" })
        {
            Assert.Equal<string>(["post"], PathMethods(root, $"{AdminPath}/{{claimId}}/{action}"));
        }

        Assert.Equal<string>(["get", "post"], PathMethods(root, FamilySubmitPath));
        Assert.Equal<string>(["get"], PathMethods(root, $"{FamilySubmitPath}/{{claimId}}"));

        JsonElement familyView = ResolveRef(root, "#/components/schemas/FamilyDuplicateChargeClaimView")
            .GetProperty("properties");
        Assert.True(familyView.TryGetProperty("payoutStatus", out _));
        Assert.True(familyView.TryGetProperty("familyReason", out _));
        foreach (string forbidden in FamilyForbiddenTokens)
        {
            Assert.False(
                familyView.EnumerateObject()
                    .Any(property => property.Name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)),
                $"The published family schema must not contain {forbidden}.");
        }
    }

    // ------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------

    private static (HttpMethod Method, string Path, object? Body)[] AdminActions() =>
    [
        (HttpMethod.Get, $"{AdminPath}?state=1", null),
        (HttpMethod.Get, $"{AdminPath}/{ClaimId}", null),
        (HttpMethod.Post, $"{AdminPath}/{ClaimId}/approve",
            new { verifiedBasis = "Provider statement 8842 confirms the double capture." }),
        (HttpMethod.Post, $"{AdminPath}/{ClaimId}/reject",
            new { familyReason = "Reviewed; not a duplicate." }),
        (HttpMethod.Post, $"{AdminPath}/{ClaimId}/execute", new { }),
        (HttpMethod.Post, $"{AdminPath}/{ClaimId}/reconcile",
            new
            {
                refundDidMove = true,
                evidence = "Provider statement 8841 confirms the refund landed.",
                verifiedProviderRefundId = "rfn-8841"
            })
    ];

    private async Task AssertAdminStatusesAsync(
        string? accountType,
        HttpStatusCode reviewStatus,
        HttpStatusCode? moneyStatus = null)
    {
        DuplicateChargeClaimSender sender = new();
        using WebApplicationFactory<Program> factory = OverrideSender(_host, sender);
        using HttpClient client = CreateClient(factory);
        string? token = accountType is null
            ? null
            : Token(Enum.Parse<AccountType>(accountType));

        foreach ((HttpMethod method, string path, object? body) in AdminActions())
        {
            bool moneyAction = path.EndsWith("/execute", StringComparison.Ordinal) ||
                path.EndsWith("/reconcile", StringComparison.Ordinal);
            HttpStatusCode expected = moneyAction ? moneyStatus ?? reviewStatus : reviewStatus;

            using HttpResponseMessage response = await SendAsync(client, method, path, body, token);
            Assert.True(
                response.StatusCode == expected,
                $"{method} {path} as {accountType ?? "anonymous"} expected {(int)expected}, saw {(int)response.StatusCode}.");
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string path, object? body, string? token)
    {
        using HttpRequestMessage request = new(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    private static WebApplicationFactory<Program> OverrideSender(PortalApiMetadataHost host, ISender sender) =>
        host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISender>();
            services.AddSingleton(sender);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private string Token(AccountType accountType)
    {
        IAuthTokenService tokens = _host.Services.GetRequiredService<IAuthTokenService>();
        User user = User.Create(
            FullName.Create("Support Account"),
            FullName.Create($"Portal Claims {accountType}"),
            Email.Create($"claims-{accountType}@example.test"),
            PhoneNumber.Create("+201000000001"));
        user.AddAccount(accountType);
        return tokens.GenerateAccessToken(user, DateTime.UtcNow.AddSeconds(-5)).PlainTextToken;
    }

    private static async Task<AuthorizationPolicy> ResolvePolicyAsync(string name)
    {
        IServiceCollection services = new ServiceCollection();
        services.AddAhlenaApi(CreateConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });

        IAuthorizationPolicyProvider policyProvider =
            provider.GetRequiredService<IAuthorizationPolicyProvider>();

        AuthorizationPolicy? policy = await policyProvider.GetPolicyAsync(name);
        Assert.NotNull(policy);
        return policy!;
    }

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>(
                    "ConnectionStrings:IdentityDatabase",
                    "Host=localhost;Database=test;Username=test;Password=test"),
                new KeyValuePair<string, string?>("Identity:Jwt:Issuer", "Ahlena.Api"),
                new KeyValuePair<string, string?>("Identity:Jwt:Audience", "Ahlena.Clients"),
                new KeyValuePair<string, string?>(
                    "Identity:Jwt:SigningKey", "12345678901234567890123456789012")
            ])
            .Build();

    private static string[] AccountValues(AuthorizationPolicy policy) =>
        Assert.Single(
            policy.Requirements.OfType<ClaimsAuthorizationRequirement>(),
            requirement => requirement.ClaimType == AuthClaimNames.AccountType)
            .AllowedValues!.ToArray();

    private static int[] StatusCodesOf(Type controller, string action) =>
    [
        .. controller.GetMethod(action)!
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .OrderBy(status => status)
    ];

    private static string[] PropertyNames(Type type) =>
        [.. type.GetProperties().Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)];

    private JsonElement Document()
    {
        if (_document is null)
        {
            using HttpClient client = _host.CreatePortalClient();
            using HttpResponseMessage response =
                client.GetAsync("/openapi/v1.json").GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            _document = JsonDocument.Parse(
                response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        }

        return _document.RootElement;
    }

    private static string[] PathMethods(JsonElement root, string path)
    {
        Assert.True(
            root.GetProperty("paths").TryGetProperty(path, out JsonElement pathItem),
            $"Missing OpenAPI path {path}.");
        return
        [
            .. pathItem.EnumerateObject()
                .Select(property => property.Name)
                .Where(name => name is "get" or "post" or "put" or "delete" or "patch")
                .OrderBy(name => name, StringComparer.Ordinal)
        ];
    }

    private static JsonElement ResolveRef(JsonElement root, string reference)
    {
        const string prefix = "#/components/schemas/";
        Assert.StartsWith(prefix, reference);
        string name = reference[prefix.Length..];
        Assert.True(
            root.GetProperty("components").GetProperty("schemas").TryGetProperty(name, out JsonElement schema),
            $"Unresolvable schema reference {reference}.");
        return schema;
    }

    /// <summary>
    /// Deterministic dispatch double: canned DTOs only — no store, no gateway,
    /// no provider. It records the account types the seam receives so the
    /// reviewer/executor wiring is asserted at the HTTP boundary, and it pins
    /// every dispatched request to the duplicate-charge workflow.
    /// </summary>
    private sealed class DuplicateChargeClaimSender : ISender
    {
        public bool FamilyReplay { get; set; }

        public Error? FamilyFailure { get; set; }

        public string? LastDecisionAccountType { get; private set; }

        public string? LastMoneyAccountType { get; private set; }

        public DateTime? LastDecisionUtc { get; private set; }

        public DateTime? LastExecuteUtc { get; private set; }

        public DateTime? LastReconcileUtc { get; private set; }

        public string? LastSubmitActorId { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            object response = request switch
            {
                ListDuplicateChargeClaimsAdminQuery =>
                    Result<IReadOnlyList<DuplicateChargeClaimAdminView>>.Success(
                        [DuplicateChargeClaimSenderViews.AdminView()]),
                GetDuplicateChargeClaimAdminQuery =>
                    Result<DuplicateChargeClaimAdminView?>.Success(
                        DuplicateChargeClaimSenderViews.AdminView()),
                ApproveDuplicateChargeClaimCommand approve =>
                    StampDecision(approve.DecidedByAccountType, approve.DecidedOnUtc),
                RejectDuplicateChargeClaimCommand reject =>
                    StampDecision(reject.DecidedByAccountType, reject.DecidedOnUtc),
                ExecuteDuplicateChargeClaimCommand execute =>
                    StampExecution(execute.ExecutedByAccountType, execute.StartedOnUtc),
                ReconcileDuplicateChargeClaimCommand reconcile =>
                    StampReconciliation(reconcile.ReconciledByAccountType, reconcile.ReconciledOnUtc),

                SubmitDuplicateChargeClaimCommand submit =>
                    StampSubmitAndFamilyResult(submit.ActorUserId.Value),
                ListMyDuplicateChargeClaimsQuery => FamilyList(),
                GetMyDuplicateChargeClaimQuery => FamilyDetail(),

                _ => throw new NotSupportedException(
                    $"The double pins the duplicate-charge routes only; unexpected {request.GetType().Name}.")
            };

            return Task.FromResult((TResponse)response);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Only typed dispatch is expected on these surfaces.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException(
            "Only typed dispatch is expected on these surfaces.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("No streaming dispatch is expected on these surfaces.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("No streaming dispatch is expected on these surfaces.");

        private object StampDecision(string? accountType, DateTime decidedOnUtc)
        {
            LastDecisionAccountType = accountType;
            LastDecisionUtc = decidedOnUtc;
            return Result<DuplicateChargeClaimAdminView>.Success(
                DuplicateChargeClaimSenderViews.AdminView());
        }

        private object StampExecution(string? accountType, DateTime startedOnUtc)
        {
            LastMoneyAccountType = accountType;
            LastExecuteUtc = startedOnUtc;
            return Result<DuplicateChargeClaimAdminView>.Success(
                DuplicateChargeClaimSenderViews.AdminView());
        }

        private object StampReconciliation(string? accountType, DateTime reconciledOnUtc)
        {
            LastMoneyAccountType = accountType;
            LastReconcileUtc = reconciledOnUtc;
            return Result<DuplicateChargeClaimAdminView>.Success(
                DuplicateChargeClaimSenderViews.AdminView());
        }

        private object StampSubmitAndFamilyResult(Guid actorUserId)
        {
            LastSubmitActorId = actorUserId.ToString();
            if (FamilyFailure is not null)
            {
                return Result<FamilyDuplicateChargeClaimView>.Failure(FamilyFailure);
            }

            return Result<FamilyDuplicateChargeClaimView>.Success(
                DuplicateChargeClaimSenderViews.FamilyView(FamilyReplay));
        }

        private object FamilyList()
        {
            if (FamilyFailure is not null)
            {
                return Result<IReadOnlyList<FamilyDuplicateChargeClaimView>>.Failure(FamilyFailure);
            }

            return Result<IReadOnlyList<FamilyDuplicateChargeClaimView>>.Success(
                [DuplicateChargeClaimSenderViews.FamilyView(FamilyReplay)]);
        }

        private object FamilyDetail()
        {
            if (FamilyFailure is not null)
            {
                return Result<FamilyDuplicateChargeClaimView>.Failure(FamilyFailure);
            }

            return Result<FamilyDuplicateChargeClaimView>.Success(
                DuplicateChargeClaimSenderViews.FamilyView(FamilyReplay));
        }
    }

    /// <summary>Canned DTOs shared by the double (typed views, no database).</summary>
    private static class DuplicateChargeClaimSenderViews
    {
        private static readonly DateTime SubmittedUtc =
            new(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);

        private static readonly DateTime DecidedUtc =
            new(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc);

        public static DuplicateChargeClaimAdminView AdminView() => new(
            Guid.Parse("0198e2c1-4444-7777-8888-000000000004"),
            Guid.Parse("0198e2c1-3333-7777-8888-000000000001"),
            Guid.Parse("0198e2c1-1111-7777-8888-000000000001"),
            Guid.Parse("0198e2c1-1111-7777-8888-000000000002"),
            Guid.Parse("0198e2c1-2222-7777-8888-000000000001"),
            34086,
            "EGP",
            DuplicateChargeClaimState.Approved,
            DuplicateChargeExecutionState.AwaitingExecution,
            "dup-key",
            "screen-x-1",
            SubmittedUtc,
            SubmittedUtc,
            Guid.Parse("0198e2c1-5555-7777-8888-000000000001"),
            "SupportAdmin",
            DecidedUtc,
            "This was the same charge twice.",
            null,
            "Provider statement 8842 confirms a true double capture.",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            0,
            SubmittedUtc,
            DecidedUtc);

        public static FamilyDuplicateChargeClaimView FamilyView(bool isReplay) => new(
            Guid.Parse("0198e2c1-4444-7777-8888-000000000004"),
            Guid.Parse("0198e2c1-3333-7777-8888-000000000001"),
            Guid.Parse("0198e2c1-2222-7777-8888-000000000001"),
            Guid.Parse("0198e2c1-1111-7777-8888-000000000002"),
            34086,
            "EGP",
            DuplicateChargeClaimState.Submitted,
            "pending",
            null,
            SubmittedUtc,
            null,
            isReplay);
    }
}
