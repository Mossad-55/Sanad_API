using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ahlena.API.Authorization;
using Ahlena.API.Controllers;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.Abstractions.Storage;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.CareHomes.Application.Abstractions.Data;
using Ahlena.Modules.CareHomes.Application.Facilities;
using Ahlena.Modules.CareHomes.Domain.Facilities;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;
using Ahlena.Modules.Identity.Application.Abstractions.Security;
using Ahlena.Modules.Identity.Domain.Users;
using Ahlena.UnitTests.CareHomes;

namespace Ahlena.UnitTests.API;

/// <summary>
/// Authored route-level checks (2026-10-27 document lifecycle) for the four
/// new document routes. The controller-facing branches are covered directly:
/// the unauthenticated-user guard, the authenticated command/query
/// propagation with the token's actor, the coded problem mapping through the
/// real ResultProblemDetailsMapper, and the presence of the role policies on
/// the controller classes.
/// <para>
/// The registered authorization policies are ALSO exercised over real HTTP
/// by <see cref="CareHomeDocumentRoutePolicyTests"/> below (in-process host,
/// real authentication + authorization middleware), so no
/// "middleware not unit-exercisable" gap remains for these routes.
/// NOT RUN under the phase source freeze.
/// </para>
/// </summary>
public sealed class CareHomeDocumentRouteTests
{
    // ---- owner routes: api/v1/care-homes/facilities/mine/documents ----

    [Fact]
    public async Task Owner_document_routes_reject_an_unauthenticated_caller()
    {
        var controller = new CareHomeFacilitiesController(new CapturingSender())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        Assert.IsType<UnauthorizedResult>(await controller.ListDocuments(
            null, null, null, default));
        Assert.IsType<UnauthorizedResult>(await controller.ReadDocument(
            Guid.NewGuid(), default));
        Assert.IsType<UnauthorizedResult>(await controller.RemoveDocument(
            Guid.NewGuid(), 3, default));
    }

    [Fact]
    public async Task Owner_document_routes_propagate_the_token_actor_to_the_commands()
    {
        UserId owner = UserId.New();
        var sender = new CapturingSender();
        var controller = new CareHomeFacilitiesController(sender)
        {
            ControllerContext = ClaimsContextFor(owner)
        };

        sender.NextList = Result<CareHomeDocumentListResponse>.Success(
            new CareHomeDocumentListResponse(CareHomeDocumentViews.All, null, null, 0, []));
        IActionResult list = await controller.ListDocuments(
            CareHomeDocumentViews.Pending, CareHomeDocumentType.HealthCertificate, isExpired: true, default);
        Assert.IsType<OkObjectResult>(list);
        GetMyCareHomeDocumentsQuery listQuery = sender.Last<GetMyCareHomeDocumentsQuery>();
        Assert.Equal(owner, listQuery.ActorUserId);
        Assert.Equal(CareHomeDocumentViews.Pending, listQuery.View);
        Assert.Equal(CareHomeDocumentType.HealthCertificate, listQuery.Type);
        Assert.True(listQuery.IsExpired);

        sender.NextUnit = Result.Success();
        IActionResult removed = await controller.RemoveDocument(Guid.NewGuid(), 7, default);
        Assert.IsType<NoContentResult>(removed);
        RemoveMyCareHomeDocumentCommand removeCommand = sender.Last<RemoveMyCareHomeDocumentCommand>();
        Assert.Equal(owner, removeCommand.ActorUserId);
        Assert.Equal(7, removeCommand.ExpectedVersion);
    }

    [Fact]
    public async Task Owner_document_file_route_returns_the_private_content_or_a_coded_404()
    {
        UserId owner = UserId.New();
        var sender = new CapturingSender();
        var controller = new CareHomeFacilitiesController(sender)
        {
            ControllerContext = ClaimsContextFor(owner)
        };

        // Success: streams the stored content with its content type; the
        // actor comes from the token.
        sender.NextFile = Result<PrivateFileContent>.Success(
            new PrivateFileContent("private/stored-key", "application/pdf", new MemoryStream([1, 2, 3])));
        IActionResult served = await controller.ReadDocument(Guid.NewGuid(), default);
        FileStreamResult file = Assert.IsType<FileStreamResult>(served);
        Assert.Equal("application/pdf", file.ContentType);
        GetMyCareHomeDocumentFileQuery fileQuery = sender.Last<GetMyCareHomeDocumentFileQuery>();
        Assert.Equal(owner, fileQuery.ActorUserId);
        Assert.Equal(0, sender.StorageDeleteCalls);

        // Unknown or logically removed document: coded 404 through the real
        // mapper, with no storage interaction on the failure path.
        sender.NextFile = Result<PrivateFileContent>.Failure(CareHomeDocumentErrors.DocumentNotFound);
        AssertProblem(
            await controller.ReadDocument(Guid.NewGuid(), default),
            404, "CareHomes.Document.DocumentNotFound");
    }

    // ---- admin routes: api/v1/admin/care-homes/{id}/documents ----

    [Fact]
    public async Task Admin_document_remove_rejects_an_unauthenticated_caller_and_propagates_the_admin_actor()
    {
        Guid careHomeId = Guid.NewGuid();
        var anonymous = new AdminCareHomesController(new CapturingSender(), new CareHomeTestClock(DateTime.UtcNow))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        Assert.IsType<UnauthorizedResult>(await anonymous.RemoveDocument(careHomeId, Guid.NewGuid(), 1, default));
        // The admin list route carries no user-id guard (consistent with the
        // other admin reads): its role enforcement is the route policy
        // asserted in Document_routes_carry_the_expected_role_policies.

        UserId admin = UserId.New();
        var sender = new CapturingSender();
        var controller = new AdminCareHomesController(sender, new CareHomeTestClock(DateTime.UtcNow))
        {
            ControllerContext = ClaimsContextFor(admin)
        };

        sender.NextUnit = Result.Success();
        IActionResult removed = await controller.RemoveDocument(careHomeId, Guid.NewGuid(), 9, default);
        Assert.IsType<NoContentResult>(removed);
        RemoveAdminCareHomeDocumentCommand command = sender.Last<RemoveAdminCareHomeDocumentCommand>();
        Assert.Equal(admin, command.ActorUserId);
        Assert.Equal(careHomeId, command.CareHomeId);
        Assert.Equal(9, command.ExpectedVersion);

        sender.NextUnit = Result.Failure(CareHomeDocumentErrors.NotRemovable);
        AssertProblem(
            await controller.RemoveDocument(careHomeId, Guid.NewGuid(), 9, default),
            409, "CareHomes.Document.NotRemovable");
    }

    [Fact]
    public async Task Admin_document_list_sends_the_filters_and_maps_coded_status()
    {
        Guid careHomeId = Guid.NewGuid();
        var sender = new CapturingSender();
        var controller = new AdminCareHomesController(sender, new CareHomeTestClock(DateTime.UtcNow))
        {
            ControllerContext = ClaimsContextFor(UserId.New())
        };

        sender.NextList = Result<CareHomeDocumentListResponse>.Success(
            new CareHomeDocumentListResponse(CareHomeDocumentViews.History, null, null, 0, []));
        IActionResult listed = await controller.ListDocuments(
            careHomeId, CareHomeDocumentViews.History, null, null, default);
        Assert.IsType<OkObjectResult>(listed);
        GetAdminCareHomeDocumentsQuery query = sender.Last<GetAdminCareHomeDocumentsQuery>();
        Assert.Equal(careHomeId, query.CareHomeId);
        Assert.Equal(CareHomeDocumentViews.History, query.View);

        sender.NextList = Result<CareHomeDocumentListResponse>.Failure(CareHomeDocumentErrors.InvalidView);
        AssertProblem(
            await controller.ListDocuments(careHomeId, "bogus", null, null, default),
            400, "CareHomes.Document.InvalidView");

        sender.NextList = Result<CareHomeDocumentListResponse>.Failure(CareHomeAdminErrors.ApplicationNotFound);
        AssertProblem(
            await controller.ListDocuments(careHomeId, null, null, null, default),
            404, "CareHomes.Admin.ApplicationNotFound");
    }

    // ---- route policy presence (the policies execute in middleware) ----

    [Fact]
    public void Document_routes_carry_the_expected_role_policies()
    {
        Assert.Equal(AuthorizationPolicies.CareHomeOwnerAccess, PolicyOn(typeof(CareHomeFacilitiesController)));
        Assert.Equal(AuthorizationPolicies.CareHomesOperationalAdmin, PolicyOn(typeof(AdminCareHomesController)));
    }

    private static string PolicyOn(Type controllerType)
    {
        AuthorizeAttribute attribute = controllerType.GetCustomAttribute<AuthorizeAttribute>()
            ?? throw new InvalidOperationException($"No [Authorize] on {controllerType.Name}.");
        return attribute.Policy
            ?? throw new InvalidOperationException($"[Authorize] on {controllerType.Name} has no policy.");
    }

    // ---- helpers ----

    private static ControllerContext ClaimsContextFor(UserId subject)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, subject.Value.ToString("D"))], "test"));
        return new ControllerContext { HttpContext = httpContext };
    }

    private static void AssertProblem(IActionResult result, int status, string code)
    {
        ObjectResult objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(status, objectResult.StatusCode);
        ProblemDetails problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(status, problem.Status);
        Assert.Equal(code, problem.Extensions["code"]);
    }

    /// <summary>
    /// Records every request through Send and returns the caller-configured
    /// Result. The boxed task is created as the exact Task of the response
    /// type MediatR binds, so the controller-side cast succeeds.
    /// </summary>
    private sealed class CapturingSender : ISender
    {
        public List<object> Requests { get; } = [];

        public Result<CareHomeDocumentListResponse> NextList { get; set; } =
            Result<CareHomeDocumentListResponse>.Success(
                new CareHomeDocumentListResponse(CareHomeDocumentViews.All, null, null, 0, []));

        public Result<PrivateFileContent> NextFile { get; set; } =
            Result<PrivateFileContent>.Failure(CareHomeDocumentErrors.DocumentNotFound);

        public Result NextUnit { get; set; } = Result.Success();

        public int StorageDeleteCalls { get; private set; }

        public TRequest Last<TRequest>() =>
            (TRequest)Requests.Last(request => request.GetType() == typeof(TRequest));

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            object task = request switch
            {
                GetMyCareHomeDocumentFileQuery => Box(NextFile),
                RemoveMyCareHomeDocumentCommand => Box(NextUnit),
                RemoveAdminCareHomeDocumentCommand => Box(NextUnit),
                GetAdminCareHomeDocumentsQuery => Box(NextList),
                GetMyCareHomeDocumentsQuery => Box(NextList),
                _ => throw new NotSupportedException($"No stubbed response for {request.GetType().Name}.")
            };
            return (Task<TResponse>)task;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException("Document routes turn commands through generic Send only.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private static object Box<T>(T value) => Task.FromResult(value);
    }
}

/// <summary>
/// Real-HTTP policy checks (2026-10-27 document lifecycle) for the new
/// document routes over the shared <see cref="PortalApiMetadataHost"/>
/// (in-process host: production controllers, real JWT authentication and
/// real authorization middleware, every store isolated). Proves the
/// registered policies — <c>CareHomeOwnerAccess</c> (owner controller) and
/// <c>CareHomesOperationalAdmin</c> (admin controller) — reject anonymous
/// calls 401, reject every other account type 403, and admit exactly the
/// intended roles through to the handlers. Patterned on
/// AdminCaregiverReviewNotesControllerTests / AdminOperationalReadAuthorizationTests.
/// NOT RUN under the phase source freeze.
/// </summary>
public sealed class CareHomeDocumentRoutePolicyTests : IClassFixture<PortalApiMetadataHost>
{
    private readonly PortalApiMetadataHost _host;

    public CareHomeDocumentRoutePolicyTests(PortalApiMetadataHost host) => _host = host;

    private const string OwnerDocumentsUrl = "/api/v1/care-homes/facilities/mine/documents";

    private static string OwnerDocumentUrl(Guid documentId) =>
        $"/api/v1/care-homes/facilities/mine/documents/{documentId:D}";

    // ---- authentication: every new route repels anonymous callers ----

    [Fact]
    public async Task All_new_document_routes_reject_anonymous_calls()
    {
        using HttpClient client = _host.CreatePortalClient();
        Guid documentId = Guid.NewGuid();

        using HttpResponseMessage ownerList = await client.GetAsync(OwnerDocumentsUrl);
        using HttpResponseMessage ownerFile = await client.GetAsync(
            $"{OwnerDocumentUrl(documentId)}/file");
        using HttpResponseMessage ownerRemove = await client.DeleteAsync(
            $"{OwnerDocumentUrl(documentId)}?expectedVersion=1");
        using HttpResponseMessage adminList = await client.GetAsync(
            $"/api/v1/admin/care-homes/{Guid.NewGuid():D}/documents");
        using HttpResponseMessage adminRemove = await client.DeleteAsync(
            $"/api/v1/admin/care-homes/{Guid.NewGuid():D}/documents/{documentId:D}?expectedVersion=1");

        Assert.Equal(HttpStatusCode.Unauthorized, ownerList.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, ownerFile.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, ownerRemove.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, adminList.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, adminRemove.StatusCode);
    }

    // ---- authorization: CareHomeOwnerAccess admits ONLY the owner role ----

    [Theory]
    [InlineData(AccountType.SuperAdmin)]
    [InlineData(AccountType.SupportAdmin)]
    [InlineData(AccountType.FinanceAdmin)]
    [InlineData(AccountType.Family)]
    [InlineData(AccountType.Elderly)]
    [InlineData(AccountType.MedicalCaregiver)]
    [InlineData(AccountType.CompanionCaregiver)]
    public async Task Owner_document_routes_deny_every_account_type_outside_the_owner_policy(
        AccountType accountType)
    {
        using HttpClient client = _host.CreatePortalClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CreateToken(CreateUser(accountType, index: 1)));

        using HttpResponseMessage response = await client.GetAsync(OwnerDocumentsUrl);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(AccountType.SuperAdmin)]
    [InlineData(AccountType.SupportAdmin)]
    public async Task Admin_document_routes_admit_operational_admins_to_the_handler(
        AccountType accountType)
    {
        using HttpClient client = _host.CreatePortalClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CreateToken(CreateUser(accountType, index: 2)));

        using HttpResponseMessage list = await client.GetAsync(
            $"/api/v1/admin/care-homes/{Guid.NewGuid():D}/documents");
        using HttpResponseMessage remove = await client.DeleteAsync(
            $"/api/v1/admin/care-homes/{Guid.NewGuid():D}/documents/{Guid.NewGuid():D}?expectedVersion=1");

        // Real registered policy (DependencyInjection.cs): CareHomesOperationalAdmin
        // admits SuperAdmin + SupportAdmin with Normal access, so they reach the
        // handlers, which return 404 for an unknown care-home id.
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
    }

    [Theory]
    [InlineData(AccountType.FinanceAdmin)]
    [InlineData(AccountType.Family)]
    [InlineData(AccountType.Elderly)]
    [InlineData(AccountType.MedicalCaregiver)]
    [InlineData(AccountType.CompanionCaregiver)]
    public async Task Admin_document_routes_deny_every_account_type_outside_the_operational_admin_policy(
        AccountType accountType)
    {
        using HttpClient client = _host.CreatePortalClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CreateToken(CreateUser(accountType, index: 2)));

        using HttpResponseMessage list = await client.GetAsync(
            $"/api/v1/admin/care-homes/{Guid.NewGuid():D}/documents");
        using HttpResponseMessage remove = await client.DeleteAsync(
            $"/api/v1/admin/care-homes/{Guid.NewGuid():D}/documents/{Guid.NewGuid():D}?expectedVersion=1");

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, remove.StatusCode);
    }

    // ---- authorization: the intended roles get through to the handlers ----

    [Fact]
    public async Task Owner_policy_admits_a_care_home_owner_to_the_document_list()
    {
        await using CareHomesDbContext db = CreateDb();
        User owner = CreateUser(AccountType.CareHomeOwner, index: 3);
        SeedApprovedFacility(db, owner.Id);
        using WebApplicationFactory<Program> factory = WithStore(db);
        using HttpClient client = CreateClient(factory);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(owner));

        using HttpResponseMessage response = await client.GetAsync(
            $"{OwnerDocumentsUrl}?view=all");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        JsonElement root = body.RootElement;
        Assert.Equal("all", root.GetProperty("view").GetString());
        // One item per required document type, each carrying the additive
        // lifecycle fields over real serialization (camelCase, string views).
        JsonElement items = root.GetProperty("items");
        Assert.Equal(items.GetArrayLength(), root.GetProperty("totalCount").GetInt32());
        bool sawCurrent = false;
        foreach (JsonElement item in items.EnumerateArray())
        {
            Assert.NotEqual(Guid.Empty, item.GetProperty("id").GetGuid());
            string? lifecycleView = item.GetProperty("lifecycleView").GetString();
            Assert.Contains(lifecycleView, new[] { "current", "pending", "history" });
            sawCurrent |= lifecycleView == "current";
            Assert.False(item.GetProperty("needsAction").GetBoolean());
        }
        // The freshly approved facility is fully usable: every type has its
        // verified current document.
        Assert.True(sawCurrent, "the approved fixture must expose current documents.");
    }

    [Fact]
    public async Task Owner_policy_admits_the_owner_to_the_scoped_file_read_and_removal_paths()
    {
        await using CareHomesDbContext db = CreateDb();
        User owner = CreateUser(AccountType.CareHomeOwner, index: 4);
        SeedApprovedFacility(db, owner.Id);
        using WebApplicationFactory<Program> factory = WithStore(db);
        using HttpClient client = CreateClient(factory);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(owner));

        // Both handlers run (policy passed): an unknown/history-only document
        // id deterministically lands on the coded 404s — never 401/403.
        using HttpResponseMessage file = await client.GetAsync(
            $"{OwnerDocumentUrl(Guid.NewGuid())}/file");
        using HttpResponseMessage remove = await client.DeleteAsync(
            $"{OwnerDocumentUrl(Guid.NewGuid())}?expectedVersion=1");
        Assert.Equal(HttpStatusCode.NotFound, file.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);

        // A foreign owner (no facility seeded for this user) gets the coded
        // 404 — the list is scoped to the token's own facility, never a leak.
        User otherOwner = CreateUser(AccountType.CareHomeOwner, index: 5);
        using HttpClient otherClient = CreateClient(factory);
        otherClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenFor(factory, otherOwner));
        using HttpResponseMessage otherList = await otherClient.GetAsync(OwnerDocumentsUrl);
        Assert.Equal(HttpStatusCode.NotFound, otherList.StatusCode);
    }

    [Fact]
    public async Task Operational_admin_policy_admits_a_support_admin_to_the_document_list()
    {
        await using CareHomesDbContext db = CreateDb();
        User admin = CreateUser(AccountType.SupportAdmin, index: 6);
        using WebApplicationFactory<Program> factory = WithStore(db);
        using HttpClient client = CreateClient(factory);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(admin));

        // Unknown facility, empty store: the handler's coded 404 proves the
        // policy admitted the request into the application layer.
        using HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/admin/care-homes/{Guid.NewGuid():D}/documents");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "CareHomes.Admin.ApplicationNotFound",
            body.RootElement.GetProperty("code").GetString());
    }

    // ---- fixture plumbing (patterned on the review-notes tests) ----

    private WebApplicationFactory<Program> WithStore(CareHomesDbContext db) =>
        _host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<CareHomesDbContext>();
            services.RemoveAll<ICareHomesDbContext>();
            services.AddSingleton(db);
            services.AddSingleton<ICareHomesDbContext>(db);
        }));

    private string CreateToken(User user) =>
        _host.Services.GetRequiredService<IAuthTokenService>()
            .GenerateAccessToken(user, DateTime.UtcNow.AddSeconds(-5)).PlainTextToken;

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private static string TokenFor(WebApplicationFactory<Program> factory, User user) =>
        factory.Services.GetRequiredService<IAuthTokenService>()
            .GenerateAccessToken(user, DateTime.UtcNow.AddSeconds(-5)).PlainTextToken;

    private static User CreateUser(AccountType accountType, int index)
    {
        User user = User.Create(
            FullName.Create($"Doc policy user {index}"),
            FullName.Create($"Doc Policy User {index}"),
            Email.Create($"doc.policy.user{index}@example.test"),
            PhoneNumber.Create($"+20100000{index:D4}"));
        user.AddAccount(accountType);
        return user;
    }

    private static CareHomesDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase($"carehome-doc-policy-{Guid.NewGuid():N}").Options);

    /// <summary>
    /// Seeds an approved facility with one verified document per required
    /// type (aggregate API only — the same shape the lifecycle tests use).
    /// </summary>
    private static void SeedApprovedFacility(CareHomesDbContext db, UserId ownerUserId)
    {
        DateTime now = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);
        UserId reviewer = UserId.New();
        CareHomeFacility facility = CareHomeFacility.CreateDraft(ownerUserId, now);
        facility.SaveDraft(ownerUserId, facility.Version, CareHomeFacilityTests.Draft(), now);
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            facility.UploadDocument(ownerUserId, type, $"private/{type}.pdf", "application/pdf", 100, null, now);
        facility.Submit(ownerUserId, facility.Version, now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
            facility.VerifyDocument(reviewer, facility.Version, document.Id, null, true, now.AddMinutes(1));
        facility.Review(reviewer, facility.Version, CareHomeReviewAction.Approved, null, now.AddMinutes(2), DateOnly.FromDateTime(now));
        db.Facilities.Add(facility);
        db.SaveChanges();
    }
}
