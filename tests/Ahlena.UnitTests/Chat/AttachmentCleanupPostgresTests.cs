using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Application.Attachments;
using Ahlena.Modules.Chat.Application.Messages;
using Ahlena.Modules.Chat.Application.RateLimiting;
using Ahlena.Modules.Chat.Domain.Attachments;
using Ahlena.Modules.Chat.Domain.Conversations;
using Ahlena.Modules.Chat.Domain.Messages;
using Ahlena.Modules.Chat.Infrastructure.Attachments;
using Ahlena.Modules.Chat.Infrastructure.Persistence;
using Ahlena.Modules.Chat.Infrastructure.Storage;

namespace Ahlena.UnitTests.Chat;

/// <summary>
/// Bind-vs-cleanup race on the authorized relational target (local PostgreSQL).
/// The sweep's <c>SELECT … FOR UPDATE SKIP LOCKED</c> claim and the send's
/// conditional bind (<c>UPDATE … WHERE IsBound = FALSE</c> via the IsBound
/// concurrency token) serialize on the row lock. A file bound to a message is
/// never deleted by the racing sweep. Temporary storage roots only; the test
/// database is a separate disposable database, reset per test.
/// </summary>
[Collection("ChatPostgresCleanup")]
public sealed class AttachmentCleanupPostgresTests : IDisposable
{
    private readonly ChatPostgresCleanupFixture _fixture;

    private readonly string _storageRoot = Path.Combine(
        Path.GetTempPath(),
        $"chat-cleanup-pg-{Guid.NewGuid():N}");

    public AttachmentCleanupPostgresTests(ChatPostgresCleanupFixture fixture)
    {
        _fixture = fixture;
    }

    [ChatPostgresFact]
    public async Task BindVsCleanupRace_NeverOrphansBoundMessage()
    {
        await _fixture.ResetAsync();
        await using ChatDbContext seedDb = _fixture.CreateDb();

        DateTime realNow = DateTime.UtcNow;
        UserId sender = UserId.New();
        UserId peer = UserId.New();
        Conversation conversation = await AddConversationAsync(seedDb, sender, peer);

        // Boundary: live for the send (5 min left) but expired for a sweep
        // whose clock is pinned 10 min ahead.
        DateTime uploadedOnUtc = realNow - TimeSpan.FromHours(24) + TimeSpan.FromMinutes(5);
        Attachment attachment = await SeedAttachmentWithBlobAsync(
            seedDb, sender, conversation, uploadedOnUtc, [0xff, 0xd8, 0xff, 0x00]);

        string blobPath = ResolvePrivatePath(attachment.StorageKey);
        Assert.True(File.Exists(blobPath));
        Guid attachmentId = attachment.Id.Value;

        DateTime sweepNow = realNow + TimeSpan.FromMinutes(10);

        // Two independent connections: the send and the sweep race on the row.
        await using ChatDbContext sendDb = _fixture.CreateDb();
        await using ChatDbContext sweepDb = _fixture.CreateDb();

        var sendHandler = new SendMessageCommandHandler(
            sendDb,
            null, null,
            new Ahlena.Modules.Chat.Application.Events.ChatEventRecorder(sendDb));

        var sweep = CreateCleanupService(sweepDb, () => sweepNow);

        Task<Ahlena.BuildingBlocks.Application.Results.Result<SendMessageResponse>> sendTask =
            sendHandler.Handle(
                new SendMessageCommand(
                    sender, conversation.Id, $"race-{Guid.NewGuid():N}", "File", null,
                    new AttachmentId(attachmentId)),
                default);

        Task<ChatAttachmentCleanupSweepResult> sweepTask =
            sweep.SweepAsync(default);

        await Task.WhenAll(sendTask, sweepTask);

        var sendResult = await sendTask;
        var sweepResult = await sweepTask;

        // Invariant: a bound message never references a missing blob.
        await using ChatDbContext verifyDb = _fixture.CreateDb();
        Attachment? finalAttachment = await verifyDb.Attachments
            .SingleOrDefaultAsync(item => item.Id == new AttachmentId(attachmentId));
        Message? message = await verifyDb.Messages
            .SingleOrDefaultAsync(item =>
                item.ConversationId == conversation.Id &&
                item.AttachmentId == new AttachmentId(attachmentId));

        if (sendResult.IsSuccess)
        {
            // The send won: the attachment is bound, the blob is intact, and
            // the sweep did not delete it.
            Assert.NotNull(message);
            Assert.NotNull(finalAttachment);
            Assert.True(finalAttachment.IsBound);
            Assert.True(File.Exists(blobPath));
            Assert.Equal(0, sweepResult.DeletedCount);
        }
        else
        {
            // The sweep won: the send failed with 404 and no message was
            // created. The attachment is either deleted or (if the sweep
            // skipped the locked row) still unbound — but never bound with a
            // missing blob.
            Assert.Equal(AttachmentErrors.NotFound, sendResult.Error);
            Assert.Null(message);

            if (finalAttachment is not null)
            {
                Assert.False(finalAttachment.IsBound);
            }
            else
            {
                Assert.False(File.Exists(blobPath));
            }
        }
    }

    [ChatPostgresFact]
    public async Task SweepFirst_SendFailsWithNotFound()
    {
        await _fixture.ResetAsync();
        await using ChatDbContext seedDb = _fixture.CreateDb();

        UserId sender = UserId.New();
        Conversation conversation = await AddConversationAsync(seedDb, sender, UserId.New());

        Attachment attachment = await SeedAttachmentWithBlobAsync(
            seedDb, sender, conversation,
            DateTime.UtcNow - TimeSpan.FromHours(25),
            [0xff, 0xd8, 0xff, 0x00]);

        // The sweep deletes the expired attachment first.
        await using ChatDbContext sweepDb = _fixture.CreateDb();
        var sweep = CreateCleanupService(sweepDb, () => DateTime.UtcNow);
        ChatAttachmentCleanupSweepResult swept = await sweep.SweepAsync(default);
        Assert.Equal(1, swept.DeletedCount);

        // A later send cannot bind the deleted attachment.
        await using ChatDbContext sendDb = _fixture.CreateDb();
        var sendHandler = new SendMessageCommandHandler(sendDb);
        var result = await sendHandler.Handle(
            new SendMessageCommand(
                sender, conversation.Id, "late-key", "File", null, attachment.Id),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(AttachmentErrors.NotFound, result.Error);
    }

    [ChatPostgresFact]
    public async Task SendFirst_SweepPreservesBoundAttachment()
    {
        await _fixture.ResetAsync();
        await using ChatDbContext seedDb = _fixture.CreateDb();

        UserId sender = UserId.New();
        Conversation conversation = await AddConversationAsync(seedDb, sender, UserId.New());

        Attachment attachment = await SeedAttachmentWithBlobAsync(
            seedDb, sender, conversation,
            DateTime.UtcNow - TimeSpan.FromHours(1),
            [0xff, 0xd8, 0xff, 0x00]);

        string blobPath = ResolvePrivatePath(attachment.StorageKey);

        // The send binds the live attachment first.
        await using ChatDbContext sendDb = _fixture.CreateDb();
        var sendHandler = new SendMessageCommandHandler(sendDb);
        var sent = await sendHandler.Handle(
            new SendMessageCommand(
                sender, conversation.Id, "bound-key", "File", null, attachment.Id),
            default);

        Assert.True(sent.IsSuccess);

        // A later sweep (even with a far-future clock) never deletes it.
        await using ChatDbContext sweepDb = _fixture.CreateDb();
        var sweep = CreateCleanupService(
            sweepDb, () => DateTime.UtcNow + TimeSpan.FromDays(30));
        ChatAttachmentCleanupSweepResult swept = await sweep.SweepAsync(default);

        Assert.Equal(0, swept.DeletedCount);

        await using ChatDbContext verifyDb = _fixture.CreateDb();
        Attachment? final = await verifyDb.Attachments
            .SingleOrDefaultAsync(item => item.Id == attachment.Id);
        Assert.NotNull(final);
        Assert.True(final.IsBound);
        Assert.True(File.Exists(blobPath));
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

    private string PrivateRoot =>
        _storageRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + "-private";

    private string ResolvePrivatePath(string storageKey) =>
        Path.Combine(
            PrivateRoot,
            storageKey.Replace('/', Path.DirectorySeparatorChar));

    private IChatAttachmentCleanupStorage CreateCleanupStorage() =>
        new LocalDiskChatAttachmentCleanupStorage(
            Options.Create(
                new Ahlena.BuildingBlocks.Infrastructure.Storage.LocalStorageOptions
                {
                    RootPath = _storageRoot
                }));

    private ChatAttachmentCleanupService CreateCleanupService(
        ChatDbContext db,
        Func<DateTime> utcNow) =>
        new(
            db,
            CreateCleanupStorage(),
            new RecordingCleanupMetrics(),
            Options.Create(new ChatAttachmentCleanupOptions()),
            Options.Create(new ChatRateOptions()),
            utcNow,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<
                Ahlena.Modules.Chat.Infrastructure.Attachments.ChatAttachmentCleanupService>.Instance);

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
            DateTime.UtcNow);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        return conversation;
    }

    private async Task<Attachment> SeedAttachmentWithBlobAsync(
        ChatDbContext db,
        UserId uploader,
        Conversation conversation,
        DateTime uploadedOnUtc,
        byte[] content)
    {
        string storageKey =
            $"private/chat-attachments/{Guid.NewGuid():N}.jpg";

        Attachment attachment = Attachment.Create(
            conversation.Id,
            uploader,
            storageKey,
            "image/jpeg",
            content.Length,
            DateTime.SpecifyKind(uploadedOnUtc, DateTimeKind.Utc));

        db.Attachments.Add(attachment);
        await db.SaveChangesAsync();

        // Detach so later contexts see committed state, not tracked entities.
        db.ChangeTracker.Clear();

        string fullPath = ResolvePrivatePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, content);

        return attachment;
    }

    private sealed class RecordingCleanupMetrics : IChatAttachmentCleanupMetrics
    {
        public Task<ChatAttachmentCleanupBacklog> GetBacklogAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new ChatAttachmentCleanupBacklog(0, 0));

        public void RecordSweep(ChatAttachmentCleanupSweepResult result)
        {
        }
    }
}
