using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Ahlena.API.Controllers;
using Ahlena.BuildingBlocks.Application.Abstractions.Storage;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Infrastructure.Storage;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Cms.Application.Library;
using Ahlena.Modules.Cms.Domain.Library;
using Ahlena.Modules.Cms.Infrastructure.Persistence;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Domain.Subscriptions;
using Ahlena.Modules.Families.Infrastructure.Persistence;
using Ahlena.Modules.Identity.Application.Abstractions.Security;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.UnitTests.API;

/// <summary>
/// The single authorized app playback route. Publication, content type, the
/// premium entitlement and the stored object are all re-checked per request and
/// every failure — unknown, unpublished, not a video, no bytes, not entitled,
/// unreadable — collapses to the same 404, so the route is not an existence or
/// entitlement oracle. Serving enables byte ranges so the player can seek.
/// </summary>
public sealed class LibraryVideoReadControllerTests : IClassFixture<PortalApiMetadataHost>
{
    private const string StoredKey = "library-video/approved.mp4";
    private static readonly byte[] VideoBytes =
        [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D];

    private readonly PortalApiMetadataHost _host;

    public LibraryVideoReadControllerTests(PortalApiMetadataHost host) => _host = host;

    [Fact]
    public async Task Published_video_with_bytes_streams_the_stored_object_with_range_support()
    {
        await using var scope = await Scope.CreateAsync();
        UserId viewer = UserId.New();
        LibraryContent video = await scope.CreateVideoAsync(isPremium: false, publish: true, StoredKey);
        var storage = new FixedStorage(VideoBytes, "video/mp4");
        LibraryController controller = scope.CreateController(storage, viewer);

        IActionResult result = await controller.GetContentVideo(video.Id.Value, CancellationToken.None);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.True(file.EnableRangeProcessing);
        Assert.Equal("video/mp4", file.ContentType);
        using var body = new MemoryStream();
        await file.FileStream!.CopyToAsync(body);
        Assert.Equal(VideoBytes, body.ToArray());
        Assert.Equal([StoredKey], storage.OpenedKeys);
    }

    [Fact]
    public async Task Detail_and_playback_routes_agree_and_never_expose_the_storage_key()
    {
        await using var scope = await Scope.CreateAsync();
        UserId viewer = UserId.New();
        LibraryContent video = await scope.CreateVideoAsync(isPremium: false, publish: true, StoredKey);

        var detail = await new GetLibraryContentQueryHandler(scope.Cms, scope.Families)
            .Handle(new GetLibraryContentQuery(video.Id.Value, viewer.Value), CancellationToken.None);

        Assert.True(detail.IsSuccess);
        Assert.Equal(LibraryVideoUrls.For(video.Id.Value), detail.Value.VideoUrl);
        Assert.Equal($"/api/v1/library/content/{video.Id.Value:D}/video", detail.Value.VideoUrl);
        Assert.DoesNotContain(StoredKey, detail.Value.VideoUrl);
    }

    [Fact]
    public async Task Caller_without_a_verified_subject_is_unauthorized()
    {
        await using var scope = await Scope.CreateAsync();
        LibraryContent video = await scope.CreateVideoAsync(isPremium: false, publish: true, StoredKey);
        LibraryController controller = scope.CreateController(new FixedStorage(VideoBytes, "video/mp4"), subject: null);

        IActionResult result = await controller.GetContentVideo(video.Id.Value, CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Public_detail_hides_the_playback_route_without_entitlement_or_bytes()
    {
        await using var scope = await Scope.CreateAsync();
        UserId stranger = UserId.New();
        LibraryContent premium = await scope.CreateVideoAsync(isPremium: true, publish: true, StoredKey);
        LibraryContent withoutBytes = await scope.CreateVideoAsync(isPremium: false, publish: true, storageKey: null);
        LibraryContent article = await scope.CreatePublishedArticleAsync();
        var storage = new FixedStorage(VideoBytes, "video/mp4");
        var handler = new GetLibraryContentQueryHandler(scope.Cms, scope.Families);

        var premiumDetail = await handler.Handle(
            new GetLibraryContentQuery(premium.Id.Value, stranger.Value), CancellationToken.None);
        Assert.False(premiumDetail.Value.CanAccess);
        Assert.Null(premiumDetail.Value.VideoUrl);

        var bytesDetail = await handler.Handle(
            new GetLibraryContentQuery(withoutBytes.Id.Value, stranger.Value), CancellationToken.None);
        Assert.True(bytesDetail.Value.CanAccess);
        Assert.Null(bytesDetail.Value.VideoUrl);

        var articleDetail = await handler.Handle(
            new GetLibraryContentQuery(article.Id.Value, stranger.Value), CancellationToken.None);
        Assert.True(articleDetail.IsSuccess);
        Assert.Null(articleDetail.Value.VideoUrl);
        Assert.NotEmpty(articleDetail.Value.Sections);

        LibraryController controller = scope.CreateController(storage, stranger);
        AssertProblem(
            await controller.GetContentVideo(withoutBytes.Id.Value, CancellationToken.None),
            404, "Cms.LibraryContent.NotPublished");
        AssertProblem(
            await controller.GetContentVideo(article.Id.Value, CancellationToken.None),
            404, "Cms.LibraryContent.NotPublished");
        Assert.Empty(storage.OpenedKeys);
    }

    [Fact]
    public async Task Premium_bytes_require_the_premium_entitlement()
    {
        await using var scope = await Scope.CreateAsync();
        UserId subscriber = UserId.New();
        UserId stranger = UserId.New();
        scope.SeedPremiumFamily(subscriber);
        LibraryContent premium = await scope.CreateVideoAsync(isPremium: true, publish: true, StoredKey);
        var storage = new FixedStorage(VideoBytes, "video/mp4");

        AssertProblem(
            await scope.CreateController(storage, stranger)
                .GetContentVideo(premium.Id.Value, CancellationToken.None),
            404, "Cms.LibraryContent.NotPublished");
        Assert.Empty(storage.OpenedKeys);

        IActionResult result = await scope.CreateController(storage, subscriber)
            .GetContentVideo(premium.Id.Value, CancellationToken.None);
        Assert.True(Assert.IsType<FileStreamResult>(result).EnableRangeProcessing);
        Assert.Equal([StoredKey], storage.OpenedKeys);
    }

    [Fact]
    public async Task Unknown_draft_and_archived_targets_return_the_same_404()
    {
        await using var scope = await Scope.CreateAsync();
        UserId viewer = UserId.New();
        LibraryContent draft = await scope.CreateVideoAsync(isPremium: false, publish: false, StoredKey);
        LibraryContent archived = await scope.CreateVideoAsync(isPremium: false, publish: true, StoredKey);
        archived.Archive();
        await scope.Cms.SaveChangesAsync();
        var storage = new FixedStorage(VideoBytes, "video/mp4");
        LibraryController controller = scope.CreateController(storage, viewer);

        AssertProblem(
            await controller.GetContentVideo(Guid.NewGuid(), CancellationToken.None),
            404, "Cms.LibraryContent.NotPublished");
        AssertProblem(
            await controller.GetContentVideo(draft.Id.Value, CancellationToken.None),
            404, "Cms.LibraryContent.NotPublished");
        AssertProblem(
            await controller.GetContentVideo(archived.Id.Value, CancellationToken.None),
            404, "Cms.LibraryContent.NotPublished");
        Assert.Empty(storage.OpenedKeys);
    }

    [Fact]
    public async Task Unreadable_or_missing_stored_bytes_fail_like_an_unpublished_target()
    {
        await using var scope = await Scope.CreateAsync();
        UserId viewer = UserId.New();
        LibraryContent video = await scope.CreateVideoAsync(isPremium: false, publish: true, StoredKey);
        var storage = new FixedStorage(VideoBytes, "video/mp4", miss: true);

        IActionResult result = await scope.CreateController(storage, viewer)
            .GetContentVideo(video.Id.Value, CancellationToken.None);

        AssertProblem(result, 404, "Cms.LibraryContent.NotPublished");
        Assert.Equal([StoredKey], storage.OpenedKeys);
    }

    // ------------- real-host HTTP binding and byte-range middleware ------------
    //
    // The approved testing contract requires real HTTP-host evidence that the
    // changed playback route binds over the wire and that byte-range middleware
    // answers a partial response, not only that the controller method returns a
    // range-enabled result when called directly. These checks bind the
    // production controller through the shared PortalApiMetadataHost with
    // ISender and IFileStorage replaced by fakes: they establish routing, JWT
    // binding and the range middleware only. They are NOT persistence, NOT the
    // real filesystem, NOT a migration, NOT a consumer screen and NOT real
    // video content — the fixture is a synthetic 12-byte ftyp box.

    [Fact]
    public async Task Playback_through_the_host_answers_a_byte_range_with_206()
    {
        var sender = new HttpPlaybackSender();
        var storage = new FixedStorage(VideoBytes, "video/mp4");
        using WebApplicationFactory<Program> factory = HttpFactory(sender, storage);
        using HttpClient client = CreateHostClient(factory);
        Guid viewerId = CreateHostToken(client, AccountType.Family);
        Guid routeId = Guid.CreateVersion7();

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/library/content/{routeId:D}/video");
        request.Headers.Range = new RangeHeaderValue(0, 3);
        using HttpResponseMessage response = await client.SendAsync(request);

        // The range middleware answers a real partial response over HTTP.
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(
            $"bytes 0-3/{VideoBytes.Length}",
            response.Content.Headers.ContentRange?.ToString());
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(4L, response.Content.Headers.ContentLength.GetValueOrDefault());
        byte[] expected = [0x00, 0x00, 0x00, 0x18];
        Assert.Equal(expected, await response.Content.ReadAsByteArrayAsync());

        // The route id and the token's sub claim reach the query unchanged, and
        // only the authorized stored key is opened.
        var query = Assert.IsType<GetLibraryContentVideoQuery>(sender.LastQuery);
        Assert.Equal(routeId, query.Id);
        Assert.Equal(viewerId, query.ViewerUserId);
        Assert.Equal([StoredKey], storage.OpenedKeys);
    }

    // Real-filesystem companion to the range check above: the stored object is
    // produced and read by LocalDiskFileStorage over a private temporary root,
    // so the 206 comes from a real FileStream on disk — not from the synthetic
    // in-memory storage used by the routing-only checks. The asset is still a
    // synthetic 12-byte ftyp box; this is not real video content or a consumer
    // screen.
    [Fact]
    public async Task Playback_through_the_host_answers_a_byte_range_from_a_real_disk_file()
    {
        string rootPath = Path.Combine(
            Path.GetTempPath(),
            "ahlena-library-video-host",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        try
        {
            var storage = new LocalDiskFileStorage(
                Options.Create(new LocalStorageOptions { RootPath = rootPath }));

            using var upload = new MemoryStream(VideoBytes);
            Result<StoredFile> saved = await storage.SavePrivateAsync(
                upload,
                "video/mp4",
                VideoBytes.Length,
                "library-video",
                CancellationToken.None);

            Assert.True(saved.IsSuccess);
            Assert.StartsWith("private/library-video/", saved.Value.Key, StringComparison.Ordinal);

            string storedPath = Path.Combine(
                rootPath + "-private",
                saved.Value.Key.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(storedPath));

            using (WebApplicationFactory<Program> firstHost =
                HttpFactory(new HttpPlaybackSender(saved.Value.Key), storage))
            {
                using HttpClient client = CreateHostClient(firstHost);
                CreateHostToken(client, AccountType.Family);

                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"/api/v1/library/content/{Guid.CreateVersion7():D}/video");
                request.Headers.Range = new RangeHeaderValue(0, 3);
                using HttpResponseMessage response = await client.SendAsync(request);

                Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
                Assert.Equal(
                    $"bytes 0-3/{VideoBytes.Length}",
                    response.Content.Headers.ContentRange?.ToString());
                Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
                Assert.Equal(4L, response.Content.Headers.ContentLength.GetValueOrDefault());
                byte[] expected = [0x00, 0x00, 0x00, 0x18];
                Assert.Equal(expected, await response.Content.ReadAsByteArrayAsync());
            }

            // Simulated host restart: a brand-new host and a brand-new storage
            // instance over the same durable root. Nothing survives in memory, so
            // a successful full read proves the bytes were persisted on disk.
            var reopenedStorage = new LocalDiskFileStorage(
                Options.Create(new LocalStorageOptions { RootPath = rootPath }));
            using (WebApplicationFactory<Program> restartedHost =
                HttpFactory(new HttpPlaybackSender(saved.Value.Key), reopenedStorage))
            {
                using HttpClient client = CreateHostClient(restartedHost);
                CreateHostToken(client, AccountType.Family);

                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"/api/v1/library/content/{Guid.CreateVersion7():D}/video");
                using HttpResponseMessage response = await client.SendAsync(request);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
                Assert.Equal(VideoBytes, await response.Content.ReadAsByteArrayAsync());
            }
        }
        finally
        {
            if (Directory.Exists(rootPath))
                Directory.Delete(rootPath, true);

            string privateRootPath = rootPath + "-private";
            if (Directory.Exists(privateRootPath))
                Directory.Delete(privateRootPath, true);
        }
    }

    [Fact]
    public async Task Playback_through_the_host_rejects_an_unsatisfiable_byte_range_with_416()
    {
        var sender = new HttpPlaybackSender();
        var storage = new FixedStorage(VideoBytes, "video/mp4");
        using WebApplicationFactory<Program> factory = HttpFactory(sender, storage);
        using HttpClient client = CreateHostClient(factory);
        CreateHostToken(client, AccountType.Family);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/library/content/{Guid.CreateVersion7():D}/video");
        request.Headers.Range = new RangeHeaderValue(
            VideoBytes.Length + 10,
            VideoBytes.Length + 20);
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, response.StatusCode);
        Assert.Equal(
            $"bytes */{VideoBytes.Length}",
            response.Content.Headers.ContentRange?.ToString());
    }

    [Fact]
    public async Task Playback_through_the_host_is_unauthorized_without_a_token()
    {
        var sender = new HttpPlaybackSender();
        var storage = new FixedStorage(VideoBytes, "video/mp4");
        using WebApplicationFactory<Program> factory = HttpFactory(sender, storage);
        using HttpClient client = CreateHostClient(factory);

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/library/content/{Guid.CreateVersion7():D}/video");

        // The JWT bearer challenge answers before the action: no dispatch and no
        // stored object is opened.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(sender.AnyRequest);
        Assert.Empty(storage.OpenedKeys);
    }

    // ---- helpers ---------------------------------------------------------------

    private static void AssertProblem(IActionResult result, int status, string code)
    {
        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(status, response.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal(status, problem.Status);
        Assert.Equal(code, problem.Extensions["code"]);
        Assert.False(problem.Extensions.ContainsKey("errors"));
    }

    /// <summary>
    /// Derives a host from the shared fixture and replaces only ISender and
    /// IFileStorage, so the production routing table, JwtBearer pipeline and MVC
    /// options under test stay exactly as they ship in the application.
    /// </summary>
    private WebApplicationFactory<Program> HttpFactory(
        HttpPlaybackSender sender, IFileStorage storage) =>
        _host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISender>();
                services.AddSingleton<ISender>(sender);
                services.RemoveAll<IFileStorage>();
                services.AddSingleton<IFileStorage>(storage);
            }));

    private static HttpClient CreateHostClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    /// <summary>
    /// Issues the access token through the host's own IAuthTokenService (the
    /// fixture's issuer/audience/key overrides), so the JWT the production
    /// JwtBearer pipeline validates is exactly the token a real login emits and
    /// the sub claim is the user id the handlers must see.
    /// </summary>
    private Guid CreateHostToken(HttpClient client, AccountType accountType)
    {
        User user = User.Create(
            FullName.Create("مستخدم"),
            FullName.Create("Portal User"),
            Email.Create($"library.playback.{Guid.CreateVersion7():N}@example.test"),
            PhoneNumber.Create("+201000003008"));
        user.AddAccount(accountType);
        IAuthTokenService tokens = _host.Services.GetRequiredService<IAuthTokenService>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", tokens.GenerateAccessToken(user, DateTime.UtcNow.AddSeconds(-5)).PlainTextToken);
        return user.Id.Value;
    }

    private sealed class Scope : IAsyncDisposable
    {
        private Scope(CmsDbContext cms, FamiliesDbContext families)
        {
            Cms = cms;
            Families = families;
        }

        public CmsDbContext Cms { get; }
        public FamiliesDbContext Families { get; }

        public static Task<Scope> CreateAsync()
        {
            var cms = new CmsDbContext(new DbContextOptionsBuilder<CmsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
            cms.Database.EnsureCreated();
            var families = new FamiliesDbContext(new DbContextOptionsBuilder<FamiliesDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
            families.Database.EnsureCreated();
            return Task.FromResult(new Scope(cms, families));
        }

        public void SeedPremiumFamily(UserId owner)
        {
            var family = Family.Create(owner);
            var version = SubscriptionPlanVersion.Create(SubscriptionPlan.PremiumPlus);
            Families.Families.Add(family);
            Families.SubscriptionPlanVersions.Add(version);
            Families.FamilySubscriptions.Add(FamilySubscription.Create(family.Id, version));
            Families.SaveChanges();
        }

        public async Task<LibraryContent> CreateVideoAsync(
            bool isPremium, bool publish, string? storageKey)
        {
            var result = await new CreateLibraryContentCommandHandler(Cms).Handle(
                new CreateLibraryContentCommand(
                    LibraryContentType.Video, null, "فيديو", "Video", "ملخص", "Summary",
                    "https://youtu.be/abc123", 300, "library/cover.png", isPremium, null),
                CancellationToken.None);
            Assert.True(result.IsSuccess);
            LibraryContent content = await Cms.LibraryContents
                .SingleAsync(item => item.Id.Value == result.Value.Id);
            if (storageKey is not null)
                content.AttachVideo(storageKey);
            if (publish)
                content.Publish();
            await Cms.SaveChangesAsync();
            return content;
        }

        public async Task<LibraryContent> CreatePublishedArticleAsync()
        {
            var result = await new CreateLibraryContentCommandHandler(Cms).Handle(
                new CreateLibraryContentCommand(
                    LibraryContentType.Article, null, "عنوان", "Title", "ملخص", "Summary", null, null,
                    "library/cover.png", false,
                    [new LibraryContentSectionInput(1, "الأول", "First", ["نقطة"], ["bullet"])]),
                CancellationToken.None);
            Assert.True(result.IsSuccess);
            LibraryContent content = await Cms.LibraryContents
                .SingleAsync(item => item.Id.Value == result.Value.Id);
            content.Publish();
            await Cms.SaveChangesAsync();
            return content;
        }

        public LibraryController CreateController(IFileStorage storage, UserId? subject)
        {
            var httpContext = new DefaultHttpContext();
            if (subject is not null)
            {
                httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(JwtRegisteredClaimNames.Sub, subject.Value.Value.ToString("D"))], "test"));
            }

            return new LibraryController(new HandlerSender(Cms, Families), storage)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext }
            };
        }

        public ValueTask DisposeAsync()
        {
            Families.Dispose();
            Cms.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class HandlerSender(CmsDbContext cms, FamiliesDbContext families) : ISender
    {
        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object response = request switch
            {
                GetLibraryContentVideoQuery query =>
                    (object)await new GetLibraryContentVideoQueryHandler(cms, families)
                        .Handle(query, cancellationToken),
                GetLibraryContentQuery query =>
                    (object)await new GetLibraryContentQueryHandler(cms, families)
                        .Handle(query, cancellationToken),
                _ => throw new NotSupportedException(request.GetType().Name)
            };
            return (TResponse)response;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(
            object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Wire-level sender: answers the playback query with an in-memory
    /// authorized file response and records the query, so the HTTP tests
    /// exercise routing, JWT binding and range middleware only. No handler, no
    /// database, no entitlement computation and no migration runs.
    /// </summary>
    private sealed class HttpPlaybackSender(string storedKey = StoredKey) : ISender
    {
        public GetLibraryContentVideoQuery? LastQuery { get; private set; }

        public bool AnyRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            AnyRequest = true;
            object response = request switch
            {
                GetLibraryContentVideoQuery query => Record(query),
                _ => throw new NotSupportedException(request.GetType().Name)
            };
            return Task.FromResult((TResponse)response);
        }

        private Result<LibraryVideoFileResponse> Record(GetLibraryContentVideoQuery query)
        {
            LastQuery = query;
            return Result<LibraryVideoFileResponse>.Success(new LibraryVideoFileResponse(storedKey));
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(
            object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedStorage(byte[] bytes, string contentType, bool miss = false) : IFileStorage
    {
        public List<string> OpenedKeys { get; } = [];

        public Task<Result<StoredFile>> SaveAsync(
            Stream content, string contentType, long contentLength, string folder,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<StoredFile>> SavePrivateAsync(
            Stream content, string contentType, long contentLength, string folder,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<StoredFile>> SavePrivateAsync(
            Stream content, string contentType, long contentLength, string folder, long maximumBytes,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<PrivateFileContent>> OpenReadAsync(
            string key, CancellationToken cancellationToken = default)
        {
            OpenedKeys.Add(key);
            return miss
                ? Task.FromResult(Result<PrivateFileContent>.Failure(
                    new Error("Storage.File.NotFound", "missing")))
                : Task.FromResult<Result<PrivateFileContent>>(
                    new PrivateFileContent(key, contentType, new MemoryStream(bytes)));
        }

        public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
