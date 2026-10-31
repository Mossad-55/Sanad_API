using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ahlena.API.Authorization;
using Ahlena.API.Controllers;
using Ahlena.BuildingBlocks.Application.Idempotency;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Families.Application.CareRecords;
using Ahlena.Modules.Families.Domain.CareRecords;
using Ahlena.Modules.Identity.Application.Abstractions.Security;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.UnitTests.API;

/// <summary>
/// W1 care-record portal API surface: policy wiring per controller, the
/// read-only admin/elderly surfaces, the 400-before-auth source parse rule and
/// the delete contract (no body — expectedVersion from the query string).
/// </summary>
public sealed class CareRecordsPortalControllersTests : IClassFixture<PortalApiMetadataHost>
{
    private readonly PortalApiMetadataHost _host;

    public CareRecordsPortalControllersTests(PortalApiMetadataHost host) => _host = host;

    // ===================== Policy wiring per controller =====================

    [Theory]
    [InlineData(typeof(FamilyCareRecordsController), AuthorizationPolicies.FamilyAccess)]
    [InlineData(typeof(CaregiverCareRecordsController), AuthorizationPolicies.CaregiverAccess)]
    [InlineData(typeof(CareHomeCareRecordsController), AuthorizationPolicies.CareHomeOwnerAccess)]
    [InlineData(typeof(ElderlyCareRecordsController), AuthorizationPolicies.ElderlyAccess)]
    [InlineData(typeof(AdminCareRecordsController), AuthorizationPolicies.OperationalReadAdmin)]
    public void Controllers_CarryExactlyTheirLockedPolicy(Type controller, string policy)
    {
        AuthorizeAttribute attribute = Assert.Single(
            controller.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>());
        Assert.Equal(policy, attribute.Policy);
    }

    [Fact]
    public async Task Policies_ResolveFromTheLiveHost()
    {
        using var scope = _host.Services.CreateScope();
        var policyProvider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

        foreach (string policy in new[]
        {
            AuthorizationPolicies.FamilyAccess,
            AuthorizationPolicies.CaregiverAccess,
            AuthorizationPolicies.CareHomeOwnerAccess,
            AuthorizationPolicies.ElderlyAccess,
            AuthorizationPolicies.OperationalReadAdmin
        })
        {
            Assert.NotNull(await policyProvider.GetPolicyAsync(policy));
        }
    }

    // ================= Read-only surfaces have no write verbs =================

    [Fact]
    public void AdminSurface_IsReadOnlyByConstruction()
    {
        // No write verb anywhere on the admin surface.
        Assert.DoesNotContain(typeof(AdminCareRecordsController).GetMethods(),
            method => method.GetCustomAttributes(true).Any(a => a is HttpPostAttribute or HttpPutAttribute or HttpDeleteAttribute or HttpPatchAttribute));
        // And it is read-capable: list + detail GET routes exist.
        Assert.Equal(2, typeof(AdminCareRecordsController).GetMethods()
            .Count(method => method.GetCustomAttributes(true).Any(a => a is HttpGetAttribute)));
    }

    [Fact]
    public void ElderlySurface_IsReadOnlyByConstruction()
    {
        Assert.DoesNotContain(typeof(ElderlyCareRecordsController).GetMethods(),
            method => method.GetCustomAttributes(true).Any(a => a is HttpPostAttribute or HttpPutAttribute or HttpDeleteAttribute or HttpPatchAttribute));
        // And it never accepts a dependent id: the elderly principal is the scope.
        Assert.Contains(typeof(ElderlyCareRecordsController).GetMethods(),
            method => method.Name == nameof(ElderlyCareRecordsController.List)
                && method.GetParameters().All(p => p.Name != "dependentId"));
    }

    // ================== Delete contract: version via query ==================

    [Theory]
    [InlineData(typeof(FamilyCareRecordsController))]
    [InlineData(typeof(CaregiverCareRecordsController))]
    [InlineData(typeof(CareHomeCareRecordsController))]
    public void Delete_BindsExpectedVersionFromQuery(Type controller)
    {
        var deletes = controller.GetMethods()
            .Where(method => method.GetCustomAttributes(true).Any(a => a is HttpDeleteAttribute))
            .ToList();

        Assert.NotEmpty(deletes);
        Assert.All(deletes, method =>
        {
            var fromQuery = Assert.Single(method.GetParameters(), p => p.Name == "expectedVersion");
            Assert.NotNull(fromQuery.GetCustomAttributes(typeof(FromQueryAttribute), inherit: false));
        });
    }

    // =================== Behavior: 400 before authorization ==================

    [Fact]
    public async Task FamilyList_UnknownSourceValueIsBadRequest_BeforeAnySender()
    {
        var sender = new NeverSender();
        FamilyCareRecordsController controller = BuildController(sender);

        IActionResult result = await controller.List(
            Guid.NewGuid(), source: "Nope", visitDateFrom: null, visitDateTo: null,
            search: null, cancellationToken: default);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        Assert.Equal(CareRecordErrors.InvalidQuery.Code, problem.Extensions["code"]);
        Assert.Null(sender.LastRequest); // 400 happened before any dispatch
    }

    [Fact]
    public async Task ElderlyList_UnknownSourceValueIsBadRequest()
    {
        var sender = new NeverSender();
        ElderlyCareRecordsController controller =
            new(sender) { ControllerContext = AuthenticatedContext() };

        IActionResult result = await controller.List(
            source: "Nope", visitDateFrom: null, visitDateTo: null,
            search: null, cancellationToken: default);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal(CareRecordErrors.InvalidQuery.Code, problem.Extensions["code"]);
    }

    [Fact]
    public async Task FamilyList_AcceptsAnyCaseSourceName_AndNeverSendsNumerics()
    {
        var sender = new OkPageSender();
        FamilyCareRecordsController controller = BuildController(sender);

        IActionResult result = await controller.List(
            Guid.NewGuid(), source: "careHOME", visitDateFrom: null, visitDateTo: null,
            search: null, cancellationToken: default);

        Assert.IsType<OkObjectResult>(result);
        Assert.IsType<ListFamilyCareRecordsQuery>(sender.LastRequest);
        var sent = Assert.IsAssignableFrom<ListFamilyCareRecordsQuery>(sender.LastRequest);
        Assert.Equal(CareRecordSource.CareHome, sent.Source);
    }

    [Fact]
    public async Task FamilyList_UnauthenticatedPrincipalIsUnauthorized()
    {
        var controller = new FamilyCareRecordsController(new NeverSender())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        IActionResult result = await controller.List(
            Guid.NewGuid(), source: null, visitDateFrom: null, visitDateTo: null,
            search: null, cancellationToken: default);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task FamilyCreate_FailureSurfacesThroughTheCodedMapper()
    {
        // Pinned precedence: the scoped missing dependent renders 404 AFTER the
        // shared ResultProblemDetailsMapper amendment for Families.CareRecords.*
        // lands (Mastermind-owned shared amendment).
        var failing = new FamilyCareRecordsController(
            new FailureSender(CareRecordErrors.DependentNotFound))
        {
            ControllerContext = AuthenticatedContext()
        };

        IActionResult failure = await failing.Create(
            Guid.NewGuid(),
            new CreateCareRecordRequest("Hospital", new(2026, 10, 1), "details"),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(failure);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);

        // The 201-created success contract is declared on the route itself.
        Assert.Contains(
            typeof(FamilyCareRecordsController).GetMethod(nameof(FamilyCareRecordsController.Create))!
                .GetCustomAttributes(true),
            attribute => attribute is ProducesResponseTypeAttribute produces
                && produces.StatusCode == StatusCodes.Status201Created);
    }

    // ============== Real HTTP host: route binding + authorization ==============

    /// <summary>
    /// Locked route through the real host (no handler executes — ISender is
    /// replaced): the route dependent, the authenticated sub claim, and the
    /// parsed query (source name, page, pageSize) must all land on the sent
    /// query object exactly as the contract pins them.
    /// </summary>
    [Fact]
    public async Task FamilyList_ThroughHost_BindsRouteClaimAndQuery()
    {
        Guid dependentId = Guid.CreateVersion7();
        var sender = new PageCapturingSender();
        using var factory = CreateFactory(sender);
        using var client = CreatePortalClient(factory);
        User familyUser = CreateUser(AccountType.Family);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAccessToken(familyUser));

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/family/dependents/{dependentId:D}/care-records?page=2&pageSize=7&source=Caregiver");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        ListFamilyCareRecordsQuery query = Assert.IsType<ListFamilyCareRecordsQuery>(sender.LastRequest);
        Assert.Equal(dependentId, query.DependentId.Value);
        Assert.Equal(familyUser.Id, query.UserId);
        Assert.Equal(CareRecordSource.Caregiver, query.Source);
        Assert.Equal(2, query.Page);
        Assert.Equal(7, query.PageSize);
    }

    [Fact]
    public async Task FamilyList_ThroughHost_RejectsWrongRoleAndAnonymous()
    {
        var sender = new PageCapturingSender();
        Guid dependentId = Guid.CreateVersion7();
        using var factory = CreateFactory(sender);
        string route = $"/api/v1/family/dependents/{dependentId:D}/care-records?page=2&pageSize=7&source=Caregiver";

        // Anonymous: the JWT bearer challenge answers before any dispatch.
        using (var anonymous = CreatePortalClient(factory))
        {
            using HttpResponseMessage unauthorized = await anonymous.GetAsync(route);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        }

        // A non-family admin role never widens into the family surface.
        using (var adminClient = CreatePortalClient(factory))
        {
            User contentAdmin = CreateUser(AccountType.ContentAdmin);
            adminClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateAccessToken(contentAdmin));
            using HttpResponseMessage forbidden = await adminClient.GetAsync(route);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        Assert.Null(sender.LastRequest); // neither request reached MediatR
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateFactory(
        PageCapturingSender sender) =>
        _host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISender>();
                services.AddSingleton<ISender>(sender);
            }));

    private static HttpClient CreatePortalClient(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private static User CreateUser(AccountType accountType)
    {
        User user = User.Create(
            FullName.Create("مستخدم"),
            FullName.Create("Portal User"),
            Email.Create($"care.records.{Guid.NewGuid():N}@example.test"),
            PhoneNumber.Create("+201000004701"));
        user.AddAccount(accountType);
        return user;
    }

    private string CreateAccessToken(User user)
    {
        IAuthTokenService tokens = _host.Services.GetRequiredService<IAuthTokenService>();
        return tokens.GenerateAccessToken(user, DateTime.UtcNow.AddSeconds(-5)).PlainTextToken;
    }

    /// <summary>
    /// Captures the dispatched request and answers every list query with a
    /// stable empty paged envelope. Nothing touches a database: the fake
    /// replaces the whole MediatR pipeline for the duration of the request.
    /// </summary>
    private sealed class PageCapturingSender : ISender
    {
        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            Assert.IsType<ListFamilyCareRecordsQuery>(request);

            object page = Result<CareRecordsPage<CareRecordResponse>>.Success(
                CareRecordPageQuery.CreatePage<CareRecordResponse>(
                    [], 1, CareRecordPageSettings.DefaultPageSize, 0));

            return Task.FromResult((TResponse)page);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static FamilyCareRecordsController BuildController(ISender sender) =>
        new(sender) { ControllerContext = AuthenticatedContext() };

    private static ControllerContext AuthenticatedContext()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [new Claim(JwtRegisteredClaimNamesFallback, Guid.NewGuid().ToString())],
                "Test"))
        };
        httpContext.Request.Path = "/api/v1/family/dependents/00000000-0000-0000-0000-000000000000/care-records";
        httpContext.TraceIdentifier = "trace-care-records";

        return new ControllerContext { HttpContext = httpContext };
    }

    private const string JwtRegisteredClaimNamesFallback = "sub";

    /// <summary>Fails any dispatch: proves guards fire before MediatR.</summary>
    private sealed class NeverSender : ISender
    {
        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            throw new InvalidOperationException("No request may reach MediatR in this scenario.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            LastRequest = request;
            throw new InvalidOperationException("No request may reach MediatR in this scenario.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>Returns a locked empty paged envelope for any list query.</summary>
    private sealed class OkPageSender : ISender
    {
        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;

            object response = request switch
            {
                ListFamilyCareRecordsQuery =>
                    Result<CareRecordsPage<CareRecordResponse>>.Success(
                        CareRecordPageQuery.CreatePage<CareRecordResponse>([], 1, 20, 0)),
                _ => throw new NotSupportedException(request.GetType().Name)
            };

            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FailureSender(Error error) : ISender
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            object result = request switch
            {
                CreateFamilyCareRecordCommand => Result<IdempotentOutcome<CareRecordResponse>>.Failure(error),
                _ => throw new NotSupportedException()
            };
            return Task.FromResult((TResponse)result);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
