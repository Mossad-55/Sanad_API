using System.Reflection;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sanad.API;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.CareHomes.Application.Visits;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.API;

/// <summary>
/// F6 operational-read HTTP/metadata contract. Both new controllers are read
/// only (<c>get</c> only), are pinned to their existing account policies
/// (SuperAdmin + SupportAdmin, never Finance, ContentAdmin or any app audience),
/// bind only the documented filters with clamped paging, and publish exactly the
/// pinned response fields. The generated OpenAPI document is the real one served
/// by the isolated <see cref="PortalApiMetadataHost"/>, so the published shapes
/// are the runtime shapes.
/// </summary>
public sealed class AdminOperationalReadAuthorizationTests : IClassFixture<PortalApiMetadataHost>
{
    private const string FamiliesPath = "/api/v1/admin/families";
    private const string FamilyDetailPath = "/api/v1/admin/families/{familyId}";
    private const string VisitsPath = "/api/v1/admin/care-homes/visits";
    private const string VisitDetailPath = "/api/v1/admin/care-homes/visits/{visitId}";

    // The controllers declare their route templates without a leading slash, the
    // repository-wide convention; the published OpenAPI paths above do carry one.
    private const string FamiliesRouteTemplate = "api/v1/admin/families";
    private const string VisitsRouteTemplate = "api/v1/admin/care-homes/visits";

    private readonly PortalApiMetadataHost _host;
    private JsonDocument? _document;

    public AdminOperationalReadAuthorizationTests(PortalApiMetadataHost host) => _host = host;

    [Fact]
    public void FamilyReadController_UsesOperationalReadAdminPolicy_AndExposesGetOnly()
    {
        var authorize = typeof(AdminFamiliesController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(AuthorizationPolicies.OperationalReadAdmin, authorize!.Policy);
        Assert.Equal(
            FamiliesRouteTemplate,
            typeof(AdminFamiliesController).GetCustomAttribute<RouteAttribute>()!.Template);

        MethodInfo[] actions = DeclaredActions(typeof(AdminFamiliesController));
        Assert.Equal(2, actions.Length);

        foreach (MethodInfo action in actions)
        {
            Assert.Single(action.GetCustomAttributes<HttpMethodAttribute>(), attribute => attribute is HttpGetAttribute);
        }
    }

    [Fact]
    public void FamilyListRoute_BindsClampedPagingAndSearchAndPublishesOnlyOk()
    {
        MethodInfo action = typeof(AdminFamiliesController)
            .GetMethod(nameof(AdminFamiliesController.List))!;

        Assert.Equal(1, Parameter(action, "page").DefaultValue);
        Assert.Equal(25, Parameter(action, "pageSize").DefaultValue);
        Assert.Equal(typeof(string), Parameter(action, "search").ParameterType);

        foreach (string name in new[] { "page", "pageSize", "search" })
        {
            Assert.True(
                Parameter(action, name).GetCustomAttributes<FromQueryAttribute>().Any(),
                $"{name} must stay a query parameter.");
        }

        Assert.Equal(
            new[] { StatusCodes.Status200OK },
            StatusCodesOf(action));
    }

    [Fact]
    public void FamilyDetailRoute_TakesAFamilyIdAndPublishesNotFound()
    {
        MethodInfo action = typeof(AdminFamiliesController)
            .GetMethod(nameof(AdminFamiliesController.Detail))!;

        Assert.Equal(typeof(Guid), Parameter(action, "familyId").ParameterType);
        Assert.Equal("{familyId:guid}", TemplateOf(action));
        Assert.Equal<int>(
            [StatusCodes.Status200OK, StatusCodes.Status404NotFound],
            StatusCodesOf(action));
    }

    [Fact]
    public void VisitReadController_UsesCareHomesOperationalAdminPolicy_AndExposesGetOnly()
    {
        var authorize = typeof(AdminCareHomeVisitsController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(AuthorizationPolicies.CareHomesOperationalAdmin, authorize!.Policy);
        Assert.Equal(
            VisitsRouteTemplate,
            typeof(AdminCareHomeVisitsController).GetCustomAttribute<RouteAttribute>()!.Template);

        MethodInfo[] actions = DeclaredActions(typeof(AdminCareHomeVisitsController));
        Assert.Equal(2, actions.Length);

        foreach (MethodInfo action in actions)
        {
            Assert.Single(action.GetCustomAttributes<HttpMethodAttribute>(), attribute => attribute is HttpGetAttribute);
        }
    }

    [Fact]
    public void VisitListRoute_BindsFacilityStatusWindowAndPagingOnly()
    {
        MethodInfo action = typeof(AdminCareHomeVisitsController)
            .GetMethod(nameof(AdminCareHomeVisitsController.List))!;

        Assert.Equal(1, Parameter(action, "page").DefaultValue);
        Assert.Equal(25, Parameter(action, "pageSize").DefaultValue);
        Assert.Equal(typeof(Guid?), Parameter(action, "facilityId").ParameterType);
        Assert.Equal(typeof(CareHomeVisitStatus?), Parameter(action, "status").ParameterType);
        Assert.Equal(typeof(DateOnly?), Parameter(action, "from").ParameterType);
        Assert.Equal(typeof(DateOnly?), Parameter(action, "to").ParameterType);

        foreach (string name in new[] { "page", "pageSize", "facilityId", "status", "from", "to" })
        {
            Assert.True(
                Parameter(action, name).GetCustomAttributes<FromQueryAttribute>().Any(),
                $"{name} must stay a query parameter.");
        }

        Assert.Null(Parameter(action, "facilityId").DefaultValue);
        Assert.Null(Parameter(action, "status").DefaultValue);
        Assert.Null(Parameter(action, "from").DefaultValue);
        Assert.Null(Parameter(action, "to").DefaultValue);

        Assert.Equal<int>(
            [StatusCodes.Status200OK, StatusCodes.Status400BadRequest],
            StatusCodesOf(action));
    }

    [Fact]
    public void VisitDetailRoute_TakesAVisitIdAndPublishesNotFound()
    {
        MethodInfo action = typeof(AdminCareHomeVisitsController)
            .GetMethod(nameof(AdminCareHomeVisitsController.Detail))!;

        Assert.Equal(typeof(Guid), Parameter(action, "visitId").ParameterType);
        Assert.Equal("{visitId:guid}", TemplateOf(action));
        Assert.Equal<int>(
            [StatusCodes.Status200OK, StatusCodes.Status404NotFound],
            StatusCodesOf(action));
    }

    [Fact]
    public async Task OperationalReadAdmin_AllowsSuperAdminAndSupportAdminOnly()
    {
        AuthorizationPolicy policy = await ResolvePolicyAsync(AuthorizationPolicies.OperationalReadAdmin);

        Assert.Contains(policy.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);

        ClaimsAuthorizationRequirement access = Assert.Single(
            policy.Requirements.OfType<ClaimsAuthorizationRequirement>(),
            requirement => requirement.ClaimType == AuthClaimNames.AccessType);
        Assert.Equal<string>([AuthAccessType.Normal.ToString()], access.AllowedValues!);

        ClaimsAuthorizationRequirement account = Assert.Single(
            policy.Requirements.OfType<ClaimsAuthorizationRequirement>(),
            requirement => requirement.ClaimType == AuthClaimNames.AccountType);

        Assert.NotNull(account.AllowedValues);
        Assert.Equal<string>(
            [AccountType.SuperAdmin.ToString(), AccountType.SupportAdmin.ToString()],
            account.AllowedValues!);

        // No widening: Finance/Content and every app audience stay out.
        foreach (AccountType excluded in new[]
                 {
                     AccountType.FinanceAdmin, AccountType.ContentAdmin, AccountType.Family,
                     AccountType.Elderly, AccountType.MedicalCaregiver,
                     AccountType.CompanionCaregiver, AccountType.CareHomeOwner
                 })
        {
            Assert.DoesNotContain(excluded.ToString(), account.AllowedValues!);
        }
    }

    [Fact]
    public async Task CareHomesOperationalAdmin_KeepsTheSiblingAdminBookingsPolicySet()
    {
        AuthorizationPolicy policy = await ResolvePolicyAsync(AuthorizationPolicies.CareHomesOperationalAdmin);

        ClaimsAuthorizationRequirement account = Assert.Single(
            policy.Requirements.OfType<ClaimsAuthorizationRequirement>(),
            requirement => requirement.ClaimType == AuthClaimNames.AccountType);

        Assert.NotNull(account.AllowedValues);
        Assert.Equal<string>(
            [AccountType.SuperAdmin.ToString(), AccountType.SupportAdmin.ToString()],
            account.AllowedValues!);
    }

    [Fact]
    public async Task FamilyController_ForwardsListAndDetailQueriesAndReturnsThePayload()
    {
        var page = new AdminFamilyPage(
            [
                new AdminFamilyListItem(
                    Guid.CreateVersion7(), "Al Noor", Guid.CreateVersion7(), 2,
                    new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), null)
            ],
            2, 10, 1);
        var listSender = new StubSender { Response = Result<AdminFamilyPage>.Success(page) };

        IActionResult listResult = await new AdminFamiliesController(listSender).List(
            page: 2, pageSize: 10, search: "noor", cancellationToken: default);

        var ok = Assert.IsType<OkObjectResult>(listResult);
        Assert.Same(page, ok.Value);
        var listQuery = Assert.IsType<ListAdminFamiliesQuery>(listSender.LastRequest);
        Assert.Equal(2, listQuery.Page);
        Assert.Equal(10, listQuery.PageSize);
        Assert.Equal("noor", listQuery.Search);

        Guid familyId = Guid.CreateVersion7();
        var detail = new AdminFamilyDetail(
            familyId, "Al Noor", Guid.CreateVersion7(), 1,
            new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), null, [], []);
        var detailSender = new StubSender { Response = Result<AdminFamilyDetail>.Success(detail) };

        IActionResult detailResult = await new AdminFamiliesController(detailSender)
            .Detail(familyId, default);

        Assert.Same(detail, Assert.IsType<OkObjectResult>(detailResult).Value);
        Assert.Equal(familyId, Assert.IsType<GetAdminFamilyQuery>(detailSender.LastRequest).FamilyId);
    }

    [Fact]
    public async Task VisitController_ForwardsListAndDetailQueriesAndMapsNotFound()
    {
        var page = new AdminCareHomeVisitPage(
            [
                new AdminCareHomeVisitListItem(
                    Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null,
                    CareHomeVisitKind.Prospective, CareHomeVisitStatus.Pending,
                    new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc),
                    null, null, null, null, null, null, null,
                    new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc))
            ],
            1, 25, 1);
        var listSender = new StubSender { Response = Result<AdminCareHomeVisitPage>.Success(page) };
        Guid facilityId = Guid.CreateVersion7();

        IActionResult listResult = await new AdminCareHomeVisitsController(listSender).List(
            page: 1,
            pageSize: 25,
            facilityId: facilityId,
            status: CareHomeVisitStatus.Pending,
            from: new DateOnly(2026, 10, 20),
            to: new DateOnly(2026, 10, 20),
            cancellationToken: default);

        Assert.Same(page, Assert.IsType<OkObjectResult>(listResult).Value);
        var listQuery = Assert.IsType<ListAdminCareHomeVisitsQuery>(listSender.LastRequest);
        Assert.Equal<Guid?>(facilityId, listQuery.FacilityId);
        Assert.Equal<CareHomeVisitStatus?>(CareHomeVisitStatus.Pending, listQuery.Status);
        Assert.Equal<DateOnly?>(new DateOnly(2026, 10, 20), listQuery.From);
        Assert.Equal<DateOnly?>(new DateOnly(2026, 10, 20), listQuery.To);

        var detailSender = new StubSender
        {
            Response = Result<AdminCareHomeVisitDetail>.Failure(CareHomeVisitErrors.NotFound)
        };
        var controller = new AdminCareHomeVisitsController(detailSender)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        Guid visitId = Guid.CreateVersion7();
        IActionResult detailResult = await controller.Detail(visitId, default);

        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(detailResult).Value);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Equal(CareHomeVisitErrors.NotFound.Code, problem.Extensions["code"]);
        Assert.Equal(visitId, Assert.IsType<GetAdminCareHomeVisitQuery>(detailSender.LastRequest).VisitId);
    }

    [Fact]
    public void OpenApi_OperationalReadPathsExistAndAreReadOnly()
    {
        JsonElement root = Document();

        Assert.Equal<string>(["get"], PathMethods(root, FamiliesPath));
        Assert.Equal<string>(["get"], PathMethods(root, FamilyDetailPath));
        Assert.Equal<string>(["get"], PathMethods(root, VisitsPath));
        Assert.Equal<string>(["get"], PathMethods(root, VisitDetailPath));
    }

    [Fact]
    public void OpenApi_AdminFamilyListPublishesTheExactPinnedFields()
    {
        JsonElement root = Document();
        JsonElement get = GetOperation(root, FamiliesPath, "get");

        Assert.True(
            get.GetProperty("responses").TryGetProperty("200", out JsonElement ok),
            $"Missing 200 response for GET {FamiliesPath}.");
        Assert.True(TryGetJsonSchema(ok, out JsonElement schema), "Missing 200 JSON schema.");
        Assert.EndsWith("AdminFamilyPage", AssertRef(schema, root));

        JsonElement page = ResolveRef(root, AssertRef(schema, root)).GetProperty("properties");
        AssertExactProperties(page, "items", "page", "pageSize", "totalCount");

        JsonElement items = page.GetProperty("items");
        Assert.Equal("array", items.GetProperty("type").GetString());
        Assert.EndsWith("AdminFamilyListItem", AssertRef(items.GetProperty("items"), root));

        JsonElement item = ResolveRef(root, "#/components/schemas/AdminFamilyListItem").GetProperty("properties");
        AssertExactProperties(
            item, "familyId", "name", "ownerUserId", "memberCount", "createdOnUtc", "deletedOnUtc");

        Assert.False(PermitsNull(item.GetProperty("familyId")));
        Assert.False(PermitsNull(item.GetProperty("name")));
        Assert.False(PermitsNull(item.GetProperty("ownerUserId")));
        Assert.False(PermitsNull(item.GetProperty("memberCount")));
        Assert.False(PermitsNull(item.GetProperty("createdOnUtc")));
        Assert.True(PermitsNull(item.GetProperty("deletedOnUtc")));

        // The safe-field exclusion is part of the published contract: no
        // contact, clinical or deletion-reason field is ever in this schema.
        AssertNoPropertyMatching(
            item,
            "deletionReason", "deletionMessage", "email", "phone", "address",
            "health", "medical", "assessment", "subscription", "search");

        JsonElement detail = ResolveRef(root, "#/components/schemas/AdminFamilyDetail").GetProperty("properties");
        foreach (string name in new[] { "members", "dependents", "familyId", "name", "ownerUserId", "deletedOnUtc" })
        {
            Assert.True(detail.TryGetProperty(name, out _), $"Missing AdminFamilyDetail.{name}.");
        }

        JsonElement member = ResolveRef(root, "#/components/schemas/AdminFamilyMemberItem").GetProperty("properties");
        AssertExactProperties(member, "userId", "role", "relationshipType", "addedByUserId", "joinedOnUtc", "status");

        JsonElement dependent = ResolveRef(root, "#/components/schemas/AdminFamilyDependentItem").GetProperty("properties");
        AssertExactProperties(dependent, "elderlyId", "identityUserId", "relationshipType");
    }

    [Fact]
    public void OpenApi_AdminVisitListPublishesTheExactPinnedFields()
    {
        JsonElement root = Document();
        JsonElement get = GetOperation(root, VisitsPath, "get");

        Assert.True(get.GetProperty("responses").TryGetProperty("400", out _), "The invalid-query rejection stays published.");
        Assert.True(get.GetProperty("responses").TryGetProperty("200", out JsonElement ok));
        Assert.True(TryGetJsonSchema(ok, out JsonElement schema));

        JsonElement page = ResolveRef(root, AssertRef(schema, root)).GetProperty("properties");
        AssertExactProperties(page, "items", "page", "pageSize", "totalCount");

        JsonElement items = page.GetProperty("items");
        Assert.EndsWith("AdminCareHomeVisitListItem", AssertRef(items.GetProperty("items"), root));

        JsonElement item = ResolveRef(root, "#/components/schemas/AdminCareHomeVisitListItem").GetProperty("properties");
        AssertExactProperties(
            item,
            "visitId", "facilityId", "familyId", "elderlyId", "kind", "status",
            "startsAtUtc", "endsAtUtc", "rescheduleOfVisitId",
            "decidedByUserId", "decidedOnUtc", "decisionReason",
            "cancelledByUserId", "cancelledOnUtc", "cancellationReason", "createdOnUtc");

        Assert.True(PermitsNull(item.GetProperty("elderlyId")));
        Assert.True(PermitsNull(item.GetProperty("decisionReason")));
        Assert.True(PermitsNull(item.GetProperty("cancellationReason")));
        Assert.False(PermitsNull(item.GetProperty("visitId")));
        Assert.False(PermitsNull(item.GetProperty("status")));

        // Visitor PII, visitor count, the requesting family user and the pending
        // decision deadline are never in this schema.
        AssertNoPropertyMatching(
            item,
            "visitorName", "visitorPhone", "visitorCount", "familyUserId",
            "decisionExpiresOnUtc", "version");

        JsonElement detail = ResolveRef(root, "#/components/schemas/AdminCareHomeVisitDetail").GetProperty("properties");
        Assert.True(detail.TryGetProperty("updatedOnUtc", out _), "Missing AdminCareHomeVisitDetail.updatedOnUtc.");

        Dictionary<string, JsonElement> parameters = get.GetProperty("parameters")
            .EnumerateArray()
            .ToDictionary(parameter => parameter.GetProperty("name").GetString()!);
        foreach (string name in new[] { "page", "pageSize", "facilityId", "status", "from", "to" })
        {
            Assert.True(parameters.ContainsKey(name), $"Missing {name} query parameter.");
            Assert.False(
                parameters[name].TryGetProperty("required", out JsonElement required) && required.GetBoolean(),
                $"{name} stays optional on the visit read.");
        }
    }

    [Fact]
    public void OpenApi_CaregiverBookingDetail_PublishesTheF6FactFieldsInsideTheUnchangedBoundary()
    {
        JsonElement root = Document();

        // The caregiver-only route and its policy are unchanged.
        Assert.Equal<string>(
            ["get"],
            PathMethods(root, "/api/v1/caregiver/bookings/{bookingId}"));
        var authorize = typeof(CaregiverBookingsController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal(AuthorizationPolicies.CaregiverAccess, authorize!.Policy);

        JsonElement detail = ResolveRef(root, "#/components/schemas/BookingDetailResponse")
            .GetProperty("properties");

        foreach (string name in new[]
                 {
                     "refundState", "cancellationActorSide", "cancellationAction",
                     "cancellationReasonCategory", "refundEntitlement", "refundDecisionReason"
                 })
        {
            Assert.True(detail.TryGetProperty(name, out _), $"Missing BookingDetailResponse.{name}.");
        }

        foreach (string added in new[]
                 {
                     "cancellationActorSide", "cancellationAction",
                     "cancellationReasonCategory", "refundEntitlement", "refundDecisionReason"
                 })
        {
            Assert.True(
                PermitsNullDeep(detail.GetProperty(added), root),
                $"{added} must allow null: {detail.GetProperty(added).GetRawText()}");
        }

        // No invented ledger, event history or derived timeline is published.
        AssertNoPropertyMatching(detail, "ledger", "timeline", "eventHistory", "isCaregiverIncident", "reasonNote");
    }

    private async Task<AuthorizationPolicy> ResolvePolicyAsync(string name)
    {
        IServiceCollection services = new ServiceCollection();
        services.AddSanadApi(CreateConfiguration());

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
                new KeyValuePair<string, string?>("Identity:Jwt:Issuer", "Sanad.Api"),
                new KeyValuePair<string, string?>("Identity:Jwt:Audience", "Sanad.Clients"),
                new KeyValuePair<string, string?>(
                    "Identity:Jwt:SigningKey",
                    "12345678901234567890123456789012")
            ])
            .Build();

    private static MethodInfo[] DeclaredActions(Type controller) =>
        [.. controller.GetMethods().Where(method => method.DeclaringType == controller)];

    private static ParameterInfo Parameter(MethodInfo method, string name) =>
        method.GetParameters().Single(parameter =>
            string.Equals(parameter.Name, name, StringComparison.Ordinal));

    private static string? TemplateOf(MethodInfo method) =>
        method.GetCustomAttributes<HttpMethodAttribute>().Single().Template;

    private static int[] StatusCodesOf(MethodInfo method) =>
        [.. method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .OrderBy(status => status)];

    private static void AssertExactProperties(JsonElement schema, params string[] names) =>
        Assert.Equal<string>(
            [.. names.OrderBy(name => name, StringComparer.Ordinal)],
            [.. schema.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal)]);

    private static void AssertNoPropertyMatching(JsonElement schema, params string[] tokens) =>
        Assert.DoesNotContain(
            schema.EnumerateObject().Select(property => property.Name),
            name => tokens.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase)));

    private static string[] PathMethods(JsonElement root, string path)
    {
        Assert.True(
            root.GetProperty("paths").TryGetProperty(path, out JsonElement pathItem),
            $"Missing path {path}.");
        return
        [
            .. pathItem.EnumerateObject()
                .Select(property => property.Name)
                .Where(name => HttpVerbs.Contains(name, StringComparer.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
        ];
    }

    private static readonly string[] HttpVerbs =
        ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    private static bool PermitsNull(JsonElement property)
    {
        bool nullable = property.TryGetProperty("nullable", out JsonElement nullableFlag)
            && nullableFlag.ValueKind == JsonValueKind.True;
        bool permitsNullUnion = new[] { "anyOf", "oneOf" }.Any(key =>
            property.TryGetProperty(key, out JsonElement union)
            && union.EnumerateArray().Any(option =>
                option.TryGetProperty("type", out JsonElement type) && type.GetString() == "null"));
        bool nullTypeMember = property.TryGetProperty("type", out JsonElement typeProperty)
            && typeProperty.ValueKind == JsonValueKind.Array
            && typeProperty.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.String && v.GetString() == "null");
        return nullable || permitsNullUnion || nullTypeMember;
    }

    /// <summary>
    /// A nullable enum may be published as a union, as an inline nullable type or
    /// as a <c>$ref</c> whose target schema carries the null member, so the check
    /// follows the reference before deciding.
    /// </summary>
    private static bool PermitsNullDeep(JsonElement property, JsonElement root)
    {
        if (PermitsNull(property)) return true;

        if (property.TryGetProperty("$ref", out JsonElement reference)
            && reference.GetString() is { } target)
            return PermitsNullDeep(ResolveRef(root, target), root);

        foreach (string union in new[] { "anyOf", "oneOf" })
        {
            if (property.TryGetProperty(union, out JsonElement options)
                && options.EnumerateArray().Any(option => PermitsNullDeep(option, root)))
                return true;
        }

        return false;
    }

    private static bool TryGetJsonSchema(JsonElement response, out JsonElement schema)
    {
        schema = default;
        return response.TryGetProperty("content", out JsonElement content) &&
            content.TryGetProperty("application/json", out JsonElement media) &&
            media.TryGetProperty("schema", out schema);
    }

    private static JsonElement GetOperation(JsonElement root, string path, string method)
    {
        Assert.True(
            root.GetProperty("paths").TryGetProperty(path, out JsonElement pathItem),
            $"Missing path {path}.");
        Assert.True(
            pathItem.TryGetProperty(method, out JsonElement operation),
            $"Missing {method} on {path}.");
        return operation;
    }

    private static string AssertRef(JsonElement schema, JsonElement root)
    {
        Assert.True(schema.TryGetProperty("$ref", out JsonElement reference), "Expected a $ref schema.");
        string value = reference.GetString()!;
        ResolveRef(root, value);
        return value;
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

    private JsonElement Document()
    {
        if (_document is null)
        {
            using HttpClient client = _host.CreatePortalClient();
            using HttpResponseMessage response = client.GetAsync("/openapi/v1.json").GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            _document = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        }

        return _document.RootElement;
    }

    private sealed class StubSender : ISender
    {
        public object? LastRequest { get; private set; }

        public object? Response { get; init; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult((TResponse)Response!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
