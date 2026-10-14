using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Application.Attachments;
using Sanad.Modules.Chat.Application.RateLimiting;
using Sanad.Modules.Chat.Domain.Attachments;
using Sanad.Modules.Chat.Infrastructure.Persistence;

namespace Sanad.Modules.Chat.Infrastructure.Attachments;

/// <summary>One claimed expired-unbound attachment awaiting physical deletion.</summary>
internal sealed class ExpiredAttachmentRow
{
    public Guid Id { get; set; }

    public string StorageKey { get; set; } = string.Empty;

    public long SizeBytes { get; set; }
}

/// <summary>
/// Bounded, crash-safe orphan sweep. Every pass claims at most
/// <c>ChatAttachmentCleanupOptions.BatchSize</c> expired unbound attachments,
/// deletes their blobs, then deletes the rows.
///
/// Race safety (bind vs cleanup): the claim is a single
/// <c>SELECT … FOR UPDATE SKIP LOCKED</c> that locks the claimed rows for the
/// whole sweep transaction. A concurrent send's conditional bind
/// (<c>UPDATE … WHERE IsBound = FALSE</c>, enforced by the IsBound concurrency
/// token) and this claim serialize on the row lock — a file that is bound to
/// a message is never deleted by the racing sweep, and a file the sweep
/// claimed is never silently bound (the send's UPDATE matches 0 rows and the
/// send fails with 404). There is no check-then-delete race.
///
/// Recovery: a crash between the blob delete and the row delete leaves the row
/// (still expired unbound); the next pass re-claims it, finds the blob missing
/// (recoverable), and deletes the row. A failed blob delete keeps its row for
/// the next pass. Only exact proven expired-unbound rows below the private
/// Chat-attachment root are ever deleted — never bound files, messages,
/// reports, general uploads, or directories.
/// </summary>
public sealed class ChatAttachmentCleanupService
{
    private readonly IChatDbContext _dbContext;
    private readonly IChatAttachmentCleanupStorage _storage;
    private readonly IChatAttachmentCleanupMetrics _metrics;
    private readonly ChatAttachmentCleanupOptions _options;
    private readonly ChatRateOptions _rateOptions;
    private readonly Func<DateTime> _utcNow;
    private readonly ILogger<ChatAttachmentCleanupService> _logger;

    public ChatAttachmentCleanupService(
        IChatDbContext dbContext,
        IChatAttachmentCleanupStorage storage,
        IChatAttachmentCleanupMetrics metrics,
        IOptions<ChatAttachmentCleanupOptions> options,
        IOptions<ChatRateOptions> rateOptions,
        Func<DateTime> utcNow,
        ILogger<ChatAttachmentCleanupService> logger)
    {
        _dbContext = dbContext;
        _storage = storage;
        _metrics = metrics;
        _options = options.Value;
        _rateOptions = rateOptions.Value;
        _utcNow = utcNow;
        _logger = logger;
    }

    /// <summary>
    /// Runs one bounded sweep pass. Returns the sanitized sweep result
    /// (deleted count/bytes + failure count). Never throws for per-item blob
    /// failures — they are counted and retried on the next pass.
    /// </summary>
    public async Task<ChatAttachmentCleanupSweepResult> SweepAsync(
        CancellationToken cancellationToken)
    {
        DateTime now = _utcNow();
        DateTime cutoff = now - _rateOptions.UnboundAttachmentLifetime;
        int batchSize = _options.BatchSize;

        DbContext? relational = RelationalContext();

        await using var transaction = relational is null
            ? null
            : await relational.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // 1. Claim (lock) up to batchSize expired unbound attachments. On
            //    PostgreSQL the FOR UPDATE SKIP LOCKED claim holds the row
            //    locks until commit, so a racing bind cannot claim a blob this
            //    sweep is deleting. InMemory has no concurrency to prove.
            List<ExpiredAttachmentRow> claimed = await ClaimExpiredUnboundAsync(
                relational,
                cutoff,
                batchSize,
                cancellationToken);

            if (claimed.Count == 0)
            {
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return new ChatAttachmentCleanupSweepResult(0, 0, 0);
            }

            // 2. Delete the blobs while the rows are locked. A Failed blob keeps
            //    its row (retry next pass); Missing/Deleted allow the row delete.
            List<(ExpiredAttachmentRow Row, ChatAttachmentDeletionOutcome Outcome)> blobResults =
                [];

            foreach (ExpiredAttachmentRow row in claimed)
            {
                ChatAttachmentDeletionOutcome outcome =
                    await _storage.DeleteExpiredUnboundAsync(
                        row.StorageKey,
                        cancellationToken);

                if (outcome == ChatAttachmentDeletionOutcome.Failed)
                {
                    _logger.LogWarning(
                        "Chat attachment orphan sweep failed to delete blob for attachment {AttachmentId}; the row is kept for the next pass.",
                        row.Id);
                }

                blobResults.Add((row, outcome));
            }

            // 3. Delete the DB rows whose blobs resolved (Deleted or Missing).
            //    The IsBound = FALSE guard is a backstop; the claim lock already
            //    excluded racing binds.
            List<Guid> deletableIds = blobResults
                .Where(item => item.Outcome
                    is ChatAttachmentDeletionOutcome.Deleted
                    or ChatAttachmentDeletionOutcome.Missing)
                .Select(item => item.Row.Id)
                .ToList();

            int deletedCount = 0;

            if (deletableIds.Count > 0)
            {
                deletedCount = await DeleteRowsAsync(
                    relational,
                    deletableIds,
                    cancellationToken);
            }

            long deletedBytes = blobResults
                .Where(item => deletableIds.Contains(item.Row.Id))
                .Sum(item => item.Row.SizeBytes);

            int failureCount = blobResults
                .Count(item => item.Outcome == ChatAttachmentDeletionOutcome.Failed);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            var result = new ChatAttachmentCleanupSweepResult(
                deletedCount,
                deletedBytes,
                failureCount);

            _metrics.RecordSweep(result);

            return result;
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
    }

    /// <summary>
    /// Claims expired unbound attachments. Relational providers use a single
    /// atomic <c>SELECT … FOR UPDATE SKIP LOCKED</c> (the locks are held until
    /// the sweep transaction commits); InMemory uses a plain LINQ query.
    /// </summary>
    private async Task<List<ExpiredAttachmentRow>> ClaimExpiredUnboundAsync(
        DbContext? relational,
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (relational is null)
        {
            return await _dbContext.Attachments
                .Where(item => !item.IsBound && item.UploadedOnUtc <= cutoff)
                .OrderBy(item => item.UploadedOnUtc)
                .Take(batchSize)
                .Select(item => new ExpiredAttachmentRow
                {
                    Id = item.Id.Value,
                    StorageKey = item.StorageKey,
                    SizeBytes = item.SizeBytes
                })
                .ToListAsync(cancellationToken);
        }

        return await relational.Database
            .SqlQueryRaw<ExpiredAttachmentRow>(
                """
                SELECT "Id", "StorageKey", "SizeBytes"
                FROM chat."Attachments"
                WHERE "IsBound" = FALSE AND "UploadedOnUtc" <= {0}
                ORDER BY "UploadedOnUtc"
                LIMIT {1}
                FOR UPDATE SKIP LOCKED
                """,
                cutoff,
                batchSize)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes claimed rows. Relational providers run one conditional
    /// <c>DELETE … WHERE IsBound = FALSE</c>; InMemory uses EF change tracking.
    /// </summary>
    private async Task<int> DeleteRowsAsync(
        DbContext? relational,
        List<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (relational is null)
        {
            List<Attachment> rows = await _dbContext.Attachments
                .Where(item => ids.Contains(item.Id.Value))
                .ToListAsync(cancellationToken);

            _dbContext.Attachments.RemoveRange(rows);

            return await _dbContext.SaveChangesAsync(cancellationToken);
        }

        object?[] parameters = [ids.ToArray()];

        return await relational.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM chat."Attachments"
            WHERE "IsBound" = FALSE AND "Id" = ANY({0})
            """,
            parameters,
            cancellationToken);
    }

    private DbContext? RelationalContext() =>
        _dbContext is DbContext context &&
        !string.Equals(
            context.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.InMemory",
            StringComparison.Ordinal)
            ? context
            : null;
}
