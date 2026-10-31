using System.Reflection;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Ahlena.API.Authorization;
using Ahlena.API.Controllers;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Community.Application.Posts;
using Ahlena.Modules.Community.Domain.Posts;

namespace Ahlena.UnitTests.API;

/// <summary>
/// F4 restriction-read HTTP/metadata contract. <c>GET
/// /api/v1/admin/community/restrictions</c> is a read under the unchanged
/// Community moderation policy (normal SuperAdmin/ContentAdmin); the post
/// detail publishes a nullable current-restriction link while retaining the
/// existing author id. The generated OpenAPI document is the real one served by
/// the isolated <see cref="PortalApiMetadataHost"/> — no handwritten schema — so
/// the published shapes are the runtime shapes.
/// </summary>
public sealed class AdminCommunityRestrictionReadTests : IClassFixture<PortalApiMetadataHost>
{
    private const string RestrictionsPath = "/api/v1/admin/community/restrictions";
    private const string PostDetailPath = "/api/v1/admin/community/posts/{postId}";

    private readonly PortalApiMetadataHost _host;
    private JsonDocument? _document;

    public AdminCommunityRestrictionReadTests(PortalApiMetadataHost host) => _host = host;

    [Fact]
    public void RestrictionReadRoute_UsesCommunityModerationPolicyAndAbsoluteTemplate()
    {
        var authorize = typeof(AdminCommunityPostsController)
            .GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal(AuthorizationPolicies.CommunityModeration, authorize!.Policy);

        Assert.Equal(RestrictionsPath, TemplateOf(nameof(AdminCommunityPostsController.ListRestrictions)));
    }

    [Fact]
    public void RestrictionReadRoute_BindsOptionalAuthorAndActiveFiltersOnly()
    {
        MethodInfo action = typeof(AdminCommunityPostsController)
            .GetMethod(nameof(AdminCommunityPostsController.ListRestrictions))!;

        Assert.Equal(typeof(Guid?), Parameter(action, "authorId").ParameterType);
        Assert.Equal(typeof(bool?), Parameter(action, "isActive").ParameterType);
        Assert.Equal(1, Parameter(action, "page").DefaultValue);
        Assert.Equal(25, Parameter(action, "pageSize").DefaultValue);

        foreach (string name in new[] { "authorId", "isActive", "page", "pageSize" })
        {
            Assert.True(
                Parameter(action, name).GetCustomAttributes<FromQueryAttribute>().Any(),
                $"{name} must stay a query parameter.");
        }
    }

    [Fact]
    public void RestrictionReadRoute_PublishesPageAndValidationResponses()
    {
        HttpMethodAttribute route = typeof(AdminCommunityPostsController)
            .GetMethod(nameof(AdminCommunityPostsController.ListRestrictions))!
            .GetCustomAttributes<HttpMethodAttribute>()
            .Single();

        Assert.Equal(RestrictionsPath, route.Template);

        var statuses = typeof(AdminCommunityPostsController)
            .GetMethod(nameof(AdminCommunityPostsController.ListRestrictions))!
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .ToDictionary(attribute => attribute.StatusCode, attribute => attribute.Type);

        Assert.Equal(typeof(AdminCommunityRestrictionsPage), statuses[StatusCodes.Status200OK]);
        Assert.Equal(typeof(ProblemDetails), statuses[StatusCodes.Status400BadRequest]);
    }

    [Fact]
    public async Task ListRestrictions_ForwardsFiltersAndReturnsPage()
    {
        var expected = new AdminCommunityRestrictionsPage(
            [
                new AdminCommunityRestrictionListItem(
                    Guid.CreateVersion7(), Guid.CreateVersion7(), "مؤلف", "Author",
                    Guid.CreateVersion7(), nameof(CommunityRestrictionDuration.Days7),
                    CommunityRestrictionReasonCodes.Spam, null,
                    new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc),
                    true, null, null, null)
            ],
            2, 10, 1);
        var sender = new StubSender { Response = Result<AdminCommunityRestrictionsPage>.Success(expected) };
        Guid authorId = Guid.CreateVersion7();

        IActionResult result = await new AdminCommunityPostsController(sender).ListRestrictions(
            authorId: authorId,
            isActive: true,
            page: 2,
            pageSize: 10,
            cancellationToken: default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value);
        var query = Assert.IsType<GetAdminCommunityRestrictionsQuery>(sender.LastRequest);
        Assert.Equal(authorId, query.AuthorId);
        Assert.True(query.IsActive);
        Assert.Equal(2, query.Page);
        Assert.Equal(10, query.PageSize);
    }

    [Fact]
    public async Task ListRestrictions_FailureResult_ReturnsMappedProblem()
    {
        var sender = new StubSender
        {
            Response = Result<AdminCommunityRestrictionsPage>.Failure(
                new Error("Community.UnreachableInProduct", "Synthetic failure for the mapping contract."))
        };

        var controller = new AdminCommunityPostsController(sender)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        IActionResult result = await controller.ListRestrictions(cancellationToken: default);

        var objectResult = Assert.IsType<ObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("Community.UnreachableInProduct", problem.Extensions["code"]);
    }

    [Fact]
    public void OpenApi_RestrictionReadOperation_PublishesActualPageShape()
    {
        JsonElement root = Document();
        JsonElement get = GetOperation(root, RestrictionsPath, "get");

        Assert.True(
            get.GetProperty("responses").TryGetProperty("200", out JsonElement ok),
            $"Missing 200 response for GET {RestrictionsPath}.");
        Assert.True(TryGetJsonSchema(ok, out JsonElement schema), "Missing 200 JSON schema.");
        Assert.EndsWith("AdminCommunityRestrictionsPage", AssertRef(schema, root));

        JsonElement page = ResolveRef(root, AssertRef(schema, root)).GetProperty("properties");
        foreach (string name in new[] { "items", "page", "pageSize", "totalCount" })
            Assert.True(page.TryGetProperty(name, out _), $"Missing AdminCommunityRestrictionsPage.{name}.");

        JsonElement items = page.GetProperty("items");
        Assert.Equal("array", items.GetProperty("type").GetString());
        Assert.EndsWith(
            "AdminCommunityRestrictionListItem", AssertRef(items.GetProperty("items"), root));

        Dictionary<string, JsonElement> parameters = get.GetProperty("parameters")
            .EnumerateArray()
            .ToDictionary(parameter => parameter.GetProperty("name").GetString()!);

        Assert.Equal("uuid", parameters["authorId"].GetProperty("schema").GetProperty("format").GetString());
        Assert.Equal("boolean", parameters["isActive"].GetProperty("schema").GetProperty("type").GetString());
        AssertIntegerQuerySchema(parameters["page"].GetProperty("schema"), 1);
        AssertIntegerQuerySchema(parameters["pageSize"].GetProperty("schema"), 25);

        foreach (string name in new[] { "authorId", "isActive", "page", "pageSize" })
        {
            Assert.False(
                parameters[name].TryGetProperty("required", out JsonElement required) && required.GetBoolean(),
                $"{name} stays optional on the restriction read.");
        }
    }

    [Fact]
    public void OpenApi_RestrictionListItemSchema_PublishesRetainedFactsAndAuthorNames()
    {
        JsonElement root = Document();
        JsonElement properties = ResolveRef(root, "#/components/schemas/AdminCommunityRestrictionListItem")
            .GetProperty("properties");

        foreach (string name in new[]
                 {
                     "restrictionId", "authorId", "authorArabicName", "authorEnglishName",
                     "moderatorId", "duration", "reasonCode", "note", "startsOnUtc",
                     "expiresOnUtc", "isActive", "revokedBy", "revokedOnUtc", "revocationReason"
                 })
        {
            Assert.True(properties.TryGetProperty(name, out _), $"Missing AdminCommunityRestrictionListItem.{name}.");
        }

        Assert.Equal("string", properties.GetProperty("duration").GetProperty("type").GetString());
        Assert.Equal("string", properties.GetProperty("reasonCode").GetProperty("type").GetString());
        Assert.Equal("boolean", properties.GetProperty("isActive").GetProperty("type").GetString());
        Assert.Equal("uuid", properties.GetProperty("restrictionId").GetProperty("format").GetString());

        foreach (string nullableField in new[] { "note", "revokedBy", "revokedOnUtc", "revocationReason" })
        {
            Assert.True(
                PermitsNull(properties.GetProperty(nullableField)),
                $"{nullableField} must allow null: {properties.GetProperty(nullableField).GetRawText()}");
        }

        foreach (string requiredField in new[] { "restrictionId", "authorId", "authorArabicName", "authorEnglishName", "moderatorId", "duration", "reasonCode", "startsOnUtc", "expiresOnUtc", "isActive" })
        {
            Assert.False(
                PermitsNull(properties.GetProperty(requiredField)),
                $"{requiredField} must not allow null: {properties.GetProperty(requiredField).GetRawText()}");
        }

        string[] forbidden = ["email", "phone", "contact"];
        Assert.DoesNotContain(
            properties.EnumerateObject().Select(property => property.Name),
            name => forbidden.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void OpenApi_PostDetail_PublishesNullableCurrentActiveRestrictionAndKeepsAuthorId()
    {
        JsonElement root = Document();
        JsonElement detail = ResolveRef(root, "#/components/schemas/AdminCommunityPostDetail")
            .GetProperty("properties");

        Assert.Equal("uuid", detail.GetProperty("authorId").GetProperty("format").GetString());

        JsonElement active = detail.GetProperty("activeRestriction");
        Assert.True(PermitsNull(active), $"activeRestriction must allow null: {active.GetRawText()}");
        Assert.EndsWith("AdminCommunityActiveRestriction", AssertRefInsideUnion(active, root));

        JsonElement properties = ResolveRef(root, "#/components/schemas/AdminCommunityActiveRestriction")
            .GetProperty("properties");

        Assert.Equal(
            new[] { "expiresOnUtc", "restrictionId" },
            properties.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.False(PermitsNull(properties.GetProperty("restrictionId")));
        Assert.False(PermitsNull(properties.GetProperty("expiresOnUtc")));
    }

    [Fact]
    public void OpenApi_ExistingRestrictionWriteRoutesAndPostDetailRemainPublished()
    {
        JsonElement root = Document();
        JsonElement create = GetOperation(root, "/api/v1/admin/community/authors/{authorId}/restrictions", "post");
        JsonElement revoke = GetOperation(root, "/api/v1/admin/community/restrictions/{restrictionId}/revoke", "post");

        foreach (JsonElement operation in new[] { create, revoke })
        {
            Assert.True(TryGetJsonSchema(operation.GetProperty("responses").GetProperty("200"), out JsonElement schema));
            Assert.EndsWith("CommunityAuthorRestrictionResponse", AssertRef(schema, root));
            Assert.True(operation.GetProperty("responses").TryGetProperty("400", out _));
            Assert.True(operation.GetProperty("responses").TryGetProperty("409", out _));
        }

        // The published problem-response sets are the unchanged existing ones:
        // create has no not-found case (an unknown author id cannot be told
        // apart from a valid author without an active restriction), revoke does.
        Assert.Equal(new[] { "200", "400", "409" }, ResponseCodes(create));
        Assert.Equal(new[] { "200", "400", "404", "409" }, ResponseCodes(revoke));

        Assert.True(
            GetOperation(root, PostDetailPath, "get")
                .GetProperty("responses")
                .TryGetProperty("404", out _),
            "The post-detail not-found response stays published.");
    }

    [Fact]
    public async Task Detail_UnknownPost_ReturnsNotFound()
    {
        var sender = new StubSender
        {
            Response = Result<AdminCommunityPostDetail>.Failure(
                new Error("Community.PostNotFound", "Community post not found."))
        };

        var controller = new AdminCommunityPostsController(sender)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        IActionResult result = await controller.Detail(Guid.CreateVersion7(), default);

        var objectResult = Assert.IsType<ObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
    }

    private static ParameterInfo Parameter(MethodInfo method, string name) =>
        method.GetParameters().Single(parameter =>
            string.Equals(parameter.Name, name, StringComparison.Ordinal));

    private static string? TemplateOf(string methodName) =>
        typeof(AdminCommunityPostsController)
            .GetMethod(methodName)!
            .GetCustomAttributes<HttpMethodAttribute>()
            .Single()
            .Template;

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
    /// The published integer query schema. ASP.NET Core publishes the accepted
    /// textual form alongside the integer type (<c>type: ["integer","string"]</c>)
    /// together with the pattern and the default, so the assertion accepts
    /// either published shape while still requiring the integer contract.
    /// </summary>
    private static void AssertIntegerQuerySchema(JsonElement schema, int expectedDefault)
    {
        JsonElement type = schema.GetProperty("type");
        bool integer = type.ValueKind == JsonValueKind.String
            ? string.Equals(type.GetString(), "integer", StringComparison.Ordinal)
            : type.EnumerateArray().Any(value => string.Equals(value.GetString(), "integer", StringComparison.Ordinal));

        Assert.True(integer, $"Expected an integer query schema: {schema.GetRawText()}");
        Assert.Equal("int32", schema.GetProperty("format").GetString());
        Assert.Equal(expectedDefault, schema.GetProperty("default").GetInt32());
    }

    private static string[] ResponseCodes(JsonElement operation) =>
        operation.GetProperty("responses")
            .EnumerateObject()
            .Select(response => response.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

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

    /// <summary>
    /// Resolves the schema reference of a property that the generator publishes
    /// as a union with <c>null</c> (<c>oneOf: [{type: null}, {$ref: ...}]</c>)
    /// instead of a plain <c>$ref</c>.
    /// </summary>
    private static string AssertRefInsideUnion(JsonElement schema, JsonElement root)
    {
        if (schema.TryGetProperty("$ref", out JsonElement direct))
        {
            string directValue = direct.GetString()!;
            ResolveRef(root, directValue);
            return directValue;
        }

        foreach (string union in new[] { "anyOf", "oneOf" })
        {
            if (!schema.TryGetProperty(union, out JsonElement options)) continue;

            foreach (JsonElement option in options.EnumerateArray())
            {
                if (!option.TryGetProperty("$ref", out JsonElement reference)) continue;

                string value = reference.GetString()!;
                ResolveRef(root, value);
                return value;
            }
        }

        throw new InvalidOperationException($"Expected a $ref schema in the union: {schema.GetRawText()}");
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
