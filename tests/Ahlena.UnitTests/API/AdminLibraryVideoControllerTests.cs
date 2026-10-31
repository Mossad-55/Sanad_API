using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ahlena.API.Controllers;
using Ahlena.BuildingBlocks.Application.Abstractions.Storage;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Cms.Application.Library;
using Ahlena.Modules.Cms.Domain.Library;
using Ahlena.Modules.Cms.Infrastructure.Persistence;
using Ahlena.Modules.Identity.Application.Abstractions.Security;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.UnitTests.API;

/// <summary>
/// The dedicated library video upload/remove routes: bytes are validated by
/// container signature (never by the declared MIME type alone), stored in the
/// private <c>library-video</c> area, and referenced only after the object
/// exists. Replacement deletes the superseded object after the new reference is
/// persisted, removal detaches first and deletes afterwards, so content never
/// points at missing bytes.
/// </summary>
public sealed class AdminLibraryVideoControllerTests : IClassFixture<PortalApiMetadataHost>
{
    private const string ExistingKey = "library-video/existing.mp4";

    private readonly PortalApiMetadataHost _host;

    public AdminLibraryVideoControllerTests(PortalApiMetadataHost host) => _host = host;

    [Fact]
    public async Task Upload_stores_private_bytes_and_returns_the_new_reference()
    {
        await using CmsDbContext db = CreateDb();
        LibraryContent video = await CreateVideoAsync(db, publish: true);
        var storage = new RecordingStorage();
        var sender = new DispatchingSender(db);
        AdminLibraryController controller = CreateController(sender, storage, "upload-trace");

        IActionResult result = await controller.UploadVideo(
            video.Id.Value, Mp4File(), CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<LibraryAdminContentResponse>(response.Value);
        Assert.Equal(storage.SavedKeys.Single(), body.VideoStorageKey);
        var upload = Assert.Single(storage.PrivateUploads);
        Assert.Equal("video/mp4", upload.ContentType);
        Assert.Equal("library-video", upload.Folder);
        Assert.Equal(52_428_800L, upload.MaximumBytes);
        // A first upload supersedes nothing, so no object is deleted.
        Assert.Empty(storage.DeletedKeys);
        LibraryContent reloaded = await ReadAsync(db, video.Id.Value);
        Assert.Equal(storage.SavedKeys.Single(), reloaded.VideoStorageKey);
        Assert.Equal("https://youtu.be/abc123", reloaded.VideoUrl);
    }

    [Fact]
    public async Task Upload_replaces_the_superseded_object_only_after_the_new_reference_is_stored()
    {
        await using CmsDbContext db = CreateDb();
        LibraryContent video = await CreateVideoAsync(db, publish: true);
        video.AttachVideo(ExistingKey);
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        AdminLibraryController controller = CreateController(new DispatchingSender(db), storage, "replace-trace");

        IActionResult result = await controller.UploadVideo(
            video.Id.Value, Mp4File(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        string uploaded = storage.SavedKeys.Single();
        Assert.Equal([ExistingKey], storage.DeletedKeys);
        Assert.Equal(uploaded, (await ReadAsync(db, video.Id.Value)).VideoStorageKey);
    }

    [Theory]
    [InlineData("image/png", "cover.png")]
    [InlineData("image/svg+xml", "cover.svg")]
    [InlineData("application/octet-stream", "video.mp4")]
    [InlineData("video/quicktime", "video.mov")]
    public async Task Upload_refuses_any_declared_type_other_than_mp4(string contentType, string fileName)
    {
        await using CmsDbContext db = CreateDb();
        LibraryContent video = await CreateVideoAsync(db);
        var storage = new RecordingStorage();
        AdminLibraryController controller = CreateController(new DispatchingSender(db), storage, "type-trace");

        IActionResult result = await controller.UploadVideo(
            video.Id.Value, FormFile(Mp4Bytes, contentType, fileName), CancellationToken.None);

        AssertProblem(result, 400, "Storage.File.UnsupportedType", "type-trace");
        Assert.Equal(0, storage.SaveCount);
        Assert.False((await ReadAsync(db, video.Id.Value)).HasStoredVideo);
    }

    [Fact]
    public async Task Upload_refuses_an_mp4_declaration_without_the_container_signature()
    {
        await using CmsDbContext db = CreateDb();
        LibraryContent video = await CreateVideoAsync(db);
        var storage = new RecordingStorage();
        AdminLibraryController controller = CreateController(new DispatchingSender(db), storage, "signature-trace");

        // Declared video/mp4 and named .mp4, but the bytes are a PNG.
        IActionResult result = await controller.UploadVideo(
            video.Id.Value, FormFile(PngBytes, "video/mp4", "video.mp4"), CancellationToken.None);

        AssertProblem(result, 400, "Storage.File.UnsupportedType", "signature-trace");
        Assert.Equal(0, storage.SaveCount);
        Assert.False((await ReadAsync(db, video.Id.Value)).HasStoredVideo);
    }

    [Fact]
    public async Task Upload_refuses_a_missing_empty_and_oversize_file()
    {
        await using CmsDbContext db = CreateDb();
        LibraryContent video = await CreateVideoAsync(db);
        var storage = new RecordingStorage();
        AdminLibraryController controller = CreateController(new DispatchingSender(db), storage, "size-trace");

        AssertProblem(
            await controller.UploadVideo(video.Id.Value, null, CancellationToken.None),
            400, "Storage.File.Empty", "size-trace");
        AssertProblem(
            await controller.UploadVideo(
                video.Id.Value, FormFile([], "video/mp4", "video.mp4"), CancellationToken.None),
            400, "Storage.File.Empty", "size-trace");
        AssertProblem(
            await controller.UploadVideo(
                video.Id.Value, OversizeMp4File(), CancellationToken.None),
            400, "Storage.File.TooLarge", "size-trace");

        Assert.Equal(0, storage.SaveCount);
        Assert.Empty(storage.DeletedKeys);
        Assert.False((await ReadAsync(db, video.Id.Value)).HasStoredVideo);
    }

    [Fact]
    public async Task Upload_on_unknown_content_returns_404_without_uploading()
    {
        await using CmsDbContext db = CreateDb();
        var storage = new RecordingStorage();
        AdminLibraryController controller = CreateController(new DispatchingSender(db), storage, "unknown-trace");

        IActionResult result = await controller.UploadVideo(Guid.NewGuid(), Mp4File(), CancellationToken.None);

        AssertProblem(result, 404, "Cms.LibraryContent.NotFound", "unknown-trace");
        Assert.Equal(0, storage.SaveCount);
    }

    [Fact]
    public async Task Upload_on_archived_content_returns_409_and_deletes_the_fresh_object()
    {
        await using CmsDbContext db = CreateDb();
        LibraryContent video = await CreateVideoAsync(db, publish: true);
        video.Archive();
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        AdminLibraryController controller = CreateController(new DispatchingSender(db), storage, "archived-trace");

        IActionResult result = await controller.UploadVideo(video.Id.Value, Mp4File(), CancellationToken.None);

        AssertProblem(result, 409, "Cms.LibraryContent.InvalidOperation", "archived-trace");
        Assert.Equal([storage.SavedKeys.Single()], storage.DeletedKeys);
        Assert.False((await ReadAsync(db, video.Id.Value)).HasStoredVideo);
    }

    [Fact]
    public async Task Upload_on_article_content_returns_400_and_deletes_only_the_fresh_object()
    {
        await using CmsDbContext db = CreateDb();
        LibraryContent article = await CreateArticleAsync(db);
        var storage = new RecordingStorage();
        AdminLibraryController controller = CreateController(new DispatchingSender(db), storage, "article-trace");

        IActionResult result = await controller.UploadVideo(article.Id.Value, Mp4File(), CancellationToken.None);

        ProblemDetails problem = AssertProblem(result, 400, "Cms.LibraryContent.Invalid", "article-trace");
        Assert.True(Errors(problem).ContainsKey("videoStorageKey"));
        Assert.Equal([storage.SavedKeys.Single()], storage.DeletedKeys);
        Assert.False((await ReadAsync(db, article.Id.Value)).HasStoredVideo);
    }

    [Fact]
    public async Task Upload_keeps_the_existing_object_when_the_attach_step_fails()
    {
        await using CmsDbContext db = CreateDb();
        // An archived target with a stored object is the reachable attach
        // failure: the fresh upload is removed and the existing reference is
        // left untouched.
        LibraryContent archived = await CreateVideoAsync(db, publish: true);
        archived.AttachVideo(ExistingKey);
        archived.Archive();
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        AdminLibraryController controller = CreateController(new DispatchingSender(db), storage, "keep-existing-trace");

        IActionResult result = await controller.UploadVideo(
            archived.Id.Value, Mp4File(), CancellationToken.None);

        AssertProblem(result, 409, "Cms.LibraryContent.InvalidOperation", "keep-existing-trace");
        Assert.Equal([storage.SavedKeys.Single()], storage.DeletedKeys);
        Assert.DoesNotContain(ExistingKey, storage.DeletedKeys);
        Assert.Equal(ExistingKey, (await ReadAsync(db, archived.Id.Value)).VideoStorageKey);
    }

    [Fact]
    public async Task Delete_detaches_the_reference_then_deletes_the_object()
    {
        await using CmsDbContext db = CreateDb();
        LibraryContent video = await CreateVideoAsync(db, publish: true);
        video.AttachVideo(ExistingKey);
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        AdminLibraryController controller = CreateController(new DispatchingSender(db), storage, "delete-trace");

        IActionResult result = await controller.DeleteVideo(video.Id.Value, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal([ExistingKey], storage.DeletedKeys);
        LibraryContent reloaded = await ReadAsync(db, video.Id.Value);
        Assert.Null(reloaded.VideoStorageKey);
        Assert.False(reloaded.HasStoredVideo);
    }

    [Fact]
    public async Task Delete_without_bytes_is_a_no_op_that_touches_no_object()
    {
        await using CmsDbContext db = CreateDb();
        LibraryContent video = await CreateVideoAsync(db, publish: true);
        var storage = new RecordingStorage();
        AdminLibraryController controller = CreateController(new DispatchingSender(db), storage, "noop-trace");

        Assert.IsType<NoContentResult>(
            await controller.DeleteVideo(video.Id.Value, CancellationToken.None));
        Assert.Empty(storage.DeletedKeys);
    }

    [Fact]
    public async Task Delete_maps_unknown_article_and_archived_targets()
    {
        await using CmsDbContext db = CreateDb();
        LibraryContent article = await CreateArticleAsync(db);
        LibraryContent archived = await CreateVideoAsync(db, publish: true);
        archived.AttachVideo(ExistingKey);
        archived.Archive();
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        AdminLibraryController controller = CreateController(new DispatchingSender(db), storage, "guard-trace");

        AssertProblem(
            await controller.DeleteVideo(Guid.NewGuid(), CancellationToken.None),
            404, "Cms.LibraryContent.NotFound", "guard-trace");
        AssertProblem(
            await controller.DeleteVideo(article.Id.Value, CancellationToken.None),
            400, "Cms.LibraryContent.Invalid", "guard-trace");
        AssertProblem(
            await controller.DeleteVideo(archived.Id.Value, CancellationToken.None),
            409, "Cms.LibraryContent.InvalidOperation", "guard-trace");
        Assert.Empty(storage.DeletedKeys);
        Assert.Equal(ExistingKey, (await ReadAsync(db, archived.Id.Value)).VideoStorageKey);
    }

    // ---------------- real-host HTTP binding, route and policy ----------------
    //
    // The approved testing contract requires real HTTP-host evidence that the
    // changed media routes bind and authorize over the wire, not only that the
    // controller method works when called directly. These checks bind the
    // production controller through the shared PortalApiMetadataHost with
    // ISender and IFileStorage replaced by fakes: they establish routing,
    // multipart binding, policy authorization and the returned reference only.
    // They are NOT persistence, NOT the real filesystem, NOT a migration, NOT a
    // consumer screen and NOT real video content — the upload fixture is a
    // synthetic 16-byte ftyp box that no decoder ever sees.

    [Fact]
    public async Task Upload_video_through_the_host_binds_the_route_id_and_the_private_key()
    {
        var sender = new HttpBindingSender();
        var storage = new HttpBindingStorage();
        using WebApplicationFactory<Program> factory = HttpFactory(sender, storage);
        using HttpClient client = CreateHostClient(factory);
        CreateHostToken(client, AccountType.ContentAdmin);
        Guid routeId = Guid.CreateVersion7();

        using HttpResponseMessage response = await client.PostAsync(
            $"/api/v1/admin/library/content/{routeId:D}/video", MultipartMp4());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The attach command carries the route id and the key private storage
        // handed back — never a client-supplied id or path.
        var attach = Assert.IsType<AttachLibraryContentVideoCommand>(sender.LastAttachCommand);
        Assert.Equal(routeId, attach.Id);
        Assert.Equal(HttpBindingStorage.UploadedKey, attach.StorageKey);

        // The file reached private storage as video/mp4 in the library-video area.
        Assert.Equal(1, storage.SaveCount);
        Assert.Equal("video/mp4", storage.LastContentType);
        Assert.Equal("library-video", storage.LastFolder);
        Assert.Equal(Mp4Bytes, storage.LastBytes);

        // A first upload supersedes nothing, so no object is deleted.
        Assert.Empty(storage.DeletedKeys);

        // The wire body carries the private reference, never the raw bytes.
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            HttpBindingStorage.UploadedKey,
            body.RootElement.GetProperty("videoStorageKey").GetString());
    }

    [Fact]
    public async Task Upload_video_through_the_host_denies_anonymous_and_disallowed_roles()
    {
        var sender = new HttpBindingSender();
        var storage = new HttpBindingStorage();
        using WebApplicationFactory<Program> factory = HttpFactory(sender, storage);
        string route = $"/api/v1/admin/library/content/{Guid.CreateVersion7():D}/video";

        // No bearer token: the JWT bearer challenge answers 401.
        using (HttpClient anonymous = CreateHostClient(factory))
        {
            using HttpResponseMessage unauthorized = await anonymous.PostAsync(route, MultipartMp4());
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        }

        // An authenticated Normal token without the CMS content role is 403: the
        // policy denies in the pipeline before the action, MediatR or storage.
        using (HttpClient family = CreateHostClient(factory))
        {
            CreateHostToken(family, AccountType.Family);
            using HttpResponseMessage forbidden = await family.PostAsync(route, MultipartMp4());
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        Assert.False(sender.AnyRequest);
        Assert.Null(sender.LastAttachCommand);
        Assert.Equal(0, storage.SaveCount);
        Assert.Empty(storage.DeletedKeys);
    }

    // ---- helpers ---------------------------------------------------------------

    private static readonly byte[] PngBytes =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03, 0x04];

    private static readonly byte[] Mp4Bytes =
        [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D, 0x00, 0x00, 0x02, 0x00];

    private static FormFile Mp4File() => FormFile(Mp4Bytes, "video/mp4", "lesson.mp4");

    private static FormFile OversizeMp4File()
    {
        var file = new FormFile(new MemoryStream(Mp4Bytes), 0, 52_428_801, "file", "lesson.mp4");
        file.Headers = new HeaderDictionary { ["Content-Type"] = "video/mp4" };
        return file;
    }

    private static FormFile FormFile(byte[] bytes, string contentType, string fileName)
    {
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName);
        file.Headers = new HeaderDictionary { ["Content-Type"] = contentType };
        return file;
    }

    private static ProblemDetails AssertProblem(
        IActionResult result, int status, string code, string traceId)
    {
        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(status, response.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal(status, problem.Status);
        Assert.Equal(code, problem.Extensions["code"]);
        Assert.Equal(traceId, problem.Extensions["traceId"]);
        return problem;
    }

    private static Dictionary<string, string[]> Errors(ProblemDetails problem)
    {
        Assert.True(problem.Extensions.TryGetValue("errors", out object? errors));
        return Assert.IsType<Dictionary<string, string[]>>(errors);
    }

    private static async Task<LibraryContent> CreateVideoAsync(CmsDbContext db, bool publish = false)
    {
        var result = await new CreateLibraryContentCommandHandler(db).Handle(
            new CreateLibraryContentCommand(
                LibraryContentType.Video, null, "فيديو", "Video", "ملخص", "Summary",
                "https://youtu.be/abc123", 300, "library/cover.png", false, null),
            CancellationToken.None);
        Assert.True(result.IsSuccess);
        // Tracked on purpose: the lifecycle steps below mutate the aggregate.
        LibraryContent content = await db.LibraryContents
            .SingleAsync(item => item.Id.Value == result.Value.Id);
        if (publish)
        {
            content.Publish();
            await db.SaveChangesAsync();
        }

        return content;
    }

    private static async Task<LibraryContent> CreateArticleAsync(CmsDbContext db)
    {
        var result = await new CreateLibraryContentCommandHandler(db).Handle(
            new CreateLibraryContentCommand(
                LibraryContentType.Article, null, "عنوان", "Title", "ملخص", "Summary", null, null,
                "library/cover.png", false, [new LibraryContentSectionInput(1, "الأول", "First", ["نقطة"], ["bullet"])]),
            CancellationToken.None);
        Assert.True(result.IsSuccess);
        return await db.LibraryContents.SingleAsync(item => item.Id.Value == result.Value.Id);
    }

    private static async Task<LibraryContent> ReadAsync(CmsDbContext db, Guid id) =>
        await db.LibraryContents
            .AsNoTracking()
            .Include(content => content.Sections)
            .SingleAsync(content => content.Id.Value == id);

    private static CmsDbContext CreateDb()
    {
        var db = new CmsDbContext(new DbContextOptionsBuilder<CmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static AdminLibraryController CreateController(
        ISender sender, IFileStorage storage, string traceId)
    {
        var httpContext = new DefaultHttpContext { TraceIdentifier = traceId };
        return new AdminLibraryController(sender, storage)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    /// <summary>
    /// Derives a host from the shared fixture and replaces only ISender and
    /// IFileStorage, so the production routing table, JwtBearer pipeline and MVC
    /// options under test stay exactly as they ship in the application.
    /// </summary>
    private WebApplicationFactory<Program> HttpFactory(
        HttpBindingSender sender, HttpBindingStorage storage) =>
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
            Email.Create($"library.video.{Guid.CreateVersion7():N}@example.test"),
            PhoneNumber.Create("+201000003007"));
        user.AddAccount(accountType);
        IAuthTokenService tokens = _host.Services.GetRequiredService<IAuthTokenService>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", tokens.GenerateAccessToken(user, DateTime.UtcNow.AddSeconds(-5)).PlainTextToken);
        return user.Id.Value;
    }

    /// <summary>
    /// One synthetic upload part: a 16-byte MP4 <c>ftyp</c> box (<c>isom</c>
    /// brand) declared <c>video/mp4</c> in the multipart <c>file</c> field, so
    /// the route's container-marker check passes. It is not playable media.
    /// </summary>
    private static MultipartFormDataContent MultipartMp4()
    {
        var file = new ByteArrayContent(Mp4Bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        var form = new MultipartFormDataContent();
        form.Add(file, "file", "lesson.mp4");
        return form;
    }

    private sealed class DispatchingSender(CmsDbContext db) : ISender
    {
        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object response = request switch
            {
                CreateLibraryContentCommand command =>
                    (object)await new CreateLibraryContentCommandHandler(db).Handle(command, cancellationToken),
                GetLibraryAdminContentQuery query =>
                    (object)await new GetLibraryAdminContentQueryHandler(db).Handle(query, cancellationToken),
                AttachLibraryContentVideoCommand command =>
                    (object)await new AttachLibraryContentVideoCommandHandler(db).Handle(command, cancellationToken),
                RemoveLibraryContentVideoCommand command =>
                    (object)await new RemoveLibraryContentVideoCommandHandler(db).Handle(command, cancellationToken),
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

    private sealed class RecordingStorage : IFileStorage
    {
        private int _counter;

        public int SaveCount { get; private set; }
        public List<string> SavedKeys { get; } = [];
        public List<string> DeletedKeys { get; } = [];
        public List<(string ContentType, string Folder, long MaximumBytes)> PrivateUploads { get; } = [];

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
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            PrivateUploads.Add((contentType, folder, maximumBytes));
            string key = $"{folder}/test-{++_counter}.mp4";
            SavedKeys.Add(key);
            return Task.FromResult<Result<StoredFile>>(new StoredFile(key));
        }

        public Task<Result<PrivateFileContent>> OpenReadAsync(
            string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            DeletedKeys.Add(key);
            return Task.FromResult(Result.Success());
        }
    }

    /// <summary>
    /// Wire-level sender: answers the existence pre-check and the attach with
    /// in-memory responses and records the attach command, so the HTTP tests
    /// exercise routing/binding only. No handler, no database and no migration
    /// runs.
    /// </summary>
    private sealed class HttpBindingSender : ISender
    {
        public AttachLibraryContentVideoCommand? LastAttachCommand { get; private set; }

        public bool AnyRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            AnyRequest = true;
            object response = request switch
            {
                GetLibraryAdminContentQuery => Result<LibraryAdminContentResponse>.Success(
                    AdminResponse(videoStorageKey: null)),
                AttachLibraryContentVideoCommand command => Result<LibraryAdminContentResponse>.Success(
                    AdminResponse(Record(command))),
                _ => throw new NotSupportedException(request.GetType().Name)
            };
            return Task.FromResult((TResponse)response);
        }

        private string Record(AttachLibraryContentVideoCommand command)
        {
            LastAttachCommand = command;
            return command.StorageKey;
        }

        private static LibraryAdminContentResponse AdminResponse(string? videoStorageKey) => new(
            Guid.CreateVersion7(),
            nameof(LibraryContentType.Video),
            nameof(LibraryContentStatus.Draft),
            "فيديو", "Video", "ملخص", "Summary",
            "https://youtu.be/abc123",
            videoStorageKey,
            300,
            "library/cover.png",
            null, null, null,
            false,
            0, 0,
            DateTime.UtcNow, DateTime.UtcNow, null,
            []);

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
    /// Captures the exact bytes, content type, folder and byte ceiling the
    /// production controller hands to private storage over the wire, and answers
    /// with an in-memory key. Nothing is written to disk and no provider is
    /// contacted.
    /// </summary>
    private sealed class HttpBindingStorage : IFileStorage
    {
        public const string UploadedKey = "library-video/wire-upload.mp4";

        public int SaveCount { get; private set; }
        public string? LastContentType { get; private set; }
        public string? LastFolder { get; private set; }
        public byte[]? LastBytes { get; private set; }
        public List<string> DeletedKeys { get; } = [];

        public Task<Result<StoredFile>> SaveAsync(
            Stream content, string contentType, long contentLength, string folder,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<StoredFile>> SavePrivateAsync(
            Stream content, string contentType, long contentLength, string folder,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<Result<StoredFile>> SavePrivateAsync(
            Stream content, string contentType, long contentLength, string folder, long maximumBytes,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            LastContentType = contentType;
            LastFolder = folder;
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            LastBytes = buffer.ToArray();
            return new StoredFile(UploadedKey);
        }

        public Task<Result<PrivateFileContent>> OpenReadAsync(
            string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            DeletedKeys.Add(key);
            return Task.FromResult(Result.Success());
        }
    }
}
