using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ahlena.API.Authorization;
using Ahlena.API.Controllers;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Infrastructure.Storage;
using Ahlena.Modules.Caregivers.Infrastructure.Persistence;
using Ahlena.Modules.Cms.Application.Landing;
using Ahlena.Modules.Cms.Domain.Landing;
using Ahlena.Modules.Cms.Infrastructure.Persistence;
using Ahlena.Modules.Families.Application.Families;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Infrastructure.Persistence;

namespace Ahlena.UnitTests.API;

/// <summary>
/// E05 controller contract for the family home CMS surface: the exact route
/// table (including the removed media-validation route), the local status
/// mapping for the eight <c>Cms.FamilyHome.*</c> codes, and the controller
/// behaviour driven through the real handlers - typed tokens, paging, media
/// checks, optimistic concurrency, deletion semantics and the Family read's
/// server-side access resolution.
/// </summary>
public sealed class FamilyHomeControllerTests
{
    private static readonly Guid Caller = Guid.Parse("4a1b6c2d-7e3f-4a51-9b0c-1d2e3f4a5b6c");

    // -----------------------------------------------------------------------
    // Route table and authorization
    // -----------------------------------------------------------------------

    [Fact]
    public void AdminController_ExposesExactlyTheEightOwnedRoutes()
    {
        IReadOnlyDictionary<string, string> routes = RoutesOf(typeof(AdminFamilyHomeController));

        Assert.Equal(
            [
                "DELETE {id:guid}", "GET", "GET {id:guid}", "POST", "POST reorder",
                "POST {id:guid}/publish", "POST {id:guid}/visibility", "PUT {id:guid}"
            ],
            routes.Keys.Order(StringComparer.Ordinal).ToArray());

        Assert.Equal("GetSections", routes["GET"]);
        Assert.Equal("GetSection", routes["GET {id:guid}"]);
        Assert.Equal("CreateSection", routes["POST"]);
        Assert.Equal("UpdateSection", routes["PUT {id:guid}"]);
        Assert.Equal("PublishSection", routes["POST {id:guid}/publish"]);
        Assert.Equal("SetVisibility", routes["POST {id:guid}/visibility"]);
        Assert.Equal("DeleteSection", routes["DELETE {id:guid}"]);
        Assert.Equal("ReorderSections", routes["POST reorder"]);
    }

    [Fact]
    public void AdminController_HasNoMediaValidationRoute()
    {
        IReadOnlyDictionary<string, string> routes = RoutesOf(typeof(AdminFamilyHomeController));

        Assert.DoesNotContain(
            routes.Keys,
            route => route.Contains("media", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("ValidateMedia", typeof(AdminFamilyHomeController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name));
    }

    [Fact]
    public void FamilyController_ExposesExactlyTheSinglePublishedRead()
    {
        IReadOnlyDictionary<string, string> routes = RoutesOf(typeof(FamilyHomeController));

        Assert.Equal(["GET sections"], routes.Keys.ToArray());
        Assert.Equal("GetSections", routes["GET sections"]);
    }

    [Theory]
    [InlineData(typeof(AdminFamilyHomeController), "api/v1/admin/family-home/sections", AuthorizationPolicies.CmsContent)]
    [InlineData(typeof(FamilyHomeController), "api/v1/family/home", AuthorizationPolicies.FamilyAccess)]
    public void Controllers_RequireExpectedRouteAndAuthorizationPolicy(
        Type controller,
        string route,
        string policy)
    {
        var routeAttribute = Assert.Single(controller.GetCustomAttributes<RouteAttribute>(inherit: true));
        var authorize = Assert.Single(controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true));

        Assert.Equal(route, routeAttribute.Template);
        Assert.Equal(policy, authorize.Policy);
    }

    // -----------------------------------------------------------------------
    // Local status mapping for the eight owned codes
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("Cms.FamilyHome.SectionInvalid", 400)]
    [InlineData("Cms.FamilyHome.InvalidKind", 400)]
    [InlineData("Cms.FamilyHome.Incomplete", 400)]
    [InlineData("Cms.FamilyHome.ReorderInvalid", 400)]
    [InlineData("Cms.FamilyHome.SectionNotFound", 404)]
    [InlineData("Cms.FamilyHome.StaleVersion", 409)]
    [InlineData("Cms.FamilyHome.PublishedHistoryConflict", 409)]
    [InlineData("Cms.FamilyHome.ImmutableHistory", 409)]
    [InlineData("Cms.FamilyHome.NotACodeAtAll", 400)]
    public void FamilyHomeCodes_MapToTheirOwnStatusesWithoutTouchingTheSharedMapper(
        string code,
        int expectedStatus)
    {
        var controller = new FamilyHomeController(new UnusedSender())
        {
            ControllerContext = new ControllerContext { HttpContext = Context("map-trace") }
        };

        IActionResult result = controller.ToFamilyHomeActionResult(
            Result.Failure(new Error(code, "test")));

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, response.StatusCode);

        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal(expectedStatus, problem.Status);
        Assert.Equal(code, problem.Extensions["code"]);
        Assert.Equal("map-trace", problem.Extensions["traceId"]);
        Assert.Equal($"https://httpstatuses.com/{expectedStatus}", problem.Type);
    }

    [Fact]
    public void SuccessfulResults_BecomeOkPayloadsAndNoContent()
    {
        FamilyHomeController controller = ControllerWith(new UnusedSender());

        Assert.IsType<OkObjectResult>(controller.ToFamilyHomeActionResult(Result<Guid>.Success(Guid.Empty)));
        Assert.IsType<NoContentResult>(controller.ToFamilyHomeActionResult(Result.Success()));
    }

    // -----------------------------------------------------------------------
    // Typed tokens and paging
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("sliders", null, "Cms.FamilyHome.InvalidKind")]
    [InlineData("2", null, "Cms.FamilyHome.InvalidKind")]
    [InlineData("quicklinks ", null, null)]
    [InlineData(null, "hidden", "Cms.FamilyHome.SectionInvalid")]
    [InlineData(null, "2", "Cms.FamilyHome.SectionInvalid")]
    [InlineData(null, "Published", null)]
    public async Task List_RejectsUnknownTokensLocallyAndAcceptsContractTokens(
        string? kind,
        string? status,
        string? expectedCode)
    {
        await using var harness = new Harness();

        IActionResult result = await harness.AdminController.GetSections(
            status, kind, search: null, page: 1, pageSize: 20, CancellationToken.None);

        if (expectedCode is null)
        {
            Assert.IsType<OkObjectResult>(result);
            return;
        }

        AssertProblem(result, 400, expectedCode);
        Assert.Empty(harness.Sender.Requests);
    }

    [Fact]
    public async Task List_FiltersByKindAndStatusAndClampsThePageSize()
    {
        await using var harness = new Harness();
        Guid quickLinks = await harness.SeedDraftAsync(
            FamilyHomeSectionKind.QuickLinks,
            """[{"arabicTitle":"الخدمات","englishTitle":"Services","target":{"type":"services"}}]""");
        await harness.SeedDraftAsync(FamilyHomeSectionKind.Banners, """[{"arabicTitle":"عرض","englishTitle":"Banner"}]""");

        IActionResult result = await harness.AdminController.GetSections(
            status: "draft", kind: "quick-links", search: null, page: 0, pageSize: 500, CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result);
        var page = Assert.IsType<FamilyHomeAdminPageResponse>(response.Value);

        Assert.Equal(100, page.PageSize);
        Assert.Equal(1, page.Page);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(quickLinks, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task Detail_UnknownId_IsSectionNotFound()
    {
        await using var harness = new Harness();

        IActionResult result = await harness.AdminController.GetSection(Guid.NewGuid(), CancellationToken.None);

        AssertProblem(result, 404, "Cms.FamilyHome.SectionNotFound");
    }

    // -----------------------------------------------------------------------
    // Media keys
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("private/secret.png")]
    [InlineData("landing/missing.png")]
    [InlineData("../outside.png")]
    public async Task Create_RejectsMediaKeysOutsideTheVerifiedPublicRootWithoutDispatching(string key)
    {
        await using var harness = new Harness();

        IActionResult result = await harness.AdminController.CreateSection(
            new CreateFamilyHomeSectionRequest(
                "banners",
                [new FamilyHomeItemRequest("عرض", "Banner", ImageKey: key)]),
            CancellationToken.None);

        AssertProblem(result, 400, "Cms.FamilyHome.SectionInvalid");
        Assert.Empty(harness.Sender.Requests);
        Assert.Empty(await harness.Cms.FamilyHomeSections.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Create_ThenPublish_WithAVerifiedPublicFile_IsCreatedThenPublished()
    {
        await using var harness = new Harness();
        harness.WritePublicFile("landing/hero.png");

        IActionResult created = await harness.AdminController.CreateSection(
            new CreateFamilyHomeSectionRequest(
                "banners",
                [new FamilyHomeItemRequest("عرض", "Banner", ImageKey: "landing/hero.png")]),
            CancellationToken.None);

        var createdResponse = Assert.IsType<ObjectResult>(created);
        Assert.Equal(StatusCodes.Status201Created, createdResponse.StatusCode);
        var section = Assert.IsType<FamilyHomeAdminSectionResponse>(createdResponse.Value);
        Assert.Equal("banners", section.Kind);
        Assert.Equal("Draft", section.Status);
        Assert.Equal("عرض", Assert.Single(section.Items).ArabicTitle);

        IActionResult published = await harness.AdminController.PublishSection(
            section.Id, new PublishFamilyHomeSectionRequest(ExpectedVersion: 0), CancellationToken.None);

        var publishedResponse = Assert.IsType<OkObjectResult>(published);
        var live = Assert.IsType<FamilyHomeAdminSectionResponse>(publishedResponse.Value);
        Assert.Equal("Published", live.Status);
        Assert.Equal(2, live.Version);
        Assert.NotNull(live.PublishedOnUtc);
    }

    [Fact]
    public async Task Publish_RejectsAStoredPrivateMediaKeyAndLeavesTheDraftAlone()
    {
        await using var harness = new Harness();
        Guid id = await harness.SeedDraftAsync(
            FamilyHomeSectionKind.Banners,
            """[{"arabicTitle":"عرض","englishTitle":"Banner","imageKey":"private/secret.png"}]""");

        IActionResult result = await harness.AdminController.PublishSection(
            id, new PublishFamilyHomeSectionRequest(ExpectedVersion: 0), CancellationToken.None);

        AssertProblem(result, 400, "Cms.FamilyHome.SectionInvalid");
        Assert.DoesNotContain(harness.Sender.Requests, request => request is PublishFamilyHomeSectionCommand);

        FamilyHomeSection stored = await harness.Cms.FamilyHomeSections.AsNoTracking().SingleAsync();
        Assert.Equal(FamilyHomeSectionStatus.Draft, stored.Status);
    }

    [Fact]
    public async Task Publish_UnknownId_IsSectionNotFound()
    {
        await using var harness = new Harness();

        IActionResult result = await harness.AdminController.PublishSection(
            Guid.NewGuid(), new PublishFamilyHomeSectionRequest(ExpectedVersion: 0), CancellationToken.None);

        AssertProblem(result, 404, "Cms.FamilyHome.SectionNotFound");
    }

    // -----------------------------------------------------------------------
    // Optimistic concurrency, immutability and deletion
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Update_MapsVersionPreconditionsToBadRequestAndConflict()
    {
        await using var harness = new Harness();
        Guid id = await harness.SeedDraftAsync(
            FamilyHomeSectionKind.QuickLinks,
            """[{"arabicTitle":"الخدمات","englishTitle":"Services","target":{"type":"services"}}]""");

        AssertProblem(
            await harness.AdminController.UpdateSection(
                id,
                new UpdateFamilyHomeSectionRequest(
                    [new FamilyHomeItemRequest("الخدمات", "Services", Target: new FamilyHomeTargetRequest("services"))],
                    DisplayOrder: 0,
                    IsVisible: true,
                    ExpectedVersion: -1),
                CancellationToken.None),
            400,
            "Cms.FamilyHome.SectionInvalid");

        AssertProblem(
            await harness.AdminController.UpdateSection(
                id,
                new UpdateFamilyHomeSectionRequest(
                    [new FamilyHomeItemRequest("الخدمات", "Services", Target: new FamilyHomeTargetRequest("services"))],
                    DisplayOrder: 0,
                    IsVisible: true,
                    ExpectedVersion: 99),
                CancellationToken.None),
            409,
            "Cms.FamilyHome.StaleVersion");

        IActionResult updated = await harness.AdminController.UpdateSection(
            id,
            new UpdateFamilyHomeSectionRequest(
                [new FamilyHomeItemRequest("الخدمات", "Services", Target: new FamilyHomeTargetRequest("services"))],
                DisplayOrder: 4,
                IsVisible: true,
                ExpectedVersion: 1),
            CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(updated);
        var section = Assert.IsType<FamilyHomeAdminSectionResponse>(response.Value);
        Assert.Equal(2, section.Version);
        Assert.Equal(4, section.DisplayOrder);
    }

    [Fact]
    public async Task Update_OnAnArchivedRow_IsImmutableHistory()
    {
        await using var harness = new Harness();
        Guid first = await harness.SeedDraftAsync(FamilyHomeSectionKind.QuickLinks, QuickLinkJson("First"));
        Assert.IsType<OkObjectResult>(await harness.AdminController.PublishSection(
            first, new PublishFamilyHomeSectionRequest(0), CancellationToken.None));

        Guid second = await harness.SeedDraftAsync(FamilyHomeSectionKind.QuickLinks, QuickLinkJson("Second"));
        Assert.IsType<OkObjectResult>(await harness.AdminController.PublishSection(
            second, new PublishFamilyHomeSectionRequest(0), CancellationToken.None));

        Assert.Equal(
            FamilyHomeSectionStatus.Archived,
            (await harness.ReadSectionAsync(first)).Status);

        AssertProblem(
            await harness.AdminController.UpdateSection(
                first,
                new UpdateFamilyHomeSectionRequest(
                    [new FamilyHomeItemRequest("أول", "First", Target: new FamilyHomeTargetRequest("services"))],
                    DisplayOrder: 0,
                    IsVisible: true,
                    ExpectedVersion: 0),
                CancellationToken.None),
            409,
            "Cms.FamilyHome.ImmutableHistory");
    }

    [Fact]
    public async Task Delete_RefusesPublishedHistoryAndDeletesDrafts()
    {
        await using var harness = new Harness();
        Guid published = await harness.SeedDraftAsync(FamilyHomeSectionKind.QuickLinks, QuickLinkJson("Live"));
        Assert.IsType<OkObjectResult>(await harness.AdminController.PublishSection(
            published, new PublishFamilyHomeSectionRequest(0), CancellationToken.None));

        AssertProblem(
            await harness.AdminController.DeleteSection(published, CancellationToken.None),
            409,
            "Cms.FamilyHome.PublishedHistoryConflict");

        AssertProblem(
            await harness.AdminController.DeleteSection(Guid.NewGuid(), CancellationToken.None),
            404,
            "Cms.FamilyHome.SectionNotFound");

        Guid draft = await harness.SeedDraftAsync(FamilyHomeSectionKind.QuickLinks, QuickLinkJson("Draft"));
        Assert.IsType<NoContentResult>(await harness.AdminController.DeleteSection(draft, CancellationToken.None));
        Assert.Equal(1, await harness.Cms.FamilyHomeSections.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task VisibilityAndReorder_ReturnNoContentAndRejectUnknownSections()
    {
        await using var harness = new Harness();
        Guid id = await harness.SeedDraftAsync(FamilyHomeSectionKind.QuickLinks, QuickLinkJson("Draft"));

        Assert.IsType<NoContentResult>(await harness.AdminController.SetVisibility(
            id, new SetFamilyHomeSectionVisibilityRequest(false, ExpectedVersion: 0), CancellationToken.None));
        Assert.False((await harness.ReadSectionAsync(id)).IsVisible);

        Assert.IsType<NoContentResult>(await harness.AdminController.ReorderSections(
            new ReorderFamilyHomeSectionsRequest([new FamilyHomeReorderEntryRequest(id, 3)]),
            CancellationToken.None));
        Assert.Equal(3, (await harness.ReadSectionAsync(id)).DisplayOrder);

        AssertProblem(
            await harness.AdminController.ReorderSections(
                new ReorderFamilyHomeSectionsRequest([new FamilyHomeReorderEntryRequest(Guid.NewGuid(), 0)]),
                CancellationToken.None),
            400,
            "Cms.FamilyHome.ReorderInvalid");
    }

    [Fact]
    public async Task MutatingRoutes_WithoutAnAuthenticatedSubject_AreUnauthorized()
    {
        await using var harness = new Harness();
        Guid id = await harness.SeedDraftAsync(FamilyHomeSectionKind.QuickLinks, QuickLinkJson("Draft"));
        harness.MakeAnonymous();

        Assert.IsType<UnauthorizedResult>(await harness.AdminController.PublishSection(
            id, new PublishFamilyHomeSectionRequest(0), CancellationToken.None));
        Assert.IsType<UnauthorizedResult>(await harness.AdminController.SetVisibility(
            id, new SetFamilyHomeSectionVisibilityRequest(true, 0), CancellationToken.None));
        Assert.IsType<UnauthorizedResult>(await harness.AdminController.DeleteSection(id, CancellationToken.None));
    }

    // -----------------------------------------------------------------------
    // Family read
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FamilyRead_WithoutAnAuthenticatedSubject_IsUnauthorized()
    {
        await using var harness = new Harness();
        harness.MakeAnonymous();

        Assert.IsType<UnauthorizedResult>(
            await harness.FamilyController.GetSections(CancellationToken.None));
    }

    [Fact]
    public async Task FamilyRead_ForACallerWithoutAFamily_IsFamiliesNotFound()
    {
        await using var harness = new Harness();

        IActionResult result = await harness.FamilyController.GetSections(CancellationToken.None);

        AssertProblem(result, 404, "Families.Family.NotFound");
    }

    [Fact]
    public async Task FamilyRead_ReturnsOnlyPublishedVisibleSectionsForTheResolvedFamily()
    {
        await using var harness = new Harness();
        await harness.SeedFamilyForCallerAsync();

        Guid published = await harness.SeedDraftAsync(FamilyHomeSectionKind.QuickLinks, QuickLinkJson("Live"));
        await harness.PublishStoredAsync(published);
        Guid draft = await harness.SeedDraftAsync(FamilyHomeSectionKind.QuickLinks, QuickLinkJson("Draft"));

        IActionResult result = await harness.FamilyController.GetSections(CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result);
        var sections = Assert.IsAssignableFrom<IReadOnlyList<FamilyHomeSectionPublicItem>>(response.Value);
        FamilyHomeSectionPublicItem section = Assert.Single(sections);

        Assert.Equal(published, section.Id);
        Assert.Equal("quick-links", section.Kind);
        Assert.NotEqual(draft, section.Id);

        FamilyHomePublicItem item = Assert.Single(section.Items);
        Assert.Equal("الخدمات", item.ArabicTitle);
        Assert.Equal("Live", item.EnglishTitle);
        Assert.Equal("services", item.TargetType);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string QuickLinkJson(string englishTitle) =>
        $"[{{\"arabicTitle\":\"الخدمات\",\"englishTitle\":\"{englishTitle}\"," +
        "\"target\":{\"type\":\"services\"}}]";

    private static IReadOnlyDictionary<string, string> RoutesOf(Type controller) =>
        controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(method => method
                .GetCustomAttributes<HttpMethodAttribute>(inherit: false)
                .SelectMany(attribute => attribute.HttpMethods.Select(
                    verb => new KeyValuePair<string, string>(
                        $"{verb} {attribute.Template ?? string.Empty}".TrimEnd(),
                        method.Name))))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static ProblemDetails AssertProblem(IActionResult result, int status, string code)
    {
        var response = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(status, response.StatusCode);

        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal(status, problem.Status);
        Assert.Equal(code, problem.Extensions["code"]);
        Assert.Equal("family-home-trace", problem.Extensions["traceId"]);
        return problem;
    }

    private static HttpContext Context(string traceId, Guid? userId = null)
    {
        var context = new DefaultHttpContext { TraceIdentifier = traceId };
        context.Request.Path = "/api/v1/admin/family-home/sections";

        if (userId is { } id)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(JwtRegisteredClaimNames.Sub, id.ToString("D"))],
                "test"));
        }

        return context;
    }

    private static FamilyHomeController ControllerWith(ISender sender) =>
        new(sender) { ControllerContext = new ControllerContext { HttpContext = Context("map-trace") } };

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string _storageRoot;

        public Harness()
        {
            _storageRoot = Path.Combine(Path.GetTempPath(), $"ahlena-e05-storage-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_storageRoot);

            Cms = InMemory<CmsDbContext>(options => new CmsDbContext(options));
            Caregivers = InMemory<CaregiversDbContext>(options => new CaregiversDbContext(options));
            Families = InMemory<FamiliesDbContext>(options => new FamiliesDbContext(options));

            Sender = new DispatchingSender(Cms, Caregivers, Families);
            var storage = Options.Create(new LocalStorageOptions { RootPath = _storageRoot });

            AdminController = new AdminFamilyHomeController(Sender, storage);
            FamilyController = new FamilyHomeController(Sender);
            Configure(Caller, authenticated: true);
        }

        public CmsDbContext Cms { get; }
        public CaregiversDbContext Caregivers { get; }
        public FamiliesDbContext Families { get; }
        public DispatchingSender Sender { get; }
        public AdminFamilyHomeController AdminController { get; }
        public FamilyHomeController FamilyController { get; }

        public void MakeAnonymous() => Configure(Caller, authenticated: false);

        public void WritePublicFile(string key)
        {
            string fullPath = Path.Combine(_storageRoot, key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, [0x89, 0x50, 0x4E, 0x47]);
        }

        public async Task<Guid> SeedDraftAsync(FamilyHomeSectionKind kind, string itemsJson)
        {
            FamilyHomeSection section = FamilyHomeSection.Create(kind, itemsJson, 0, isVisible: true);
            Cms.FamilyHomeSections.Add(section);
            await Cms.SaveChangesAsync();
            return section.Id.Value;
        }

        public async Task PublishStoredAsync(Guid id)
        {
            FamilyHomeSection section = await Cms.FamilyHomeSections.SingleAsync(
                row => row.Id == new FamilyHomeSectionId(id));
            section.Publish(section.Version);
            await Cms.SaveChangesAsync();
        }

        public async Task<FamilyHomeSection> ReadSectionAsync(Guid id) =>
            await Cms.FamilyHomeSections.AsNoTracking().SingleAsync(row => row.Id == new FamilyHomeSectionId(id));

        public async Task SeedFamilyForCallerAsync()
        {
            Families.Families.Add(Family.Create(new UserId(Caller)));
            await Families.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Cms.DisposeAsync();
            await Caregivers.DisposeAsync();
            await Families.DisposeAsync();

            try
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp folder is not a test result.
            }
        }

        private void Configure(Guid userId, bool authenticated)
        {
            AdminController.ControllerContext = new ControllerContext
            {
                HttpContext = Context("family-home-trace", authenticated ? userId : null)
            };
            FamilyController.ControllerContext = new ControllerContext
            {
                HttpContext = Context("family-home-trace", authenticated ? userId : null)
            };
        }

        private static TContext InMemory<TContext>(Func<DbContextOptions<TContext>, TContext> factory)
            where TContext : DbContext
        {
            var options = new DbContextOptionsBuilder<TContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            TContext db = factory(options);
            db.Database.EnsureCreated();
            return db;
        }
    }

    private sealed class DispatchingSender(
        CmsDbContext cms,
        CaregiversDbContext caregivers,
        FamiliesDbContext families) : ISender
    {
        public List<object> Requests { get; } = [];

        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            object response = request switch
            {
                CreateFamilyHomeSectionCommand command =>
                    (object)await new CreateFamilyHomeSectionCommandHandler(cms).Handle(command, cancellationToken),
                UpdateFamilyHomeSectionCommand command =>
                    (object)await new UpdateFamilyHomeSectionCommandHandler(cms).Handle(command, cancellationToken),
                PublishFamilyHomeSectionCommand command =>
                    (object)await new PublishFamilyHomeSectionCommandHandler(cms, caregivers)
                        .Handle(command, cancellationToken),
                SetFamilyHomeSectionVisibilityCommand command =>
                    (object)await new SetFamilyHomeSectionVisibilityCommandHandler(cms).Handle(command, cancellationToken),
                DeleteFamilyHomeSectionCommand command =>
                    (object)await new DeleteFamilyHomeSectionCommandHandler(cms).Handle(command, cancellationToken),
                ReorderFamilyHomeSectionsCommand command =>
                    (object)await new ReorderFamilyHomeSectionsCommandHandler(cms).Handle(command, cancellationToken),
                GetFamilyHomeAdminListQuery query =>
                    (object)await new GetFamilyHomeAdminListQueryHandler(cms).Handle(query, cancellationToken),
                GetFamilyHomeAdminSectionQuery query =>
                    (object)await new GetFamilyHomeAdminSectionQueryHandler(cms).Handle(query, cancellationToken),
                GetFamilyHomeAccessQuery query =>
                    (object)await new GetFamilyHomeAccessQueryHandler(families).Handle(query, cancellationToken),
                GetPublishedFamilyHomeSectionsQuery query =>
                    (object)await new GetPublishedFamilyHomeSectionsQueryHandler(cms, caregivers)
                        .Handle(query, cancellationToken),
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

    private sealed class UnusedSender : ISender
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

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
