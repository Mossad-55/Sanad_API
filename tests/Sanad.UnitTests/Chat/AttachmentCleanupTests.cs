using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Infrastructure.Storage;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Attachments;
using Sanad.Modules.Chat.Application.RateLimiting;
using Sanad.Modules.Chat.Domain.Attachments;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Infrastructure;
using Sanad.Modules.Chat.Infrastructure.Attachments;
using Sanad.Modules.Chat.Infrastructure.Persistence;
using Sanad.Modules.Chat.Infrastructure.Storage;

namespace Sanad.UnitTests.Chat;

/// <summary>
/// Orphan-sweep, expiry-boundary, quota, and recovery tests over an EF
/// InMemory ChatDbContext plus a REAL temp-root private storage. The
/// PostgreSQL bind-vs-cleanup race (row-level FOR UPDATE SKIP LOCKED +
/// IsBound concurrency token) is proven separately on the authorized
/// relational target; InMemory has no row locking to prove.
/// </summary>
public sealed class AttachmentCleanupTests : IDisposable
{
    private static readonly byte[] JpegBytes = [0xff, 0xd8, 0xff, 0x00, 0x01, 0x02];

    private static DateTime PinnedNow =>
        DateTime.SpecifyKind(new DateTime(2026, 10, 13, 12, 0, 0), DateTimeKind.Utc);

    private readonly string _storageRoot = Path.Combine(
        Path.GetTempPath(),
        $"chat-cleanup-test-{Guid.NewGuid():N}");

    [Fact]
    public void CleanupService_ResolvesFromProductionChatInfrastructureRegistration()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ChatDatabase"] =
                    "Host=localhost;Database=chat_cleanup_di_test;Username=test;Password=test"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<LocalStorageOptions>(
            configuration.GetSection(LocalStorageOptions.SectionName));
        services.AddChatInfrastructure(configuration);

        using ServiceProvider provider = services.BuildServiceProvider(
            validateScopes: true);
        using IServiceScope scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider
            .GetRequiredService<ChatAttachmentCleanupService>());
    }

    [Fact]
    public async Task CleanupStorage_RejectsForeignAndEscapingKeys()
    {
        string foreignKey = $"private/medical-reports/{Guid.NewGuid():N}.pdf";
        string foreignPath = ResolvePrivatePath(foreignKey);
        Directory.CreateDirectory(Path.GetDirectoryName(foreignPath)!);
        await File.WriteAllBytesAsync(foreignPath, [0x01, 0x02]);

        IChatAttachmentCleanupStorage storage = CreateCleanupStorage();

        Assert.Equal(
            ChatAttachmentDeletionOutcome.Failed,
            await storage.DeleteExpiredUnboundAsync(foreignKey, default));
        Assert.Equal(
            ChatAttachmentDeletionOutcome.Failed,
            await storage.DeleteExpiredUnboundAsync(
                "private/chat-attachments/../../private/medical-reports/protected.pdf",
                default));
        Assert.True(File.Exists(foreignPath));
    }

    // ------------------------------------------------------------------
    // Sweep deletes expired unbound blobs + rows; keeps live/bound.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Sweep_DeletesExpiredUnboundBlobsAndRows()
    {
        await using ChatDbContext db = CreateDb();
        UserId uploader = UserId.New();
        Conversation conversation = await AddConversationAsync(db, uploader, UserId.New());

        Attachment expired = await SeedAttachmentWithBlobAsync(
            db, uploader, conversation,
            PinnedNow - TimeSpan.FromHours(25),
            JpegBytes);

        string blobPath = ResolvePrivatePath(expired.StorageKey);
        Assert.True(File.Exists(blobPath));

        var sweep = CreateCleanupService(db, () => PinnedNow);
        ChatAttachmentCleanupSweepResult result = await sweep.SweepAsync(default);

        Assert.Equal(1, result.DeletedCount);
        Assert.Equal(expired.SizeBytes, result.DeletedBytes);
        Assert.Equal(0, result.FailureCount);
        Assert.Equal(0, await db.Attachments.CountAsync());
        Assert.False(File.Exists(blobPath));
    }

    [Fact]
    public async Task Sweep_IgnoresLiveUnboundBoundAndOtherBlobs()
    {
        await using ChatDbContext db = CreateDb();
        UserId uploader = UserId.New();
        Conversation conversation = await AddConversationAsync(db, uploader, UserId.New());

        // Live (1h old) unbound: kept.
        Attachment live = await SeedAttachmentWithBlobAsync(
            db, uploader, conversation,
            PinnedNow - TimeSpan.FromHours(1),
            JpegBytes);

        // Expired but bound: kept (history never expires).
        Attachment bound = await SeedAttachmentWithBlobAsync(
            db, uploader, conversation,
            PinnedNow - TimeSpan.FromHours(30),
            JpegBytes);
        bound.Bind();
        await db.SaveChangesAsync();

        // A foreign private blob (medical report) is never touched.
        string foreignKey = $"private/medical-reports/{Guid.NewGuid():N}.pdf";
        string foreignPath = Path.Combine(
            PrivateRoot,
            foreignKey.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(foreignPath)!);
        await File.WriteAllBytesAsync(foreignPath, [0x01, 0x02]);

        var sweep = CreateCleanupService(db, () => PinnedNow);
        ChatAttachmentCleanupSweepResult result = await sweep.SweepAsync(default);

        Assert.Equal(0, result.DeletedCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Equal(2, await db.Attachments.CountAsync());
        Assert.True(File.Exists(ResolvePrivatePath(live.StorageKey)));
        Assert.True(File.Exists(ResolvePrivatePath(bound.StorageKey)));
        Assert.True(File.Exists(foreignPath));
    }

    // ------------------------------------------------------------------
    // Boundary expiry: UploadedOnUtc <= now-24h is expired.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Sweep_BoundaryExpiryDeletesExactlyAtCutoff()
    {
        await using ChatDbContext db = CreateDb();
        UserId uploader = UserId.New();
        Conversation conversation = await AddConversationAsync(db, uploader, UserId.New());
        DateTime cutoff = PinnedNow - TimeSpan.FromHours(24);

        Attachment atCutoff = await SeedAttachmentWithBlobAsync(
            db, uploader, conversation, cutoff, JpegBytes);
        Attachment oneSecondLive = await SeedAttachmentWithBlobAsync(
            db, uploader, conversation, cutoff.AddSeconds(1), JpegBytes);

        var sweep = CreateCleanupService(db, () => PinnedNow);
        ChatAttachmentCleanupSweepResult result = await sweep.SweepAsync(default);

        Assert.Equal(1, result.DeletedCount);
        Assert.Null(await db.Attachments.SingleOrDefaultAsync(item => item.Id == atCutoff.Id));
        Assert.NotNull(await db.Attachments.SingleOrDefaultAsync(item => item.Id == oneSecondLive.Id));
        Assert.False(File.Exists(ResolvePrivatePath(atCutoff.StorageKey)));
        Assert.True(File.Exists(ResolvePrivatePath(oneSecondLive.StorageKey)));
    }

    // ------------------------------------------------------------------
    // Missing blob: row is still deleted (recoverable).
    // ------------------------------------------------------------------

    [Fact]
    public async Task Sweep_MissingBlobStillDeletesRow()
    {
        await using ChatDbContext db = CreateDb();
        UserId uploader = UserId.New();
        Conversation conversation = await AddConversationAsync(db, uploader, UserId.New());

        Attachment orphan = await SeedAttachmentWithBlobAsync(
            db, uploader, conversation,
            PinnedNow - TimeSpan.FromHours(26),
            JpegBytes);

        // Simulate a crash after the blob delete but before the row delete.
        File.Delete(ResolvePrivatePath(orphan.StorageKey));

        var sweep = CreateCleanupService(db, () => PinnedNow);
        ChatAttachmentCleanupSweepResult result = await sweep.SweepAsync(default);

        Assert.Equal(1, result.DeletedCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Equal(0, await db.Attachments.CountAsync());
    }

    // ------------------------------------------------------------------
    // Failed deletion: row is kept for the next pass (retry).
    // ------------------------------------------------------------------

    [Fact]
    public async Task Sweep_FailedDeletionKeepsRowForRetry()
    {
        await using ChatDbContext db = CreateDb();
        UserId uploader = UserId.New();
        Conversation conversation = await AddConversationAsync(db, uploader, UserId.New());

        Attachment expired = await SeedAttachmentWithBlobAsync(
            db, uploader, conversation,
            PinnedNow - TimeSpan.FromHours(25),
            JpegBytes);

        var failing = new AlwaysFailingCleanupStorage();
        var sweep = CreateCleanupService(db, () => PinnedNow, failing);
        ChatAttachmentCleanupSweepResult failed = await sweep.SweepAsync(default);

        Assert.Equal(0, failed.DeletedCount);
        Assert.Equal(1, failed.FailureCount);
        Assert.Equal(1, await db.Attachments.CountAsync());
        Assert.True(File.Exists(ResolvePrivatePath(expired.StorageKey)));

        // Next pass with a working storage recovers the row.
        var retry = CreateCleanupService(db, () => PinnedNow);
        ChatAttachmentCleanupSweepResult recovered = await retry.SweepAsync(default);

        Assert.Equal(1, recovered.DeletedCount);
        Assert.Equal(0, await db.Attachments.CountAsync());
        Assert.False(File.Exists(ResolvePrivatePath(expired.StorageKey)));
    }

    // ------------------------------------------------------------------
    // Bounded batch: at most 100 objects per pass.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Sweep_DeletesAtMostBatchSizePerPass()
    {
        await using ChatDbContext db = CreateDb();
        UserId uploader = UserId.New();
        Conversation conversation = await AddConversationAsync(db, uploader, UserId.New());

        for (int index = 0; index < 105; index++)
        {
            await SeedAttachmentWithBlobAsync(
                db, uploader, conversation,
                PinnedNow - TimeSpan.FromHours(25) - TimeSpan.FromMinutes(index),
                JpegBytes);
        }

        var sweep = CreateCleanupService(
            db, () => PinnedNow,
            batchSize: 100);
        ChatAttachmentCleanupSweepResult first = await sweep.SweepAsync(default);

        Assert.Equal(100, first.DeletedCount);
        Assert.Equal(5, await db.Attachments.CountAsync());

        ChatAttachmentCleanupSweepResult second = await sweep.SweepAsync(default);

        Assert.Equal(5, second.DeletedCount);
        Assert.Equal(0, await db.Attachments.CountAsync());
    }

    // ------------------------------------------------------------------
    // Quota: expired-but-not-swept bytes still count (physical bound).
    // ------------------------------------------------------------------

    [Fact]
    public async Task Upload_ExpiredUnboundBytesStillCountTowardQuota()
    {
        await using ChatDbContext db = CreateDb();
        UserId uploader = UserId.New();
        Conversation conversation = await AddConversationAsync(db, uploader, UserId.New());

        // Expired (30h) but not yet swept: 60 bytes still physically occupied.
        byte[] sixtyBytes = new byte[60];
        sixtyBytes[0] = 0xff;
        sixtyBytes[1] = 0xd8;
        sixtyBytes[2] = 0xff;
        await SeedAttachmentWithBlobAsync(
            db, uploader, conversation,
            PinnedNow - TimeSpan.FromHours(30),
            sixtyBytes);

        var handler = new UploadAttachmentCommandHandler(
            db,
            CreateFileStorage(),
            rateOptions: new ChatRateOptions { UnboundQuotaBytesPerUser = 100 });

        // 60 (expired, still on disk) + 50 (new) = 110 > 100 => rejected.
        byte[] fiftyBytes = new byte[50];
        fiftyBytes[0] = 0xff;
        fiftyBytes[1] = 0xd8;
        fiftyBytes[2] = 0xff;
        var rejected = await handler.Handle(
            new UploadAttachmentCommand(
                uploader,
                conversation.Id,
                "photo.jpg",
                "image/jpeg",
                fiftyBytes.Length,
                new MemoryStream(fiftyBytes)),
            default);

        Assert.True(rejected.IsFailure);
        Assert.Equal(AttachmentErrors.QuotaExceeded, rejected.Error);

        // After the sweep reclaims the expired bytes, the same upload fits.
        var sweep = CreateCleanupService(
            db, () => PinnedNow,
            rateOptions: new ChatRateOptions { UnboundQuotaBytesPerUser = 100 });
        await sweep.SweepAsync(default);

        var accepted = await handler.Handle(
            new UploadAttachmentCommand(
                uploader,
                conversation.Id,
                "photo.jpg",
                "image/jpeg",
                fiftyBytes.Length,
                new MemoryStream(fiftyBytes)),
            default);

        Assert.True(accepted.IsSuccess);
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

    private Sanad.BuildingBlocks.Application.Abstractions.Storage.IFileStorage CreateFileStorage() =>
        new Sanad.BuildingBlocks.Infrastructure.Storage.LocalDiskFileStorage(
            Options.Create(
                new Sanad.BuildingBlocks.Infrastructure.Storage.LocalStorageOptions
                {
                    RootPath = _storageRoot
                }));

    private IChatAttachmentCleanupStorage CreateCleanupStorage() =>
        new LocalDiskChatAttachmentCleanupStorage(
            Options.Create(
                new Sanad.BuildingBlocks.Infrastructure.Storage.LocalStorageOptions
                {
                    RootPath = _storageRoot
                }));

    private ChatAttachmentCleanupService CreateCleanupService(
        ChatDbContext db,
        Func<DateTime> utcNow,
        IChatAttachmentCleanupStorage? storage = null,
        ChatAttachmentCleanupOptions? options = null,
        ChatRateOptions? rateOptions = null,
        int? batchSize = null) =>
        new(
            db,
            storage ?? CreateCleanupStorage(),
            new RecordingCleanupMetrics(),
            Options.Create(options ?? new ChatAttachmentCleanupOptions
            {
                BatchSize = batchSize ?? 100
            }),
            Options.Create(rateOptions ?? new ChatRateOptions()),
            utcNow,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<
                Sanad.Modules.Chat.Infrastructure.Attachments.ChatAttachmentCleanupService>.Instance);

    private static ChatDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

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
            PinnedNow);
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

        string fullPath = ResolvePrivatePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, content);

        return attachment;
    }

    private sealed class AlwaysFailingCleanupStorage : IChatAttachmentCleanupStorage
    {
        public Task<ChatAttachmentDeletionOutcome> DeleteExpiredUnboundAsync(
            string storageKey,
            CancellationToken cancellationToken) =>
            Task.FromResult(ChatAttachmentDeletionOutcome.Failed);
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
