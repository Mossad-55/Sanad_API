using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Infrastructure.Storage;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Application.Attachments;
using Sanad.Modules.Chat.Application.Conversations;
using Sanad.Modules.Chat.Domain.Attachments;
using Sanad.Modules.Chat.Domain.Blocks;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Domain.Messages;
using Sanad.Modules.Chat.Domain.Reports;
using Sanad.Modules.Chat.Infrastructure.Persistence;

namespace Sanad.UnitTests.Chat;

/// <summary>
/// Endpoints 5 (upload) and 6 (serve) over a REAL
/// <see cref="LocalDiskFileStorage"/> rooted in a Guid-isolated temp
/// directory (deleted on dispose) plus an EF InMemory ChatDbContext — the
/// same provenance shape as the community image-upload tests.
/// </summary>
public sealed class AttachmentTests : IDisposable
{
    private static DateTime UtcStamp =>
        DateTime.SpecifyKind(new DateTime(2026, 10, 2, 9, 0, 0), DateTimeKind.Utc);

    private static readonly byte[] JpegBytes = [0xff, 0xd8, 0xff, 0x00, 0x01, 0x02];
    private static readonly byte[] PngBytes = [137, 80, 78, 71, 13, 10, 26, 10, 0x00];
    private static readonly byte[] WebpBytes =
    [
        (byte)'R', (byte)'I', (byte)'F', (byte)'F',
        0x00, 0x00, 0x00, 0x00,
        (byte)'W', (byte)'E', (byte)'B', (byte)'P', 0x00
    ];
    private static readonly byte[] Mp4Bytes =
    [
        0x00, 0x00, 0x00, 0x18,
        (byte)'f', (byte)'t', (byte)'y', (byte)'p',
        (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0x00
    ];
    private static readonly byte[] PdfBytes =
        [(byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', (byte)'1', (byte)'.', (byte)'7'];

    private readonly string _storageRoot = Path.Combine(
        Path.GetTempPath(),
        $"chat-attachment-test-{Guid.NewGuid():N}");

    // ------------------------------------------------------------------
    // Endpoint 5 — upload handler over real temp-root storage.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Upload_PersistsConversationScopedUnboundRecordUnderPrivateStorage()
    {
        await using ChatDbContext db = CreateDb();
        UserId uploader = UserId.New();
        UserId peer = UserId.New();
        Conversation conversation = await AddConversationAsync(db, uploader, peer);
        var handler = CreateUploadHandler(db);

        var result = await handler.Handle(
            new UploadAttachmentCommand(
                uploader,
                conversation.Id,
                "holiday photo.JPG",
                "image/jpeg",
                JpegBytes.Length,
                new MemoryStream(JpegBytes)),
            default);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value.AttachmentId);
        Assert.Equal("holiday photo.JPG", result.Value.FileName);
        Assert.Equal("image/jpeg", result.Value.ContentType);
        Assert.Equal(JpegBytes.Length, result.Value.SizeBytes);

        Attachment stored = await db.Attachments.SingleAsync();
        Assert.Equal(conversation.Id, stored.ConversationId);
        Assert.Equal(uploader, stored.UploadedBy);
        Assert.False(stored.IsBound); // Lane B binds it to exactly one message.
        Assert.StartsWith("private/chat-attachments/", stored.StorageKey);
        Assert.EndsWith(".jpg", stored.StorageKey);
        Assert.True(File.Exists(ResolvePrivatePath(stored.StorageKey)));
    }

    [Fact]
    public async Task Upload_AcceptsPngWebpMp4AndPdfWithMatchingSignatures()
    {
        await using ChatDbContext db = CreateDb();
        UserId uploader = UserId.New();
        Conversation conversation = await AddConversationAsync(db, uploader, UserId.New());
        var handler = CreateUploadHandler(db);

        (byte[] Bytes, string ContentType, string Extension)[] samples =
        [
            (PngBytes, "image/png", ".png"),
            (WebpBytes, "image/webp", ".webp"),
            (Mp4Bytes, "video/mp4", ".mp4"),
            (PdfBytes, "application/pdf", ".pdf")
        ];

        foreach ((byte[] bytes, string contentType, string extension) in samples)
        {
            var result = await handler.Handle(
                new UploadAttachmentCommand(
                    uploader,
                    conversation.Id,
                    $"sample{extension}",
                    contentType,
                    bytes.Length,
                    new MemoryStream(bytes)),
                default);

            Assert.True(result.IsSuccess, contentType);
            Assert.Equal(contentType, result.Value.ContentType);
        }

        Assert.Equal(4, await db.Attachments.CountAsync());

        Attachment video = await db.Attachments
            .SingleAsync(item => item.ContentType == "video/mp4");
        Assert.EndsWith(".mp4", video.StorageKey);
        Assert.True(File.Exists(ResolvePrivatePath(video.StorageKey)));
    }

    [Fact]
    public async Task Upload_RejectsOversizedImageWithTheTooLargeCode()
    {
        await using ChatDbContext db = CreateDb();
        UserId uploader = UserId.New();
        Conversation conversation = await AddConversationAsync(db, uploader, UserId.New());
        var handler = CreateUploadHandler(db);

        byte[] oversized = new byte[UploadAttachmentCommandHandler.MaximumImageBytes + 1];
        oversized[0] = 0xff;
        oversized[1] = 0xd8;
        oversized[2] = 0xff;

        var result = await handler.Handle(
            new UploadAttachmentCommand(
                uploader,
                conversation.Id,
                "huge.jpg",
                "image/jpeg",
                oversized.Length,
                new MemoryStream(oversized)),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(AttachmentErrors.TooLarge, result.Error);
        Assert.Equal(0, await db.Attachments.CountAsync());
        AssertNoStoredFiles();
    }

    [Fact]
    public async Task Upload_RejectsSignatureMismatchWithTheInvalidCode()
    {
        await using ChatDbContext db = CreateDb();
        UserId uploader = UserId.New();
        Conversation conversation = await AddConversationAsync(db, uploader, UserId.New());
        var handler = CreateUploadHandler(db);

        byte[] executable = [(byte)'M', (byte)'Z', 0x00, 0x01];
        var result = await handler.Handle(
            new UploadAttachmentCommand(
                uploader,
                conversation.Id,
                "invoice.png",
                "image/png",
                executable.Length,
                new MemoryStream(executable)),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(AttachmentErrors.Invalid, result.Error);
        Assert.Equal(0, await db.Attachments.CountAsync());
        AssertNoStoredFiles();
    }

    [Fact]
    public async Task Upload_RejectsUnsupportedContentTypeWithItsOwnCode()
    {
        await using ChatDbContext db = CreateDb();
        UserId uploader = UserId.New();
        Conversation conversation = await AddConversationAsync(db, uploader, UserId.New());
        var handler = CreateUploadHandler(db);

        byte[] text = [(byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o'];
        var result = await handler.Handle(
            new UploadAttachmentCommand(
                uploader,
                conversation.Id,
                "notes.txt",
                "text/plain",
                text.Length,
                new MemoryStream(text)),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(AttachmentErrors.UnsupportedType, result.Error);
        Assert.Equal(0, await db.Attachments.CountAsync());
    }

    [Fact]
    public async Task Upload_UnknownAndForeignConversationsFailWithTheSamePrivacyCode()
    {
        await using ChatDbContext db = CreateDb();
        UserId uploader = UserId.New();
        Conversation conversation = await AddConversationAsync(db, uploader, UserId.New());
        var handler = CreateUploadHandler(db);

        var unknown = await handler.Handle(
            new UploadAttachmentCommand(
                uploader,
                ConversationId.New(),
                "photo.jpg",
                "image/jpeg",
                JpegBytes.Length,
                new MemoryStream(JpegBytes)),
            default);

        var foreign = await handler.Handle(
            new UploadAttachmentCommand(
                UserId.New(),
                conversation.Id,
                "photo.jpg",
                "image/jpeg",
                JpegBytes.Length,
                new MemoryStream(JpegBytes)),
            default);

        Assert.True(unknown.IsFailure);
        Assert.True(foreign.IsFailure);
        Assert.Equal(ConversationErrors.NotFound.Code, unknown.Error.Code);
        Assert.Equal(unknown.Error.Code, foreign.Error.Code);
        Assert.Equal(0, await db.Attachments.CountAsync());
        AssertNoStoredFiles();
    }

    [Fact]
    public async Task Upload_DeletesTheStoredFileWhenDatabaseSaveFails()
    {
        await using ChatDbContext inner = CreateDb();
        UserId uploader = UserId.New();
        Conversation conversation = await AddConversationAsync(inner, uploader, UserId.New());
        var handler = new UploadAttachmentCommandHandler(
            new ThrowingSaveChatDbContext(inner),
            CreateStorage());

        var result = await handler.Handle(
            new UploadAttachmentCommand(
                uploader,
                conversation.Id,
                "photo.jpg",
                "image/jpeg",
                JpegBytes.Length,
                new MemoryStream(JpegBytes)),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(AttachmentErrors.Invalid, result.Error);
        AssertNoStoredFiles(); // the orphaned blob was cleaned up
    }

    // ------------------------------------------------------------------
    // Endpoint 6 — serve handler: participant-only bytes.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Serve_ReturnsTheExactBytesToBothParticipants()
    {
        await using ChatDbContext db = CreateDb();
        UserId uploader = UserId.New();
        UserId peer = UserId.New();
        Conversation conversation = await AddConversationAsync(db, uploader, peer);

        AttachmentUploadResponse uploaded = (await CreateUploadHandler(db).Handle(
            new UploadAttachmentCommand(
                peer, // the OTHER participant may also fetch it
                conversation.Id,
                "shared.png",
                "image/png",
                PngBytes.Length,
                new MemoryStream(PngBytes)),
            default)).Value;

        Attachment stored = await db.Attachments.SingleAsync();
        var handler = new GetAttachmentFileQueryHandler(db, CreateStorage());

        foreach (UserId viewer in new[] { uploader, peer })
        {
            var served = await handler.Handle(
                new GetAttachmentFileQuery(
                    viewer,
                    new AttachmentId(uploaded.AttachmentId)),
                default);

            Assert.True(served.IsSuccess);
            Assert.Equal("image/png", served.Value.ContentType);
            await using (served.Value.Content)
            {
                await using MemoryStream buffer = new();
                await served.Value.Content.CopyToAsync(buffer);
                Assert.Equal(PngBytes, buffer.ToArray());
            }
        }

        Assert.False(stored.IsBound); // serving never binds
    }

    [Fact]
    public async Task Serve_DeniesStrangersUnknownIdsAndForeignConversationsAlike()
    {
        await using ChatDbContext db = CreateDb();
        UserId uploader = UserId.New();
        Conversation conversation = await AddConversationAsync(db, uploader, UserId.New());
        Conversation other = await AddConversationAsync(db, UserId.New(), UserId.New());

        AttachmentUploadResponse uploaded = (await CreateUploadHandler(db).Handle(
            new UploadAttachmentCommand(
                uploader,
                conversation.Id,
                "photo.jpg",
                "image/jpeg",
                JpegBytes.Length,
                new MemoryStream(JpegBytes)),
            default)).Value;

        AttachmentUploadResponse foreign = (await CreateUploadHandler(db).Handle(
            new UploadAttachmentCommand(
                other.ParticipantOne,
                other.Id,
                "other.png",
                "image/png",
                PngBytes.Length,
                new MemoryStream(PngBytes)),
            default)).Value;

        var handler = new GetAttachmentFileQueryHandler(db, CreateStorage());
        AttachmentId attachmentId = new(uploaded.AttachmentId);
        AttachmentId foreignAttachmentId = new(foreign.AttachmentId);

        var stranger = await handler.Handle(
            new GetAttachmentFileQuery(UserId.New(), attachmentId),
            default);
        var unknownAttachment = await handler.Handle(
            new GetAttachmentFileQuery(uploader, AttachmentId.New()),
            default);
        var foreignConversationAttachment = await handler.Handle(
            new GetAttachmentFileQuery(uploader, foreignAttachmentId),
            default);

        Assert.All(
            new[] { stranger, unknownAttachment, foreignConversationAttachment },
            result =>
            {
                Assert.True(result.IsFailure);
                Assert.Equal(AttachmentErrors.NotFound, result.Error);
            });
    }

    // ------------------------------------------------------------------
    // Single-use binding invariant (domain level).
    // ------------------------------------------------------------------

    [Fact]
    public void Bind_BindsExactlyOnceAndThrowsOnTheSecondAttempt()
    {
        Conversation conversation = Conversation.Create(
            UserId.New(),
            UserId.New(),
            ConversationScope.Direct,
            null,
            UtcStamp);
        Attachment attachment = Attachment.Create(
            conversation.Id,
            UserId.New(),
            "private/chat-attachments/file.webp",
            "image/webp",
            2048,
            UtcStamp);

        attachment.Bind();

        Assert.True(attachment.IsBound);
        Assert.Throws<DomainException>(() => attachment.Bind());
        Assert.True(attachment.IsBound);
    }

    // ------------------------------------------------------------------
    // Endpoints 5 / 6 — controller contract and JWT dispatch.
    // ------------------------------------------------------------------

    [Fact]
    public void ChatController_ExposesEndpoints5And6UnderAuthorizeOnTheChatRoute()
    {
        Assert.Equal(
            "api/v1/chat",
            Assert.Single(typeof(ChatController).GetCustomAttributes<RouteAttribute>()).Template);

        var authorize = Assert.Single(typeof(ChatController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>());
        Assert.Null(authorize.Policy);

        System.Reflection.MethodInfo upload =
            typeof(ChatController).GetMethod(nameof(ChatController.UploadAttachment))!;
        System.Reflection.MethodInfo serve =
            typeof(ChatController).GetMethod(nameof(ChatController.GetAttachmentFile))!;

        Assert.Equal(
            "conversations/{conversationId:guid}/attachments",
            upload.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal(
            "attachments/{attachmentId:guid}/file",
            serve.GetCustomAttribute<HttpGetAttribute>()!.Template);

        Assert.NotNull(upload.GetCustomAttribute<ConsumesAttribute>());
        // Transport framing capped to the 50 MB video ceiling; the per-type
        // numeric limits themselves are enforced and tested in the handler.
        Assert.NotNull(upload.GetCustomAttribute<RequestSizeLimitAttribute>());

        Assert.Contains(upload.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status201Created);
        Assert.Contains(upload.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status400BadRequest);
        Assert.Contains(upload.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status404NotFound);
        Assert.Contains(serve.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status200OK);
        Assert.Contains(serve.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status404NotFound);

        // Endpoint 5 owns no other route; Lane B slots stay untouched.
        Assert.DoesNotContain(typeof(ChatController).GetMethods(),
            method => method.GetCustomAttribute<HttpPostAttribute>()?.Template
                ?.StartsWith("conversations/{conversationId:guid}/attachments") == true &&
                method != upload);
    }

    [Fact]
    public async Task UploadAttachment_RequiresAJwtDispatchesTheMultipartFileAndAnswers201()
    {
        AttachmentUploadResponse response = new(
            Guid.NewGuid(), "photo.jpg", "image/jpeg", JpegBytes.Length);
        var sender = new CapturingSender(
            Result<AttachmentUploadResponse>.Success(response));

        Assert.IsType<UnauthorizedResult>(
            await CreateController(sender).UploadAttachment(
                Guid.NewGuid(), CreateFormFile(JpegBytes), default));

        Guid conversationId = Guid.NewGuid();
        UserId actor = UserId.New();
        ChatController controller = CreateController(sender, actor);

        IActionResult created = await controller.UploadAttachment(
            conversationId, CreateFormFile(JpegBytes), default);

        ObjectResult objectResult = Assert.IsType<ObjectResult>(created);
        Assert.Equal(StatusCodes.Status201Created, objectResult.StatusCode);
        Assert.Same(response, objectResult.Value);

        UploadAttachmentCommand command =
            Assert.IsType<UploadAttachmentCommand>(sender.LastRequest);
        Assert.Equal(actor, command.Requester);
        Assert.Equal(new ConversationId(conversationId), command.ConversationId);
        Assert.Equal("image/jpeg", command.ContentType);
        Assert.Equal(JpegBytes.Length, command.Length);

        ChatController missing = CreateController(sender, actor);
        IActionResult badRequest = await missing.UploadAttachment(
            conversationId, null, default);
        var problem = Assert.IsType<ProblemDetails>(
            Assert.IsType<BadRequestObjectResult>(badRequest).Value);
        Assert.Equal(AttachmentErrors.Invalid.Code, problem.Extensions["code"]);
    }

    [Fact]
    public async Task GetAttachmentFile_SetsNoStoreAndForcesAGenericPrivacy404()
    {
        var sender = new CapturingSender(
            Result<AttachmentFileResponse>.Success(
                new AttachmentFileResponse(
                    "image/jpeg",
                    new MemoryStream(JpegBytes))));
        UserId actor = UserId.New();
        ChatController controller = CreateController(sender, actor);

        IActionResult served = await controller.GetAttachmentFile(
            Guid.NewGuid(), default);

        // Controller action returns File(stream, contentType) → FileStreamResult.
        FileStreamResult fileResult = Assert.IsType<FileStreamResult>(served);
        Assert.Equal("image/jpeg", fileResult.ContentType);
        Assert.True(
            controller.Response.GetTypedHeaders().CacheControl?.NoStore);
        Assert.Equal("no-cache", controller.Response.Headers.Pragma.ToString());

        ChatController denied = CreateController(
            new CapturingSender(Result<AttachmentFileResponse>.Failure(
                AttachmentErrors.NotFound)),
            actor);

        IActionResult notFound = await denied.GetAttachmentFile(
            Guid.NewGuid(), default);

        ObjectResult objectResult = Assert.IsType<ObjectResult>(notFound);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(
            AttachmentErrors.NotFound.Code,
            problemDetails.Extensions["code"]);
        // The detail stays generic — no reason, owner, or existence is revealed.
        Assert.Equal(
            "The requested resource was not found.",
            problemDetails.Detail);
    }

    public void Dispose()
    {
        string privateRoot = PrivateRoot;
        if (Directory.Exists(privateRoot))
        {
            Directory.Delete(privateRoot, recursive: true);
        }

        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    // ------------------------------------------------------------------
    // Harness.
    // ------------------------------------------------------------------

    /// <summary>Mirrors LocalDiskFileStorage.ResolveFullPath for private keys.</summary>
    private string PrivateRoot =>
        _storageRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + "-private";

    private string ResolvePrivatePath(string storageKey) =>
        Path.Combine(
            PrivateRoot,
            storageKey.Replace('/', Path.DirectorySeparatorChar));

    private void AssertNoStoredFiles()
    {
        if (!Directory.Exists(PrivateRoot))
        {
            return;
        }

        Assert.Empty(Directory.GetFiles(PrivateRoot, "*", SearchOption.AllDirectories));
    }

    private IFileStorage CreateStorage() =>
        new LocalDiskFileStorage(
            Options.Create(
                new LocalStorageOptions { RootPath = _storageRoot }));

    private UploadAttachmentCommandHandler CreateUploadHandler(ChatDbContext db) =>
        new(db, CreateStorage());

    private static async Task<Conversation> AddConversationAsync(
        ChatDbContext db,
        UserId participantA,
        UserId participantB)
    {
        Conversation conversation = Conversation.Create(
            participantA,
            participantB,
            ConversationScope.Direct,
            null,
            UtcStamp);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        return conversation;
    }

    private static ChatDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static IFormFile CreateFormFile(byte[] bytes)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(
            stream,
            baseStreamOffset: 0,
            length: bytes.Length,
            name: "file",
            fileName: "photo.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };
    }

    private static ChatController CreateController(ISender sender, UserId? actor = null)
    {
        var controller = new ChatController(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        if (actor.HasValue)
        {
            controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(JwtRegisteredClaimNames.Sub, actor.Value.Value.ToString())],
                    "test"));
        }

        return controller;
    }

    /// <summary>
    /// Simulates a database rejection after the blob was written so the
    /// handler's storage-cleanup path can be proven without touching the
    /// real context.
    /// </summary>
    private sealed class ThrowingSaveChatDbContext(ChatDbContext inner) : IChatDbContext
    {
        public DbSet<Conversation> Conversations => inner.Conversations;

        public DbSet<Attachment> Attachments => inner.Attachments;

        public DbSet<Block> Blocks => inner.Blocks;

        public DbSet<ConversationReadState> ReadStates => inner.ReadStates;

        // Lane B extended IChatDbContext with the message/report tables; this
        // fake only ever runs the upload path, so it simply forwards them.
        public DbSet<Message> Messages => inner.Messages;

        public DbSet<Report> Reports => inner.Reports;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new DbUpdateException(
                "Simulated database failure.",
                new Exception("inner"));
    }

    private sealed class CapturingSender(object response) : ISender
    {
        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

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
