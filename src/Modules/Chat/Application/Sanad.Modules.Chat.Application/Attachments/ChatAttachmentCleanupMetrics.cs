using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Application.RateLimiting;

namespace Sanad.Modules.Chat.Application.Attachments;

/// <summary>
/// Default sanitized metrics: the backlog is computed from the Chat database
/// and every sweep pass is logged as aggregate counts/bytes. No user ids,
/// storage keys, conversation ids, or message payloads are ever recorded.
/// </summary>
public sealed class ChatAttachmentCleanupMetrics : IChatAttachmentCleanupMetrics
{
    private readonly IChatDbContext _dbContext;
    private readonly ChatRateOptions _rateOptions;
    private readonly ILogger<ChatAttachmentCleanupMetrics> _logger;

    public ChatAttachmentCleanupMetrics(
        IChatDbContext dbContext,
        IOptions<ChatRateOptions> rateOptions,
        ILogger<ChatAttachmentCleanupMetrics> logger)
    {
        _dbContext = dbContext;
        _rateOptions = rateOptions.Value;
        _logger = logger;
    }

    public async Task<ChatAttachmentCleanupBacklog> GetBacklogAsync(
        CancellationToken cancellationToken)
    {
        DateTime cutoff = DateTime.UtcNow - _rateOptions.UnboundAttachmentLifetime;

        int count = await _dbContext.Attachments
            .Where(item => !item.IsBound && item.UploadedOnUtc <= cutoff)
            .CountAsync(cancellationToken);

        long bytes = await _dbContext.Attachments
            .Where(item => !item.IsBound && item.UploadedOnUtc <= cutoff)
            .SumAsync(item => (long?)item.SizeBytes, cancellationToken)
            ?? 0;

        return new ChatAttachmentCleanupBacklog(count, bytes);
    }

    public void RecordSweep(ChatAttachmentCleanupSweepResult result)
    {
        _logger.LogInformation(
            "Chat attachment orphan sweep deleted {DeletedCount} objects ({DeletedBytes} bytes) with {FailureCount} failures.",
            result.DeletedCount,
            result.DeletedBytes,
            result.FailureCount);
    }
}
