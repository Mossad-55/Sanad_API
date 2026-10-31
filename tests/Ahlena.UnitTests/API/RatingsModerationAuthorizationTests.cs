using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ahlena.API;
using Ahlena.API.Authorization;
using Ahlena.API.Controllers;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.CareHomes.Application.Discovery;
using Ahlena.Modules.Caregivers.Application.Discovery;
using Ahlena.Modules.Identity.Application.Authentication.Tokens;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.UnitTests.API;

/// <summary>
/// F7 ratings-visibility HTTP/metadata contract. Both moderation controllers are
/// pinned to existing admin account policies and the owner surface to the owner
/// policy, every published path/verb/status is the pinned one, the published
/// schemas carry exactly the documented fields with no reviewer identity, and
/// the shared mapper pins all twelve new error codes. The generated OpenAPI
/// document is the real one served by the isolated
/// <see cref="PortalApiMetadataHost"/>, so the published shapes are the runtime
/// shapes.
/// </summary>
public sealed class RatingsModerationAuthorizationTests : IClassFixture<PortalApiMetadataHost>
{
    private const string CaregiverRatingsRoute = "api/v1/admin/caregivers/ratings";
    private const string CareHomeRatingsRoute = "api/v1/admin/care-homes/ratings";
    private const string OwnerRatingsRoute = "api/v1/care-homes/facilities/mine/ratings";

    private const string CaregiverRatingsPath = "/api/v1/admin/caregivers/ratings";
    private const string CareHomeRatingsPath = "/api/v1/admin/care-homes/ratings";
    private const string OwnerRatingsPath = "/api/v1/care-homes/facilities/mine/ratings";

    private static readonly string[] ForbiddenAdminTokens =
    [
        "reviewer", "familyname", "email", "phone", "avatar",
        "username", "displayname", "owneruserid"
    ];

    private static readonly string[] ForbiddenOwnerTokens =
    [
        "reviewer", "familyname", "family", "email", "phone", "avatar",
        "username", "displayname", "userid", "owneruserid", "createdby"
    ];

    private readonly PortalApiMetadataHost _host;
    private JsonDocument? _document;

    public RatingsModerationAuthorizationTests(PortalApiMetadataHost host) => _host = host;

    [Fact]
    public void AdminModerationControllers_UseTheExistingAdminPoliciesAndRoutes()
    {
        var caregiver = typeof(AdminCaregiverRatingsController).GetCustomAttribute<AuthorizeAttribute>();
        var careHome = typeof(AdminCareHomeRatingsController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(caregiver);
        Assert.NotNull(careHome);
        Assert.Equal(AuthorizationPolicies.CaregiverReviewAdmin, caregiver!.Policy);
        Assert.Equal(AuthorizationPolicies.CareHomesOperationalAdmin, careHome!.Policy);

        Assert.Equal(
            CaregiverRatingsRoute,
            typeof(AdminCaregiverRatingsController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(
            CareHomeRatingsRoute,
            typeof(AdminCareHomeRatingsController).GetCustomAttribute<RouteAttribute>()!.Template);

        foreach (Type controller in new[]
                 { typeof(AdminCaregiverRatingsController), typeof(AdminCareHomeRatingsController) })
        {
            Assert.Empty(controller.GetCustomAttributes<AllowAnonymousAttribute>());
        }
    }

    [Fact]
    public void OwnerRatingsController_UsesOnlyTheOwnerPolicyAndRoute()
    {
        var authorize = typeof(OwnerCareHomeRatingsController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(AuthorizationPolicies.CareHomeOwnerAccess, authorize!.Policy);
        Assert.NotEqual(AuthorizationPolicies.CaregiverReviewAdmin, authorize.Policy);
        Assert.NotEqual(AuthorizationPolicies.CareHomesOperationalAdmin, authorize.Policy);
        Assert.Equal(
            OwnerRatingsRoute,
            typeof(OwnerCareHomeRatingsController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Empty(typeof(OwnerCareHomeRatingsController).GetCustomAttributes<AllowAnonymousAttribute>());
    }

    [Fact]
    public void ModerationActions_ArePostOnlyAndReadsAreGetOnly()
    {
        Assert.Equal(["GET", "POST", "POST"], Verbs(typeof(AdminCaregiverRatingsController)));
        Assert.Equal(["GET", "POST", "POST", "POST"], Verbs(typeof(AdminCareHomeRatingsController)));
        Assert.Equal(["GET", "POST"], Verbs(typeof(OwnerCareHomeRatingsController)));
    }

    [Fact]
    public void ModerationActions_PublishThePinnedStatusCodes()
    {
        Assert.Equal<int>(
            [204, 400, 404, 409],
            StatusCodesOf(typeof(AdminCaregiverRatingsController), nameof(AdminCaregiverRatingsController.Hide)));
        Assert.Equal<int>(
            [204, 400, 404, 409],
            StatusCodesOf(typeof(AdminCaregiverRatingsController), nameof(AdminCaregiverRatingsController.Restore)));
        Assert.Equal<int>(
            [204, 400, 404, 409],
            StatusCodesOf(typeof(AdminCareHomeRatingsController), nameof(AdminCareHomeRatingsController.Hide)));
        Assert.Equal<int>(
            [204, 400, 404, 409],
            StatusCodesOf(typeof(AdminCareHomeRatingsController), nameof(AdminCareHomeRatingsController.Keep)));
        Assert.Equal<int>(
            [204, 400, 404, 409],
            StatusCodesOf(typeof(AdminCareHomeRatingsController), nameof(AdminCareHomeRatingsController.Restore)));
        Assert.Equal<int>(
            [204, 400, 404],
            StatusCodesOf(typeof(OwnerCareHomeRatingsController), nameof(OwnerCareHomeRatingsController.Flag)));
    }

    [Fact]
    public async Task CaregiverReviewAdmin_AllowsSuperAdminAndSupportAdminOnly()
    {
        AuthorizationPolicy policy = await ResolvePolicyAsync(AuthorizationPolicies.CaregiverReviewAdmin);

        Assert.Contains(policy.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);
        Assert.Equal<string>([AuthAccessType.Normal.ToString()], AccessValues(policy));
        Assert.Equal<string>(
            [AccountType.SuperAdmin.ToString(), AccountType.SupportAdmin.ToString()],
            AccountValues(policy));
        AssertAppAudiencesExcluded(policy);
    }

    [Fact]
    public async Task CareHomesOperationalAdmin_AllowsSuperAdminAndSupportAdminOnly()
    {
        AuthorizationPolicy policy = await ResolvePolicyAsync(AuthorizationPolicies.CareHomesOperationalAdmin);

        Assert.Contains(policy.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);
        Assert.Equal<string>([AuthAccessType.Normal.ToString()], AccessValues(policy));
        Assert.Equal<string>(
            [AccountType.SuperAdmin.ToString(), AccountType.SupportAdmin.ToString()],
            AccountValues(policy));
        AssertAppAudiencesExcluded(policy);
    }

    [Fact]
    public async Task CareHomeOwnerAccess_AllowsTheOwnerOnlyAndNeverAnAdmin()
    {
        AuthorizationPolicy policy = await ResolvePolicyAsync(AuthorizationPolicies.CareHomeOwnerAccess);

        Assert.Contains(policy.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);
        Assert.Equal<string>([AuthAccessType.Normal.ToString()], AccessValues(policy));
        Assert.Equal<string>([AccountType.CareHomeOwner.ToString()], AccountValues(policy));

        foreach (AccountType excluded in new[]
                 {
                     AccountType.SuperAdmin, AccountType.SupportAdmin, AccountType.ContentAdmin,
                     AccountType.FinanceAdmin, AccountType.Family, AccountType.Elderly,
                     AccountType.MedicalCaregiver, AccountType.CompanionCaregiver
                 })
        {
            Assert.DoesNotContain(excluded.ToString(), AccountValues(policy));
        }
    }

    [Fact]
    public void OpenApi_PublishesThePinnedRatingPathsAndVerbsOnly()
    {
        JsonElement root = Document();

        Assert.Equal<string>(["get"], PathMethods(root, CaregiverRatingsPath));
        Assert.Equal<string>(["post"], PathMethods(root, $"{CaregiverRatingsPath}/{{ratingId}}/hide"));
        Assert.Equal<string>(["post"], PathMethods(root, $"{CaregiverRatingsPath}/{{ratingId}}/restore"));

        Assert.Equal<string>(["get"], PathMethods(root, CareHomeRatingsPath));
        Assert.Equal<string>(["post"], PathMethods(root, $"{CareHomeRatingsPath}/{{ratingId}}/hide"));
        Assert.Equal<string>(["post"], PathMethods(root, $"{CareHomeRatingsPath}/{{ratingId}}/keep"));
        Assert.Equal<string>(["post"], PathMethods(root, $"{CareHomeRatingsPath}/{{ratingId}}/restore"));

        Assert.Equal<string>(["get"], PathMethods(root, OwnerRatingsPath));
        Assert.Equal<string>(["post"], PathMethods(root, $"{OwnerRatingsPath}/{{ratingId}}/flag"));

        // The owner surface never grows a moderation verb, and the admin
        // surface never grows an owner flag.
        Assert.DoesNotContain("post", PathMethods(root, OwnerRatingsPath));
        Assert.DoesNotContain("post", PathMethods(root, CareHomeRatingsPath));
        Assert.DoesNotContain("post", PathMethods(root, CaregiverRatingsPath));
    }

    [Fact]
    public void OpenApi_AdminListsPublishTheExactPinnedFieldsWithNoReviewerIdentity()
    {
        JsonElement root = Document();

        AssertPageSchema(
            root, CaregiverRatingsPath, "AdminCaregiverRatingPage", "AdminCaregiverRatingListItem",
            "items", "page", "pageSize", "totalCount");
        JsonElement caregiverItem = ResolveRef(root, "#/components/schemas/AdminCaregiverRatingListItem")
            .GetProperty("properties");
        AssertExactProperties(
            caregiverItem,
            "ratingId", "caregiverId", "bookingId", "familyId", "stars", "reviewText",
            "createdOnUtc", "updatedOnUtc", "moderationState", "hiddenByUserId", "hiddenOnUtc", "hideReason");
        AssertNoPropertyMatching(caregiverItem, ForbiddenAdminTokens);

        AssertPageSchema(
            root, CareHomeRatingsPath, "AdminCareHomeRatingPage", "AdminCareHomeRatingListItem",
            "items", "page", "pageSize", "totalCount");
        JsonElement careHomeItem = ResolveRef(root, "#/components/schemas/AdminCareHomeRatingListItem")
            .GetProperty("properties");
        AssertExactProperties(
            careHomeItem,
            "ratingId", "facilityId", "bookingId", "familyId", "stars", "reviewText",
            "createdOnUtc", "updatedOnUtc", "moderationState", "hiddenByUserId", "hiddenOnUtc",
            "hideReason", "unresolvedFlagId", "unresolvedFlagReason", "unresolvedFlaggedOnUtc");
        AssertNoPropertyMatching(careHomeItem, ForbiddenAdminTokens);

        AssertPageSchema(
            root, OwnerRatingsPath, "OwnerCareHomeRatingPage", "OwnerCareHomeRatingItem",
            "items", "page", "pageSize", "totalCount", "averageStars", "ratingCount");
        JsonElement ownerItem = ResolveRef(root, "#/components/schemas/OwnerCareHomeRatingItem")
            .GetProperty("properties");
        AssertExactProperties(
            ownerItem,
            "ratingId", "stars", "reviewText", "createdOnUtc", "flagStatus", "myFlagReason", "myFlaggedOnUtc");
        AssertNoPropertyMatching(ownerItem, ForbiddenOwnerTokens);
    }

    [Fact]
    public void OpenApi_AdminListsPublishTheFourDocumentedFilters()
    {
        JsonElement root = Document();

        Dictionary<string, JsonElement> caregiver = GetOperation(root, CaregiverRatingsPath, "get")
            .GetProperty("parameters")
            .EnumerateArray()
            .ToDictionary(parameter => parameter.GetProperty("name").GetString()!);
        Assert.Equal(
            ["caregiverId", "moderation", "page", "pageSize", "stars"],
            [.. caregiver.Keys.OrderBy(name => name, StringComparer.Ordinal)]);

        Dictionary<string, JsonElement> careHome = GetOperation(root, CareHomeRatingsPath, "get")
            .GetProperty("parameters")
            .EnumerateArray()
            .ToDictionary(parameter => parameter.GetProperty("name").GetString()!);
        Assert.Equal(
            ["facilityId", "flagged", "moderation", "page", "pageSize", "stars"],
            [.. careHome.Keys.OrderBy(name => name, StringComparer.Ordinal)]);

        Dictionary<string, JsonElement> owner = GetOperation(root, OwnerRatingsPath, "get")
            .GetProperty("parameters")
            .EnumerateArray()
            .ToDictionary(parameter => parameter.GetProperty("name").GetString()!);
        Assert.Equal(
            ["page", "pageSize"],
            [.. owner.Keys.OrderBy(name => name, StringComparer.Ordinal)]);

        // The owner read names no reviewer dimension at all.
        foreach (string name in new[] { "familyId", "userId", "reviewer", "search" })
        {
            Assert.False(owner.ContainsKey(name), $"The owner rating read must not accept {name}.");
        }
    }

    [Fact]
    public void OpenApi_OwnerPagePublishesBothAggregatesAndTheOwnerFlagStatus()
    {
        JsonElement root = Document();
        JsonElement page = ResolveRef(root, "#/components/schemas/OwnerCareHomeRatingPage").GetProperty("properties");

        AssertExactProperties(page, "items", "page", "pageSize", "totalCount", "averageStars", "ratingCount");
        Assert.False(PermitsNull(page.GetProperty("averageStars")));
        Assert.False(PermitsNull(page.GetProperty("ratingCount")));

        JsonElement item = ResolveRef(root, "#/components/schemas/OwnerCareHomeRatingItem").GetProperty("properties");
        Assert.False(PermitsNull(item.GetProperty("flagStatus")));
        Assert.True(PermitsNullDeep(item.GetProperty("myFlagReason"), root));
        Assert.True(PermitsNullDeep(item.GetProperty("myFlaggedOnUtc"), root));
    }

    [Fact]
    public void Mapper_PinsEveryNewModerationErrorCode()
    {
        Assert.Equal(404, MapperStatus(CaregiverRatingModerationErrors.NotFound));
        Assert.Equal(400, MapperStatus(CaregiverRatingModerationErrors.InvalidReason));
        Assert.Equal(400, MapperStatus(CaregiverRatingModerationErrors.InvalidQuery));
        Assert.Equal(409, MapperStatus(CaregiverRatingModerationErrors.AlreadyHidden));
        Assert.Equal(409, MapperStatus(CaregiverRatingModerationErrors.AlreadyVisible));

        Assert.Equal(404, MapperStatus(CareHomeRatingModerationErrors.NotFound));
        Assert.Equal(400, MapperStatus(CareHomeRatingModerationErrors.InvalidReason));
        Assert.Equal(400, MapperStatus(CareHomeRatingModerationErrors.InvalidQuery));
        Assert.Equal(409, MapperStatus(CareHomeRatingModerationErrors.AlreadyHidden));
        Assert.Equal(409, MapperStatus(CareHomeRatingModerationErrors.AlreadyVisible));
        Assert.Equal(409, MapperStatus(CareHomeRatingModerationErrors.NoOpenFlag));

        // No code defaults silently to 400 without being pinned.
        Assert.Equal(
            "CareHomes.Rating.NoOpenFlag",
            CareHomeRatingModerationErrors.NoOpenFlag.Code);
        Assert.Equal("Caregivers.Rating.InvalidQuery", CaregiverRatingModerationErrors.InvalidQuery.Code);
    }

    private static void AssertAppAudiencesExcluded(AuthorizationPolicy policy)
    {
        foreach (AccountType excluded in new[]
                 {
                     AccountType.Family, AccountType.Elderly, AccountType.MedicalCaregiver,
                     AccountType.CompanionCaregiver, AccountType.CareHomeOwner,
                     AccountType.FinanceAdmin, AccountType.ContentAdmin
                 })
        {
            Assert.DoesNotContain(excluded.ToString(), AccountValues(policy));
        }
    }

    private static string[] AccessValues(AuthorizationPolicy policy) =>
        Assert.Single(
            policy.Requirements.OfType<ClaimsAuthorizationRequirement>(),
            requirement => requirement.ClaimType == AuthClaimNames.AccessType).AllowedValues!.ToArray();

    private static string[] AccountValues(AuthorizationPolicy policy) =>
        Assert.Single(
            policy.Requirements.OfType<ClaimsAuthorizationRequirement>(),
            requirement => requirement.ClaimType == AuthClaimNames.AccountType).AllowedValues!.ToArray();

    private static string[] Verbs(Type controller, params string[] actionNames) =>
        [.. (actionNames.Length == 0
                ? controller.GetMethods()
                    .Where(method => method.DeclaringType == controller)
                    .Select(method => method.Name)
                : actionNames)
            .SelectMany(name => controller.GetMethod(name)!
                .GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>())
            .Select(attribute => attribute.HttpMethods.Single())
            .OrderBy(verb => verb, StringComparer.Ordinal)];

    private static int[] StatusCodesOf(Type controller, string action) =>
        [.. controller.GetMethod(action)!
            .GetCustomAttributes<Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .OrderBy(status => status)];

    private static int MapperStatus(Error error) =>
        Ahlena.API.ProblemDetail.ResultProblemDetailsMapper.Create(
            error, new Microsoft.AspNetCore.Http.DefaultHttpContext()).Status ?? -1;

    private async Task<AuthorizationPolicy> ResolvePolicyAsync(string name)
    {
        IServiceCollection services = new ServiceCollection();
        services.AddAhlenaApi(CreateConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });

        IAuthorizationPolicyProvider policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

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
                    "Identity:Jwt:SigningKey",
                    "12345678901234567890123456789012")
            ])
            .Build();

    private static void AssertPageSchema(
        JsonElement root, string path, string pageSchema, string itemSchema, params string[] pageProperties)
    {
        JsonElement get = GetOperation(root, path, "get");
        Assert.True(
            get.GetProperty("responses").TryGetProperty("400", out _),
            $"The invalid-query rejection stays published for GET {path}.");
        Assert.True(get.GetProperty("responses").TryGetProperty("200", out JsonElement ok));

        Assert.True(TryGetJsonSchema(ok, out JsonElement schema), $"Missing 200 JSON schema for {path}.");
        Assert.EndsWith(pageSchema, AssertRef(schema, root));

        JsonElement page = ResolveRef(root, AssertRef(schema, root)).GetProperty("properties");
        AssertExactProperties(page, pageProperties);

        JsonElement items = page.GetProperty("items");
        Assert.Equal("array", items.GetProperty("type").GetString());
        Assert.EndsWith(itemSchema, AssertRef(items.GetProperty("items"), root));
        Assert.True(
            root.GetProperty("components").GetProperty("schemas").TryGetProperty(itemSchema, out _),
            $"Missing schema {itemSchema}.");
    }

    private static void AssertExactProperties(JsonElement schema, params string[] names) =>
        Assert.Equal<string>(
            [.. names.OrderBy(name => name, StringComparer.Ordinal)],
            [.. schema.EnumerateObject().Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)]);

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
            && typeProperty.EnumerateArray().Any(value =>
                value.ValueKind == JsonValueKind.String && value.GetString() == "null");
        return nullable || permitsNullUnion || nullTypeMember;
    }

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
            _document = JsonDocument.Parse(
                response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        }

        return _document.RootElement;
    }
}
