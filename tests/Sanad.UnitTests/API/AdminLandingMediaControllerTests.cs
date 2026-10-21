using System.Reflection;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Infrastructure.Storage;
using Sanad.Modules.Cms.Application.Landing;
using Sanad.Modules.Cms.Domain.Landing;
using Sanad.Modules.Cms.Infrastructure.Persistence;
using Sanad.Modules.Identity.Application.Authentication.Tokens;

namespace Sanad.UnitTests.API;

/// <summary>
/// E08 controller contract for the landing media delete route: the exact route
/// table (the delete route added to the nine existing routes), the
/// <c>CmsContent</c> policy and the real policy outcome per account type, the
/// local status mapping for the three <c>Cms.Landing.Media.*</c> codes, and the
/// end-to-end behaviour through the real handler and the real local storage.
/// </summary>
public sealed class AdminLandingMediaControllerTests : IClassFixture<PortalApiMetadataHost>
{
    private const string FreeKey = "landing/free.png";
    private const string ReferencedKey = "landing/referenced.png";

    private readonly PortalApiMetadataHost _host;

    public AdminLandingMediaControllerTests(PortalApiMetadataHost host) => _host = host;

    [Fact]
    public void Controller_KeepsTheExactRouteTable()
    {
        Assert.Equal(
            new[]
            {
                "DELETE media",
                "DELETE sections/{id:guid}",
                "GET sections",
                "POST media",
                "POST sections",
                "POST sections/reorder",
                "POST sections/{id:guid}/publish",
                "POST sections/{id:guid}/visibility",
                "PUT sections/{id:guid}",
            }.OrderBy(route => route, StringComparer.Ordinal).ToArray(),
            RoutesOf(typeof(AdminLandingController)));
    }

    [Fact]
    public void Controller_UsesCmsContentPolicyAndTheLandingRoute()
    {
        AuthorizeAttribute authorize = Assert.Single(
            typeof(AdminLandingController)
                .GetCustomAttributes<AuthorizeAttribute>(inherit: true));

        Assert.Equal(AuthorizationPolicies.CmsContent, authorize.Policy);

        Assert.Equal(
            "api/v1/admin/landing",
            typeof(AdminLandingController)
                .GetCustomAttributes<RouteAttribute>(inherit: true)
                .Single()
                .Template);

        // The policy sits on the controller, so it covers the delete route; the
        // route must not opt out of it.
        Assert.Empty(
            typeof(AdminLandingController)
                .GetMethod(nameof(AdminLandingController.DeleteMedia))!
                .GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
    }

    [Theory]
    [InlineData("SuperAdmin", "Normal", true)]
    [InlineData("ContentAdmin", "Normal", true)]
    [InlineData("SupportAdmin", "Normal", false)]
    [InlineData("FinanceAdmin", "Normal", false)]
    [InlineData("Family", "Normal", false)]
    [InlineData("MedicalCaregiver", "Normal", false)]
    [InlineData("CompanionCaregiver", "Normal", false)]
    [InlineData("Elderly", "Normal", false)]
    [InlineData("CareHomeOwner", "Normal", false)]
    [InlineData("ContentAdmin", "Restricted", false)]
    public async Task CmsContentPolicy_Outcomes(string accountType, string accessType, bool expected)
    {
        using var scope = _host.Services.CreateScope();
        var authorizationService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var policyProvider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await policyProvider.GetPolicyAsync(AuthorizationPolicies.CmsContent);
        Assert.NotNull(policy);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(AuthClaimNames.AccessType, accessType),
            new Claim(AuthClaimNames.AccountType, accountType),
        ], "Test"));

        var result = await authorizationService.AuthorizeAsync(principal, policy);

        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public async Task CmsContentPolicy_DeniesAnonymous()
    {
        using var scope = _host.Services.CreateScope();
        var authorizationService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var policyProvider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await policyProvider.GetPolicyAsync(AuthorizationPolicies.CmsContent);
        Assert.NotNull(policy);

        var result = await authorizationService.AuthorizeAsync(
            new ClaimsPrincipal(new ClaimsIdentity()),
            policy);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UnreferencedKey_Returns204WithAnEmptyBodyAndDeletesTheFile()
    {
        using var harness = new Harness();
        harness.WriteFile(FreeKey);

        IActionResult result = await harness.Controller.DeleteMedia(FreeKey);

        var noContent = Assert.IsType<NoContentResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, noContent.StatusCode);
        Assert.False(harness.FileExists(FreeKey));
    }

    [Fact]
    public async Task SameRequestTwice_Returns204Then404()
    {
        using var harness = new Harness();
        harness.WriteFile(FreeKey);

        IActionResult first = await harness.Controller.DeleteMedia(FreeKey);
        IActionResult second = await harness.Controller.DeleteMedia(FreeKey);

        Assert.IsType<NoContentResult>(first);

        (int status, string code, string traceId) = ReadProblem(second);
        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Equal(LandingErrors.MediaNotFound.Code, code);
        Assert.NotEmpty(traceId);
        Assert.False(harness.FileExists(FreeKey));
    }

    [Fact]
    public async Task ReferencedKey_Returns409AndKeepsTheFile()
    {
        using var harness = new Harness();
        harness.WriteFile(ReferencedKey);
        harness.Cms.LandingSections.Add(LandingSection.Create(
            LandingSectionType.Hero,
            "بطل",
            "Hero",
            imagePath: ReferencedKey));
        await harness.Cms.SaveChangesAsync();

        IActionResult result = await harness.Controller.DeleteMedia(ReferencedKey);

        (int status, string code, _) = ReadProblem(result);
        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Equal(LandingErrors.MediaReferenced.Code, code);
        Assert.True(harness.FileExists(ReferencedKey));
    }

    [Fact]
    public async Task WellFormedButMissingKey_Returns404()
    {
        using var harness = new Harness();

        IActionResult result = await harness.Controller.DeleteMedia("landing/absent.png");

        (int status, string code, _) = ReadProblem(result);
        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Equal(LandingErrors.MediaNotFound.Code, code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("private/landing/free.png")]
    [InlineData("documents/free.png")]
    [InlineData("landing")]
    [InlineData("landing/../free.png")]
    [InlineData("landing\\..\\free.png")]
    public async Task InvalidKey_Returns400AndNeverReachesTheCommandOrTheFile(string? key)
    {
        using var harness = new Harness();
        harness.WriteFile(FreeKey);
        string[] before = harness.Snapshot();

        IActionResult result = await harness.Controller.DeleteMedia(key);

        (int status, string code, _) = ReadProblem(result);
        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal(LandingErrors.MediaKeyInvalid.Code, code);
        Assert.Equal(before, harness.Snapshot());
        Assert.Empty(harness.Sender.Requests);
    }

    [Fact]
    public async Task HandlerReportedMissingFile_StillMaps404()
    {
        using var harness = new Harness();
        // The file exists, so the controller's probe passes and the handler's own
        // not-found answer is what reaches the mapping — the documented
        // check-then-delete race.
        harness.WriteFile(FreeKey);
        AdminLandingController controller = harness.NewController(
            new FixedSender(Result.Failure(LandingErrors.MediaNotFound)));

        IActionResult result = await controller.DeleteMedia(FreeKey);

        (int status, string code, _) = ReadProblem(result);
        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Equal(LandingErrors.MediaNotFound.Code, code);
    }

    [Fact]
    public async Task UnmappedHandlerFailure_Maps400AndKeepsTheHandlerCode()
    {
        using var harness = new Harness();
        harness.WriteFile(FreeKey);
        AdminLandingController controller = harness.NewController(
            new FixedSender(Result.Failure(StorageErrors.Empty)));

        IActionResult result = await controller.DeleteMedia(FreeKey);

        (int status, string code, _) = ReadProblem(result);
        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal(StorageErrors.Empty.Code, code);
    }

    private static string[] RoutesOf(Type controller) =>
        controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true))
            .SelectMany(attribute => attribute.HttpMethods.Select(
                verb => $"{verb} {attribute.Template}"))
            .OrderBy(route => route, StringComparer.Ordinal)
            .ToArray();

    private static (int Status, string Code, string TraceId) ReadProblem(IActionResult result)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);

        Assert.Equal(objectResult.StatusCode, problem.Status);
        Assert.Equal(
            objectResult.StatusCode,
            problem.Type is null ? null : int.Parse(problem.Type[(problem.Type.LastIndexOf('/') + 1)..]));

        return (
            objectResult.StatusCode!.Value,
            Assert.IsType<string>(problem.Extensions["code"]),
            Assert.IsType<string>(problem.Extensions["traceId"]));
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _root;

        public Harness()
        {
            _root = Path.Combine(Path.GetTempPath(), $"sanad-e08-api-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_root);

            Cms = new CmsDbContext(new DbContextOptionsBuilder<CmsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
            Cms.Database.EnsureCreated();

            StorageOptions = Options.Create(new LocalStorageOptions { RootPath = _root });
            Storage = new LocalDiskFileStorage(StorageOptions);
            Sender = new LandingSender(Cms, Storage);
            Controller = NewController(Sender);
        }

        public CmsDbContext Cms { get; }

        public IOptions<LocalStorageOptions> StorageOptions { get; }

        public LocalDiskFileStorage Storage { get; }

        public LandingSender Sender { get; }

        public AdminLandingController Controller { get; }

        public AdminLandingController NewController(ISender sender)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/v1/admin/landing/media";
            context.TraceIdentifier = "e08-controller-trace";

            return new AdminLandingController(sender, Storage, StorageOptions)
            {
                ControllerContext = new ControllerContext { HttpContext = context }
            };
        }

        public void WriteFile(string key)
        {
            string path = FullPath(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        }

        public bool FileExists(string key) => File.Exists(FullPath(key));

        public string[] Snapshot() =>
            Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(_root, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

        public void Dispose()
        {
            Cms.Dispose();

            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp folder is not a test result.
            }
        }

        private string FullPath(string key) =>
            Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));
    }

    private sealed class LandingSender(CmsDbContext cms, IFileStorage storage) : ISender
    {
        public List<object> Requests { get; } = [];

        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            object response = request switch
            {
                DeleteLandingMediaCommand command =>
                    (object)await new DeleteLandingMediaCommandHandler(cms, storage)
                        .Handle(command, cancellationToken),
                _ => throw new NotSupportedException($"Unexpected request {request.GetType().Name}.")
            };

            return (TResponse)response;
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

    private sealed class FixedSender(Result result) : ISender
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult((TResponse)(object)result);

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
